// Import DXF vectorial (ghidul BIM §3.1–3.3, Faza 2). Rulează în browser, pe fișierul utilizatorului: planul nu pleacă nicăieri
// (CONSTITUTION art. 14). Parser propriu, minimal, pentru DXF ASCII: LINE, LWPOLYLINE, POLYLINE/VERTEX. Fără dependențe noi.
// Separă extragerea vectorială (parseDxf) de interpretare (wallCandidates) și de aplicare (applyWallCandidates), ca în ghid:
// candidații nu intră în proiect până nu îi aprobă utilizatorul, iar grosimea lipsă nu se inventează.
import type { Confidence, Floor, Wall, WallOrigin } from './types';
import { r3 } from './geometry';

export const DXF_LIMITS = { maxBytes: 10_000_000, maxSegments: 20_000, maxAbsCoord: 1e7, minThicknessM: 0.05, maxThicknessM: 0.6 } as const;
export type DxfUnit = 'mm' | 'cm' | 'm' | 'in' | 'ft';
export const DXF_UNIT_M: Record<DxfUnit, number> = { mm: 0.001, cm: 0.01, m: 1, in: 0.0254, ft: 0.3048 };
const INSUNITS: Record<number, DxfUnit> = { 1: 'in', 2: 'ft', 4: 'mm', 5: 'cm', 6: 'm' };

export interface DxfSegment { layer: string; a: [number, number]; b: [number, number] }
export interface DxfParse { units: DxfUnit | null; segments: DxfSegment[]; layers: string[]; skipped: Record<string, number> }

class DxfError extends Error {}
const fail = (m: string): never => { throw new DxfError(m); };

/** Citește perechile (cod de grup, valoare) și păstrează segmentele în unitățile fișierului (Y în sus, ca în DXF). */
export function parseDxf(text: string): DxfParse {
  if (text.length > DXF_LIMITS.maxBytes) fail('Fișierul DXF este prea mare (maximum 10 MB).');
  if (text.startsWith('AutoCAD Binary DXF')) fail('DXF binar nu este suportat; salvează planul ca DXF ASCII.');
  const lines = text.split(/\r\n|\n|\r/);
  if (!lines.some(l => l.trim() === 'ENTITIES')) fail('Fișierul nu pare un DXF (lipsește secțiunea ENTITIES).');
  let units: DxfUnit | null = null;
  const segments: DxfSegment[] = [], layers = new Set<string>(), skipped: Record<string, number> = {};
  const num = (v: string) => { const n = Number(v); if (!Number.isFinite(n) || Math.abs(n) > DXF_LIMITS.maxAbsCoord) fail('O coordonată din DXF este invalidă sau în afara limitelor.'); return n; };
  const push = (layer: string, a: [number, number], b: [number, number]) => {
    if (a[0] === b[0] && a[1] === b[1]) return;
    if (segments.length >= DXF_LIMITS.maxSegments) fail(`Planul are prea multe entități (maximum ${DXF_LIMITS.maxSegments} segmente).`);
    segments.push({ layer, a, b }); layers.add(layer);
  };
  // entitatea curentă: tipul și perechile ei
  let section = '', type = '', grp: [number, string][] = [];
  let poly: { layer: string; closed: boolean; pts: [number, number][] } | null = null;
  const polyline = (layer: string, pts: [number, number][], closed: boolean) => {
    for (let i = 0; i + 1 < pts.length; i++) push(layer, pts[i]!, pts[i + 1]!);
    if (closed && pts.length > 2) push(layer, pts[pts.length - 1]!, pts[0]!);
  };
  const flush = () => {
    const layer = (grp.find(g => g[0] === 8)?.[1] ?? '0').trim().slice(0, 100) || '0';
    const val = (c: number) => grp.find(g => g[0] === c)?.[1];
    if (section === 'HEADER') {
      const i = grp.findIndex(g => g[0] === 9 && g[1].trim() === '$INSUNITS');
      if (i >= 0) { const v = grp.slice(i + 1).find(g => g[0] === 70); if (v) units = INSUNITS[Number(v[1])] ?? null; }
    } else if (section === 'ENTITIES' && type) {
      if (type === 'LINE') push(layer, [num(val(10) ?? 'x'), num(val(20) ?? 'x')], [num(val(11) ?? 'x'), num(val(21) ?? 'x')]);
      else if (type === 'LWPOLYLINE') {
        const pts: [number, number][] = []; let x: number | null = null;
        for (const [c, v] of grp) { if (c === 10) x = num(v); else if (c === 20 && x != null) { pts.push([x, num(v)]); x = null; } }
        polyline(layer, pts, (Number(val(70) ?? 0) & 1) === 1);
      } else if (type === 'POLYLINE') poly = { layer, closed: (Number(val(70) ?? 0) & 1) === 1, pts: [] };
      else if (type === 'VERTEX' && poly) poly.pts.push([num(val(10) ?? 'x'), num(val(20) ?? 'x')]);
      else if (type === 'SEQEND' && poly) { polyline(poly.layer, poly.pts, poly.closed); poly = null; }
      else skipped[type] = (skipped[type] ?? 0) + 1;
    }
    grp = [];
  };
  for (let i = 0; i + 1 < lines.length; i += 2) {
    const code = Number(lines[i]!.trim()), value = lines[i + 1]!;
    if (!Number.isInteger(code)) fail('Fișierul DXF este corupt (cod de grup invalid).');
    if (code === 0) {
      flush();
      const v = value.trim();
      if (v === 'SECTION') { type = ''; section = '?'; continue; }
      if (v === 'ENDSEC') { section = ''; type = ''; continue; }
      if (v === 'EOF') break;
      type = v; continue;
    }
    if (code === 2 && section === '?') { section = value.trim(); continue; }
    grp.push([code, value]);
  }
  flush();
  return { units, segments, layers: [...layers], skipped };
}

