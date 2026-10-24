// Export DXF (ASCII, AC1009 / R12) al planului: funcție pură, fără React și fără I/O.
// Unități: milimetri (1 m = 1000). Planul are z în jos pe ecran, DXF are y în sus => y = -z * 1000.
import type { Catalog, RoomRect, Snapshot, Wall } from './types';
import { resolve } from './catalog';
import { footprintOf } from './validate';
import { TECH_KINDS, type TechKind } from './technical';
import { wallDimensions, roomSchedule } from './dimensions';
import { doorSwing } from './doors';
import { doorSymbol } from './door-symbols';
import { floors, levelView, stairGeometry, stairVoids, stairRect, SLAB } from './levels';
import { wallLength } from './geometry';
import { nicheGeometry } from './niches';
import { dimGeometry, textHeight } from './annotations';

export const DXF_LAYERS = [
  { name: 'WALLS', color: 7 }, { name: 'DOORS', color: 1 }, { name: 'WINDOWS', color: 5 }, { name: 'ROOMS', color: 3 },
  { name: 'DIMENSIONS', color: 2 }, { name: 'FURNITURE', color: 6 }, { name: 'SERVICES', color: 4 },
] as const;
/** Straturi adăugate doar când există mai multe niveluri sau scări; mereu în tabelul LAYER. */
export const DXF_LEVEL_LAYERS = [{ name: 'LEVELS', color: 30 }, { name: 'STAIRS', color: 140 }] as const;
/** Textele libere de pe plan (cotele persistente merg pe DIMENSIONS); mereu în tabelul LAYER. */
export const DXF_NOTE_LAYERS = [{ name: 'NOTES', color: 8 }] as const;
/** Planul de renovare și zonele de pardoseală: în tabelul LAYER doar când proiectul le folosește (un proiect vechi rămâne identic). */
export const DXF_RENOVATION_LAYERS = [{ name: 'WALLS_NEW', color: 3 }, { name: 'WALLS_DEMOLISH', color: 1 }] as const;
export const DXF_ZONE_LAYERS = [{ name: 'FLOOR_ZONES', color: 9 }] as const;
export const TECH_SYMBOL: Record<TechKind, string> = { outlet: 'P', outlet_double: 'P2', switch: 'I', light_point: 'L', data: 'D', cooker: 'K', water_cold: 'A', water_hot: 'C', drain: 'S', hvac_split: 'AC', thermostat: 'T', vent_exhaust: 'V', radiator: 'R' };

