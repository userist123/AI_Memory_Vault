// Export DXF (ASCII, AC1009 / R12) al planului: funcție pură, fără React și fără I/O.
// Unități: milimetri (1 m = 1000). Planul are z în jos pe ecran, DXF are y în sus => y = -z * 1000.
import type { Catalog, Snapshot, Wall } from './types';
import { resolve } from './catalog';
import { footprintOf } from './validate';
import { TECH_KINDS, type TechKind } from './technical';
import { wallDimensions, roomSchedule } from './dimensions';
import { wallLength } from './geometry';

export const DXF_LAYERS = [
  { name: 'WALLS', color: 7 }, { name: 'DOORS', color: 1 }, { name: 'WINDOWS', color: 5 }, { name: 'ROOMS', color: 3 },
  { name: 'DIMENSIONS', color: 2 }, { name: 'FURNITURE', color: 6 }, { name: 'SERVICES', color: 4 },
] as const;
export const TECH_SYMBOL: Record<TechKind, string> = { outlet: 'P', outlet_double: 'P2', switch: 'I', light_point: 'L', data: 'D', cooker: 'K', water_cold: 'A', water_hot: 'C', drain: 'S' };

/** Text sigur pentru R12: fără caractere de control, fără coduri %% și \, non-ASCII ca \U+XXXX (convenția AutoCAD). */
export function dxfText(s: string, max = 120): string {
  const clean = String(s ?? '').replace(/[\u0000-\u001f\u007f]/g, ' ').replace(/%%/g, '%').replace(/\\/g, '/').trim().slice(0, max);
  let out = ''; for (const ch of clean){ const c = ch.codePointAt(0)!;
    if (c < 128) out += ch;
    else if (c > 0xffff){ const v = c - 0x10000; out += hex(0xd800 + (v >> 10)) + hex(0xdc00 + (v & 0x3ff)); }
    else out += hex(c); }
  return out;
}
const hex = (c: number) => '\\U+' + c.toString(16).toUpperCase().padStart(4, '0');