export interface WallCandidate { key: string; layer: string; a: [number, number]; b: [number, number]; thickness: number | null; thicknessFrom: 'source' | null; confidence: Confidence }
export interface CandidateOptions { unit?: DxfUnit | null; layers?: string[]; minLengthM?: number }

type Seg = { layer: string; a: [number, number]; b: [number, number]; len: number; ux: number; uz: number };

/** Interpretarea: două linii paralele, apropiate și suprapuse pe lungime = un perete (axa la mijloc, grosimea = distanța măsurată);
 *  o linie rămasă singură = perete candidat fără grosime. Coordonatele trec în metri, cu Z = −Y (planul se vede de sus). */
export function wallCandidates(p: DxfParse, o: CandidateOptions = {}): WallCandidate[] {
  const unit = o.unit ?? p.units;
  if (!unit) fail('Unitatea desenului nu este declarată în fișier: alege unitatea (mm, cm, m) înainte de import.');
  const k = DXF_UNIT_M[unit!], minLen = o.minLengthM ?? 0.3, want = o.layers ? new Set(o.layers) : null;
  const segs: Seg[] = [];
  for (const s of p.segments) {
    if (want && !want.has(s.layer)) continue;
    const a: [number, number] = [s.a[0] * k, -s.a[1] * k], b: [number, number] = [s.b[0] * k, -s.b[1] * k];
    const len = Math.hypot(b[0] - a[0], b[1] - a[1]); if (len < minLen) continue;
    segs.push({ layer: s.layer, a, b, len, ux: (b[0] - a[0]) / len, uz: (b[1] - a[1]) / len });
  }
  // perechi: paralele (sub ~1°), la distanța unei grosimi de perete, suprapuse pe cel puțin 80% din cea mai scurtă
  const pairs: { i: number; j: number; d: number; t0: number; t1: number }[] = [];
  for (let i = 0; i < segs.length; i++) for (let j = i + 1; j < segs.length; j++) {
    const A = segs[i]!, B = segs[j]!;
    if (Math.abs(A.ux * B.uz - A.uz * B.ux) > 0.0175) continue;
    const nx = -A.uz, nz = A.ux, d = Math.abs((B.a[0] - A.a[0]) * nx + (B.a[1] - A.a[1]) * nz);
    if (d < DXF_LIMITS.minThicknessM || d > DXF_LIMITS.maxThicknessM) continue;
    const proj = (q: [number, number]) => (q[0] - A.a[0]) * A.ux + (q[1] - A.a[1]) * A.uz;
    const b0 = Math.min(proj(B.a), proj(B.b)), b1 = Math.max(proj(B.a), proj(B.b));
    if (Math.min(A.len, b1) - Math.max(0, b0) < 0.8 * Math.min(A.len, B.len)) continue;
    // capetele axei = media capetelor celor două fețe: la un colț (fața interioară mai scurtă, cea exterioară mai lungă cu o
    // grosime) axele pereților vecini se întâlnesc exact în colț; două linii egale dau capetele lor
    pairs.push({ i, j, d, t0: b0 / 2, t1: (A.len + b1) / 2 });
  }
  pairs.sort((x, y) => x.d - y.d);
  const used = new Set<number>(), out: WallCandidate[] = [];
  const pt = (q: [number, number]): [number, number] => [r3(q[0]) + 0, r3(q[1]) + 0];
  for (const pr of pairs) {
    if (used.has(pr.i) || used.has(pr.j)) continue; used.add(pr.i); used.add(pr.j);
    const A = segs[pr.i]!, B = segs[pr.j]!, nx = -A.uz, nz = A.ux;
    const side = Math.sign((B.a[0] - A.a[0]) * nx + (B.a[1] - A.a[1]) * nz) || 1, h = side * pr.d / 2;
    const at = (t: number): [number, number] => [A.a[0] + A.ux * t + nx * h, A.a[1] + A.uz * t + nz * h];
    out.push({ key: `p${pr.i}-${pr.j}`, layer: A.layer, a: pt(at(pr.t0)), b: pt(at(pr.t1)), thickness: r3(pr.d), thicknessFrom: 'source', confidence: 'MEDIUM' });
  }
  segs.forEach((s, i) => { if (!used.has(i)) out.push({ key: `s${i}`, layer: s.layer, a: pt(s.a), b: pt(s.b), thickness: null, thicknessFrom: null, confidence: 'LOW' }); });
  return out;
}