/** Text sigur pentru R12: fără caractere de control, fără coduri %% (oricâte %), fără \, non-ASCII ca \U+XXXX (convenția AutoCAD). */
export function dxfText(s: string, max = 120): string {
  const clean = String(s ?? '').replace(/[\u0000-\u001f\u007f]/g, ' ').replace(/%{2,}/g, '%').replace(/\\/g, '/').trim().slice(0, max);
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
  let ox = 0;   // deplasarea nivelului curent pe X (mm)
  let usedRenovation = false, usedZones = false;
  const P = (x: number, z: number): Pt => [x * 1000 + ox, -z * 1000];   // metri plan -> mm DXF
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

  const drawLevel = (snap: Snapshot) => {
    // ---- pereți, uși, ferestre ----
    const dims = new Map(wallDimensions(snap.floor).map(d => [d.wallId, d]));
    const band = (w: Wall, s: number, e: number) => {
      const L = wallLength(w.a, w.b), ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L, h = w.thickness / 2;
      const at = (t: number, side: number) => P(w.a[0] + ux * t - uz * h * side, w.a[1] + uz * t + ux * h * side);
      return [at(s, 1), at(e, 1), at(e, -1), at(s, -1)]; };
    for (const w of snap.floor.walls){
      const L = wallLength(w.a, w.b); if (!(L > 0) || !Number.isFinite(L)) continue;
      const ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L, h = w.thickness / 2;
      // peretele de demolat: doar conturul, pe stratul lui, fără goluri (nu mai contează)
      if (w.status === 'demolish'){ usedRenovation = true; poly('WALLS_DEMOLISH', band(w, 0, L)); continue; }
      const WL = w.status === 'new' ? 'WALLS_NEW' : 'WALLS'; if (w.status === 'new') usedRenovation = true;
      const ops = w.openings.map(o => ({ o, s: Math.max(0, o.offset), e: Math.min(L, o.offset + o.width) })).filter(q => q.e > q.s).sort((a, b) => a.s - b.s);
      const piece = (s0: number, e0: number) => { const q = band(w, s0, e0); poly(WL, q);
        // peretele nou: hașură diagonală la 10 cm (linii de la o față la cealaltă)
        if (w.status === 'new') for (let t = s0 + .1; t < e0 - .02; t += .1){ const t1 = Math.min(e0, t + w.thickness);
          line(WL, P(w.a[0] + ux * t + uz * h, w.a[1] + uz * t - ux * h), P(w.a[0] + ux * t1 - uz * h, w.a[1] + uz * t1 + ux * h)); } };
      let cur = 0; for (const q of ops){ if (q.s > cur + 1e-6) piece(cur, q.s); cur = Math.max(cur, q.e); }
      if (L > cur + 1e-6) piece(cur, L);
      // spre interior = opusul normalei exterioare
      void dims;
      for (const { o, s, e } of ops){
        const pa = (t: number, side = 0) => P(w.a[0] + ux * t - uz * h * side, w.a[1] + uz * t + ux * h * side);
        // golul de trecere: doar tocurile (peste grosimea peretelui), fără foaie și fără arc
        if (o.kind === 'door' && o.passage){ line('DOORS', pa(s, 1), pa(s, -1)); line('DOORS', pa(e, 1), pa(e, -1)); }
        else if (o.kind === 'door'){
          // balamaua și partea de deschidere: aceleași ca în plan și în 3D (core/doors.ts)
          const wi = snap.floor.walls.indexOf(w), oi = w.openings.indexOf(o), sw = doorSwing(snap.floor, wi, oi), type = o.doorType ?? 'swing';
          // o foaie batantă: linia foii deschise și arcul ei, cu balamaua la `t` (atS = foaia se întinde spre capătul `e`)
          const swingLeaf = (t: number, atS: boolean, width: number) => { const hinge = pa(t), rr = width * 1000, d: Pt = atS ? [ux, -uz] : [-ux, uz], n: Pt = [-uz * sw.swing, -ux * sw.swing];   // în coordonate DXF (z inversat)
            line('DOORS', hinge, [hinge[0] + n[0] * rr, hinge[1] + n[1] * rr]);
            const aC = Math.atan2(d[1], d[0]), aO = Math.atan2(n[1], n[0]), ccw = d[0] * n[1] - d[1] * n[0] > 0;
            arc('DOORS', hinge, rr, deg(ccw ? aC : aO), deg(ccw ? aO : aC)); };
          if (type === 'swing') swingLeaf(sw.hinge !== 'end' ? s : e, sw.hinge !== 'end', e - s);
          else if (type === 'double'){ swingLeaf(s, true, (e - s) / 2); swingLeaf(e, false, (e - s) / 2); }
          else { // glisantă, în perete, pliantă, pivotantă: segmentele simbolului din plan (core/door-symbols.ts); arcele pivotantei rămân doar în plan
            const sym = doorSymbol({ a: [w.a[0] + ux * s, w.a[1] + uz * s], b: [w.a[0] + ux * e, w.a[1] + uz * e], thickness: w.thickness, hinge: sw.hinge, swing: sw.swing, type, flush: o.flush });
            for (const q of sym.paths){ let cur: Pt | null = null, first: Pt | null = null;
              for (const [, c, args] of q.d.matchAll(/([MLAZ])([^MLAZ]*)/g)){ const v = args!.trim().split(/[\s,]+/).filter(Boolean).map(Number);
                if (c === 'M'){ cur = first = P(v[0]!, v[1]!); }
                else if (c === 'L' && cur){ const nx2 = P(v[0]!, v[1]!); line('DOORS', cur, nx2); cur = nx2; }
                else if (c === 'A'){ cur = P(v[5]!, v[6]!); }
                else if (c === 'Z' && cur && first){ line('DOORS', cur, first); cur = first; } } } }
        } else for (const side of [1, 0, -1]) line('WINDOWS', pa(s, side), pa(e, side));
      }
    }

    // ---- camere ----
    const sched = new Map(roomSchedule(snap).rows.map(r => [r.id, r]));
    for (const r of snap.floor.rooms){ const { x0: a, z0: b, x1: c, z1: d } = r.rect;
      poly('ROOMS', [P(a, b), P(c, b), P(c, d), P(a, d)]);
      const ar = sched.get(r.id)?.area ?? (c - a) * (d - b);
      // numele deasupra, aria dedesubt; înălțimea scade cât să încapă în lățimea camerei (≈0,9·h pe caracter)
      const lines = [r.name, `${ar.toFixed(2)} m²`], wide = Math.max(...lines.map(l => [...dxfText(l).replace(/\\U\+[0-9A-F]{4}/g, '#')].length));
      const h = Math.max(50, Math.min(180, (c - a) * 1000 * .9 / (wide * .9))), [cx, cy] = P((a + c) / 2, (b + d) / 2);
      text('ROOMS', [cx, cy + h * .6], h, lines[0], { center: true }); text('ROOMS', [cx, cy - h * .6], h, lines[1], { center: true }); }

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

    // ---- zone de pardoseală: conturul și suprafața, pe stratul lor ----
    for (const z of snap.floor.zones ?? []){ const r = z.rect; usedZones = true; poly('FLOOR_ZONES', [P(r.x0, r.z0), P(r.x1, r.z0), P(r.x1, r.z1), P(r.x0, r.z1)]);
      text('FLOOR_ZONES', P((r.x0 + r.x1) / 2, (r.z0 + r.z1) / 2), 90, `${z.label ? `${z.label} ` : ''}${((r.x1 - r.x0) * (r.z1 - r.z0)).toFixed(2)} m²`, { center: true }); }

    // ---- ghene și casetări: contur pe stratul pereților, ghena cu diagonale (hașură) ----
    for (const c of snap.floor.columns ?? []){ if (c.role !== 'shaft' && c.role !== 'boxing') continue; const x0 = c.x - c.w / 2, x1 = c.x + c.w / 2, z0 = c.z - c.d / 2, z1 = c.z + c.d / 2;
      poly('WALLS', [P(x0, z0), P(x1, z0), P(x1, z1), P(x0, z1)]);
      if (c.role === 'shaft'){ line('WALLS', P(x0, z0), P(x1, z1)); line('WALLS', P(x1, z0), P(x0, z1)); } }

    // ---- nișe: conturul scobiturii în grosimea peretelui ----
    for (const n of snap.floor.niches ?? []){ const g = nicheGeometry(snap.floor, n); if (g) poly('WALLS', g.quad.map(q => P(q[0], q[1]))); }

    // ---- cote persistente (LINE + TEXT, ca cotele pereților) și texte libere (curățate de dxfText) ----
    for (const an of snap.floor.annotations ?? []){
      if (an.kind === 'dim'){ const g = dimGeometry(an), A = P(...g.p1), B = P(...g.p2), t = 80, dx = g.u[0], dy = -g.u[1];
        line('DIMENSIONS', A, B); line('DIMENSIONS', P(...an.a), A); line('DIMENSIONS', P(...an.b), B);
        for (const pt of [A, B]) line('DIMENSIONS', [pt[0] - (dx - dy) * t, pt[1] - (dy + dx) * t], [pt[0] + (dx - dy) * t, pt[1] + (dy + dx) * t]);
        const m = P(g.mid[0] + g.n[0] * .12, g.mid[1] + g.n[1] * .12); text('DIMENSIONS', m, 120, String(Math.round(g.value * 1000)), { rot: ((-g.rot) % 360 + 360) % 360, center: true }); }
      else text('NOTES', P(...an.at), textHeight(an) * 1000, an.text, { rot: an.rotation ? (360 - an.rotation) % 360 : 0 }); }

    // ---- mobilier ----
    for (const p of snap.placements){ const fp = footprintOf(cat, p); if (!fp) continue;
      poly('FURNITURE', [P(fp.x0, fp.z0), P(fp.x1, fp.z0), P(fp.x1, fp.z1), P(fp.x0, fp.z1)]);
      const nm = resolve(cat, p.variantId)?.product.name ?? p.group;
      text('FURNITURE', P((fp.x0 + fp.x1) / 2, (fp.z0 + fp.z1) / 2), 70, nm.length > 18 ? nm.slice(0, 17) + '.' : nm, { center: true }); }

    // ---- instalații ----
    for (const t of snap.tech ?? []){ if (!(t.kind in TECH_KINDS) || !Number.isFinite(t.x) || !Number.isFinite(t.z)) continue;
      const c = P(t.x, t.z); circle('SERVICES', c, 80); text('SERVICES', [c[0] + 120, c[1] - 50], 100, TECH_SYMBOL[t.kind]); }
  };
  const drawStairs = (full: Snapshot, i: number) => {
    const f = floors(full)[i]!, ST = 'STAIRS', rect = (r: RoomRect) => [P(r.x0, r.z0), P(r.x1, r.z0), P(r.x1, r.z1), P(r.x0, r.z1)];
    for (const st of f.stairs ?? []){
      const g = stairGeometry(st, f.ceilingHeight + SLAB), r = stairRect(st), d: Pt = [g.dir[0], -g.dir[1]], n: Pt = [-d[1], d[0]];   // d, n în coordonate DXF
      poly(ST, rect(r));
      for (let k = 1; k < g.steps; k++){ const c = [st.x - g.dir[0] * st.length / 2 + g.dir[0] * g.going * k, st.z - g.dir[1] * st.length / 2 + g.dir[1] * g.going * k] as Pt;
        const hw = st.width / 2, a = P(c[0] - g.dir[1] * hw, c[1] + g.dir[0] * hw), b = P(c[0] + g.dir[1] * hw, c[1] - g.dir[0] * hw); line(ST, a, b); }
      const bo = P(...g.bottom), tp = P(...g.top); line(ST, bo, tp);
      for (const sg of [1, -1]) line(ST, tp, [tp[0] - d[0] * 150 + n[0] * 60 * sg, tp[1] - d[1] * 150 + n[1] * 60 * sg]);
      text(ST, [bo[0] + d[0] * 250 + n[0] * st.width * 250, bo[1] + d[1] * 250 + n[1] * st.width * 250], 100, opts.lang === 'ro' ? 'SUS' : 'UP', { center: true });
    }
    for (const v of stairVoids(full, i)){ const q = rect(v); poly(ST, q); line(ST, q[0], q[2]); line(ST, q[1], q[3]); }
  };

  // ---- niveluri: nivelul 0 rămâne pe loc, următoarele la 3000 mm în dreapta (măsurat cu o trecere de probă) ----
  const all = floors(snap), multi = all.length > 1;
  let prevMax = -Infinity;
  all.forEach((fl, i) => {
    const view = levelView(snap, i), save = { n: ents.length, x0, y0, x1, y1 };
    x0 = y0 = Infinity; x1 = y1 = -Infinity; ox = 0; drawLevel(view); drawStairs(snap, i);
    const m = { x0, x1, y1 };   // întinderea nivelului la ox = 0
    ents.length = save.n; ({ x0, y0, x1, y1 } = save);
    if (i > 0 && Number.isFinite(m.x0)) ox = prevMax + 3000 - m.x0; else ox = 0;
    drawLevel(view); drawStairs(snap, i);
    if (multi && Number.isFinite(m.x0)) text('LEVELS', [(m.x0 + m.x1) / 2 + ox, m.y1 + 300], 250, i === 0 ? (opts.lang === 'en' ? 'Ground floor' : 'Parter') : fl.name || `L${i}`, { center: true });
    if (Number.isFinite(m.x1)) prevMax = m.x1 + ox;
  });

  if (!Number.isFinite(x0)){ x0 = y0 = 0; x1 = y1 = 1000; }
  const lt = ['0\nLTYPE', tag(2, 'CONTINUOUS'), tag(70, 0), tag(3, 'Solid line'), tag(72, 65), tag(73, 0), tag(40, 0)];
  const layers = [...DXF_LAYERS, ...DXF_LEVEL_LAYERS, ...DXF_NOTE_LAYERS, ...(usedRenovation ? DXF_RENOVATION_LAYERS : []), ...(usedZones ? DXF_ZONE_LAYERS : [])].map(l => ['0\nLAYER', tag(2, l.name), tag(70, 0), tag(62, l.color), tag(6, 'CONTINUOUS')].join('\n'));
  void opts;   // limba: etichetele din desen sunt neutre (nume de camere deja localizate, cote în mm, simboluri)
  return [
    '0\nSECTION', '2\nHEADER', '9\n$ACADVER\n1\nAC1009', '9\n$INSUNITS\n70\n4', '9\n$MEASUREMENT\n70\n1',
    `9\n$EXTMIN\n10\n${num(x0)}\n20\n${num(y0)}\n30\n0`, `9\n$EXTMAX\n10\n${num(x1)}\n20\n${num(y1)}\n30\n0`, '0\nENDSEC',
    '0\nSECTION', '2\nTABLES', '0\nTABLE', '2\nLTYPE', '70\n1', lt.join('\n'), '0\nENDTAB',
    '0\nTABLE', '2\nLAYER', `70\n${layers.length}`, ...layers, '0\nENDTAB', '0\nENDSEC',
    '0\nSECTION', '2\nENTITIES', ...ents, '0\nENDSEC', '0\nEOF', '',
  ].join('\n');
}