type Pt = [number, number];
export function planToDxf(snap: Snapshot, cat: Catalog, opts: { lang: 'ro' | 'en' }): string {
  const ents: string[] = [], tag = (code: number, v: string | number) => `${code}\n${v}`;
  let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
  const num = (v: number) => (Math.round(v * 1000) / 1000).toString();
  const grow = (x: number, y: number) => { x0 = Math.min(x0, x); y0 = Math.min(y0, y); x1 = Math.max(x1, x); y1 = Math.max(y1, y); };
  const P = (x: number, z: number): Pt => [x * 1000, -z * 1000];   // metri plan -> mm DXF
  const line = (layer: string, a: Pt, b: Pt) => { grow(...a); grow(...b);
    ents.push(['0\nLINE', tag(8, layer), tag(10, num(a[0])), tag(20, num(a[1])), tag(30, 0), tag(11, num(b[0])), tag(21, num(b[1])), tag(31, 0)].join('\n')); };
  const poly = (layer: string, pts: Pt[]) => { // contur închis
    const o = ['0\nPOLYLINE', tag(8, layer), tag(66, 1), tag(70, 1)];
    for (const p of pts){ grow(...p); o.push('0\nVERTEX', tag(8, layer), tag(10, num(p[0])), tag(20, num(p[1])), tag(30, 0)); }
    o.push('0\nSEQEND', tag(8, layer)); ents.push(o.join('\n')); };
  const circle = (layer: string, c: Pt, r: number) => { grow(c[0] - r, c[1] - r); grow(c[0] + r, c[1] + r);
    ents.push(['0\nCIRCLE', tag(8, layer), tag(10, num(c[0])), tag(20, num(c[1])), tag(30, 0), tag(40, num(r))].join('\n')); };
  const arc = (layer: string, c: Pt, r: number, a0: number, a1: number) => { grow(c[0] - r, c[1] - r); grow(c[0] + r, c[1] + r);
    ents.push(['0\nARC', tag(8, layer), tag(10, num(c[0])), tag(20, num(c[1])), tag(30, 0), tag(40, num(r)), tag(50, num(a0)), tag(51, num(a1))].join('\n')); };
  const text = (layer: string, at: Pt, h: number, s: string, o: { rot?: number; center?: boolean } = {}) => { grow(...at);
    const l = ['0\nTEXT', tag(8, layer), tag(10, num(at[0])), tag(20, num(at[1])), tag(30, 0), tag(40, num(h)), tag(1, dxfText(s))];
    if (o.rot) l.push(tag(50, num(o.rot)));
    if (o.center) l.push(tag(72, 1), tag(11, num(at[0])), tag(21, num(at[1])), tag(31, 0));
    ents.push(l.join('\n')); };
  const deg = (r: number) => ((r * 180 / Math.PI) % 360 + 360) % 360;

  // ---- pereți, uși, ferestre ----
  const dims = new Map(wallDimensions(snap.floor).map(d => [d.wallId, d]));
  const band = (w: Wall, s: number, e: number) => {
    const L = wallLength(w.a, w.b), ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L, h = w.thickness / 2;
    const at = (t: number, side: number) => P(w.a[0] + ux * t - uz * h * side, w.a[1] + uz * t + ux * h * side);
    return [at(s, 1), at(e, 1), at(e, -1), at(s, -1)]; };
  for (const w of snap.floor.walls){
    const L = wallLength(w.a, w.b); if (!(L > 0) || !Number.isFinite(L)) continue;
    const ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L, h = w.thickness / 2;
    const ops = w.openings.map(o => ({ o, s: Math.max(0, o.offset), e: Math.min(L, o.offset + o.width) })).filter(q => q.e > q.s).sort((a, b) => a.s - b.s);
    let cur = 0; for (const q of ops){ if (q.s > cur + 1e-6) poly('WALLS', band(w, cur, q.s)); cur = Math.max(cur, q.e); }
    if (L > cur + 1e-6) poly('WALLS', band(w, cur, L));
    // spre interior = opusul normalei exterioare
    const nd = dims.get(w.id)!, nx = -nd.normal[0], nz = -nd.normal[1];
    for (const { o, s, e } of ops){
      const pa = (t: number, side = 0) => P(w.a[0] + ux * t - uz * h * side, w.a[1] + uz * t + ux * h * side);
      if (o.kind === 'door'){
        const hinge = pa(s), rr = (e - s) * 1000, d: Pt = [ux, -uz], n: Pt = [nx, -nz];   // în coordonate DXF (z inversat)
        const closed: Pt = [hinge[0] + d[0] * rr, hinge[1] + d[1] * rr], open: Pt = [hinge[0] + n[0] * rr, hinge[1] + n[1] * rr];
        line('DOORS', hinge, open);
        const aC = Math.atan2(d[1], d[0]), aO = Math.atan2(n[1], n[0]), ccw = d[0] * n[1] - d[1] * n[0] > 0;
        arc('DOORS', hinge, rr, deg(ccw ? aC : aO), deg(ccw ? aO : aC)); void closed;
      } else for (const side of [1, 0, -1]) line('WINDOWS', pa(s, side), pa(e, side));
    }
  }

  // ---- camere ----
  const sched = new Map(roomSchedule(snap).rows.map(r => [r.id, r]));
  for (const r of snap.floor.rooms){ const { x0: a, z0: b, x1: c, z1: d } = r.rect;
    poly('ROOMS', [P(a, b), P(c, b), P(c, d), P(a, d)]);
    const ar = sched.get(r.id)?.area ?? (c - a) * (d - b);
    text('ROOMS', P((a + c) / 2, (b + d) / 2), 180, `${r.name}  ${ar.toFixed(2)} m²`, { center: true }); }

  // ---- cote (LINE + TEXT, compatibil R12) ----
  for (const w of snap.floor.walls){ const dm = dims.get(w.id)!, L = wallLength(w.a, w.b); if (!(L > 0)) continue;
    const off = w.thickness / 2 + 0.4, [nx, nz] = dm.normal, ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L;
    const A = P(w.a[0] + nx * off, w.a[1] + nz * off), B = P(w.b[0] + nx * off, w.b[1] + nz * off);
    line('DIMENSIONS', A, B);
    for (const [pt, wp] of [[A, w.a], [B, w.b]] as [Pt, Pt][]){   // extensie spre perete + tick oblic
      line('DIMENSIONS', P(wp[0] + nx * (w.thickness / 2 + 0.05), wp[1] + nz * (w.thickness / 2 + 0.05)), [pt[0] + nx * 100, pt[1] - nz * 100]);
      const t = 80, dx = ux, dy = -uz; line('DIMENSIONS', [pt[0] - (dx - dy) * t, pt[1] - (dy + dx) * t], [pt[0] + (dx - dy) * t, pt[1] + (dy + dx) * t]); }
    let rot = deg(Math.atan2(-uz, ux)); if (rot > 90 && rot <= 270) rot = (rot + 180) % 360;
    const mid: Pt = [(A[0] + B[0]) / 2 + nx * 120, (A[1] + B[1]) / 2 - nz * 120];
    text('DIMENSIONS', mid, 120, String(Math.round(L * 1000)), { rot, center: true }); }

  // ---- mobilier ----
  for (const p of snap.placements){ const fp = footprintOf(cat, p); if (!fp) continue;
    poly('FURNITURE', [P(fp.x0, fp.z0), P(fp.x1, fp.z0), P(fp.x1, fp.z1), P(fp.x0, fp.z1)]);
    const nm = resolve(cat, p.variantId)?.product.name ?? p.group;
    text('FURNITURE', P((fp.x0 + fp.x1) / 2, (fp.z0 + fp.z1) / 2), 70, nm.length > 18 ? nm.slice(0, 17) + '.' : nm, { center: true }); }

  // ---- instalații ----
  for (const t of snap.tech ?? []){ if (!(t.kind in TECH_KINDS) || !Number.isFinite(t.x) || !Number.isFinite(t.z)) continue;
    const c = P(t.x, t.z); circle('SERVICES', c, 80); text('SERVICES', [c[0] + 120, c[1] - 50], 100, TECH_SYMBOL[t.kind]); }

  if (!Number.isFinite(x0)){ x0 = y0 = 0; x1 = y1 = 1000; }
  const lt = ['0\nLTYPE', tag(2, 'CONTINUOUS'), tag(70, 0), tag(3, 'Solid line'), tag(72, 65), tag(73, 0), tag(40, 0)];
  const layers = DXF_LAYERS.map(l => ['0\nLAYER', tag(2, l.name), tag(70, 0), tag(62, l.color), tag(6, 'CONTINUOUS')].join('\n'));
  void opts;   // limba: etichetele din desen sunt neutre (nume de camere deja localizate, cote în mm, simboluri)
  return [
    '0\nSECTION', '2\nHEADER', '9\n$ACADVER\n1\nAC1009', '9\n$INSUNITS\n70\n4', '9\n$MEASUREMENT\n70\n1',
    `9\n$EXTMIN\n10\n${num(x0)}\n20\n${num(y0)}\n30\n0`, `9\n$EXTMAX\n10\n${num(x1)}\n20\n${num(y1)}\n30\n0`, '0\nENDSEC',
    '0\nSECTION', '2\nTABLES', '0\nTABLE', '2\nLTYPE', '70\n1', lt.join('\n'), '0\nENDTAB',
    '0\nTABLE', '2\nLAYER', `70\n${layers.length}`, ...layers, '0\nENDTAB', '0\nENDSEC',
    '0\nSECTION', '2\nENTITIES', ...ents, '0\nENDSEC', '0\nEOF', '',
  ].join('\n');
}