export interface ApplyOptions { sha256: string; offset: [number, number]; manualThicknessM?: number; exterior?: boolean; takenIds?: Iterable<string> }

/** Pereții aprobați intră în nivel cu id nou, unic, și cu proveniența lor. Nivelul primit nu se modifică (se întoarce o copie). */
export function applyWallCandidates(floor: Floor, approved: WallCandidate[], o: ApplyOptions): Floor {
  if (!/^[0-9a-f]{64}$/.test(o.sha256)) fail('Lipsește amprenta fișierului sursă.');
  const needs = approved.some(c => c.thickness == null);
  const manual = o.manualThicknessM;
  if (needs && !(manual != null && Number.isFinite(manual) && manual >= DXF_LIMITS.minThicknessM && manual <= 1))
    fail('Unii pereți nu au grosime măsurată în desen: introdu o grosime (5–100 cm) sau debifează-i.');
  const taken = new Set<string>([...floor.walls.map(w => w.id), ...(o.takenIds ?? [])]);
  let n = 1; const nextId = () => { while (taken.has(`dxf-w-${n}`)) n++; const id = `dxf-w-${n}`; taken.add(id); return id; };
  const [dx, dz] = o.offset, mv = (q: [number, number]): [number, number] => [r3(q[0] + dx), r3(q[1] + dz)];
  const walls: Wall[] = approved.map(c => {
    const origin: WallOrigin = { method: 'dxf', sha256: o.sha256, layer: c.layer, thickness: c.thickness == null ? 'manual' : 'source' };
    return { id: nextId(), a: mv(c.a), b: mv(c.b), thickness: c.thickness ?? r3(manual!), exterior: !!o.exterior, openings: [], origin };
  });
  return { ...floor, walls: [...floor.walls, ...walls] };
}
