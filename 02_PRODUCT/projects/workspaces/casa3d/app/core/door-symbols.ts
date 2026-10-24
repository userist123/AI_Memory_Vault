// Simbolurile ușilor în plan, pe tip (convenția de arhitectură): batantă (foaie + arc), dublă (două foi și două arce),
// glisantă pe perete (foaia pe fața peretelui, șina punctată, săgeata), în perete (foaia și caseta punctate în grosime),
// pliantă (zigzag), pivotantă (foaia rotită în jurul pivotului, două arce). Funcție pură: PlanView desenează drumurile,
// testele le verifică. Coordonatele sunt ale planului (x, z în metri); `swing` și `hinge` au înțelesul din core/types.ts.
import type { DoorType } from './types';
import { FOLDING_FOUR_PANELS_M } from './rules';

type P = [number, number];
export interface DoorSymbolInput { a: P; b: P; thickness: number; hinge: 'start' | 'end'; swing: 1 | -1; type?: DoorType | null; flush?: boolean }
/** `dash`: linie punctată (șină, casetă, foaia ascunsă în perete); `fill`: dreptunghi plin (foaia glisantă). */
export interface DoorSymbolPath { d: string; dash?: boolean; fill?: boolean }
export interface DoorSymbol { type: DoorType; paths: DoorSymbolPath[]; label?: { x: number; z: number; text: string } }

const r4 = (v: number) => Math.round(v * 1e4) / 1e4;
const pt = (p: P) => `${r4(p[0])} ${r4(p[1])}`;
const add = (p: P, d: P, k: number): P => [p[0] + d[0] * k, p[1] + d[1] * k];
// arc de cerc mic de la `from` la `to` în jurul lui `c` (indicatorul de sens din produsul vectorial, ca arcul de azi din PlanView)
const arc = (c: P, from: P, to: P, r: number) => { const cr = (from[0] - c[0]) * (to[1] - c[1]) - (from[1] - c[1]) * (to[0] - c[0]); return `A${r4(r)} ${r4(r)} 0 0 ${cr > 0 ? 1 : 0} ${pt(to)}`; };
const rect = (p0: P, p1: P, p2: P, p3: P) => `M${pt(p0)}L${pt(p1)}L${pt(p2)}L${pt(p3)}Z`;

export function doorSymbol(g: DoorSymbolInput): DoorSymbol {
  const type: DoorType = g.type ?? 'swing', L = Math.hypot(g.b[0] - g.a[0], g.b[1] - g.a[1]) || 1e-9, w = L;
  const u: P = [(g.b[0] - g.a[0]) / L, (g.b[1] - g.a[1]) / L], n: P = [-u[1], u[0]], s: P = [n[0] * g.swing, n[1] * g.swing];
  const H = g.hinge === 'end' ? g.b : g.a, E = g.hinge === 'end' ? g.a : g.b, c: P = g.hinge === 'end' ? [-u[0], -u[1]] : u;   // c = de la balama spre celălalt capăt
  const at = (t: number, off = 0): P => add(add(H, c, t), s, off), th = g.thickness, paths: DoorSymbolPath[] = [];
  switch (type){
    case 'double': { const m = add(g.a, u, w / 2), h = w / 2;
      for (const q of [g.a, g.b]){ const P0 = add(q, s, h); paths.push({ d: `M${pt(q)}L${pt(P0)}${arc(q, P0, m, h)}` }); } break; }
    case 'sliding': { // foaia pe fața peretelui, pe jumătate deschisă spre `hinge`; șina de 2w punctată; săgeata în sensul deschiderii
      const f0 = th / 2 + .01, f1 = f0 + .04, t0 = -w / 2 - .04, t1 = w / 2 + .04;
      paths.push({ d: rect(at(t0, f0), at(t1, f0), at(t1, f1), at(t0, f1)), fill: true });
      paths.push({ d: `M${pt(at(-w, f1 + .02))}L${pt(at(w, f1 + .02))}`, dash: true });
      const y = f1 + .1, tip = at(w * .3, y); paths.push({ d: `M${pt(at(w * .75, y))}L${pt(tip)}M${pt(at(w * .3 + .06, y + .04))}L${pt(tip)}L${pt(at(w * .3 + .06, y - .04))}` }); break; }
    case 'pocket': { // foaia (deschisă) în axa peretelui, spre `hinge`, și conturul casetei (foaia + 10 cm) — totul punctat
      const hw = .015, k = th / 2 - .01;
      paths.push({ d: rect(at(0, -hw), at(-w, -hw), at(-w, hw), at(0, hw)), dash: true });
      paths.push({ d: rect(at(0, -k), at(-w - .1, -k), at(-w - .1, k), at(0, k)), dash: true }); break; }
    case 'folding': { // panourile în zigzag, deschise la 60° pe fața `swing`, strânse spre `hinge`
      const k = w <= FOLDING_FOUR_PANELS_M + 1e-9 ? 2 : 4, pw = w / k, dx = pw * Math.cos(Math.PI / 3), dy = pw * Math.sin(Math.PI / 3);
      let d = `M${pt(H)}`; for (let i = 1; i <= k; i++) d += `L${pt(at(i * dx, i % 2 ? dy : 0))}`; paths.push({ d }); break; }
    case 'pivot': { // pivotul la w/4 de balama; foaia rotită la 90°, arcul mare (3w/4) și cel mic (w/4)
      const pv = at(w / 4), big = add(pv, s, w * .75), small = add(pv, s, -w / 4);
      paths.push({ d: `M${pt(small)}L${pt(big)}` }); paths.push({ d: `M${pt(big)}${arc(pv, big, E, w * .75)}` }); paths.push({ d: `M${pt(small)}${arc(pv, small, H, w / 4)}` }); break; }
    default: { const P0 = add(H, s, w); paths.push({ d: `M${pt(H)}L${pt(P0)}${arc(H, P0, E, w)}` }); }
  }
  // filomuro: fără toc desenat (ca azi), cu eticheta „F” pe fața opusă deschiderii
  const mid: P = [(g.a[0] + g.b[0]) / 2, (g.a[1] + g.b[1]) / 2], lp = add(mid, s, -(th / 2 + .14));
  return { type, paths, ...(g.flush ? { label: { x: r4(lp[0]), z: r4(lp[1]), text: 'F' } } : {}) };
}
