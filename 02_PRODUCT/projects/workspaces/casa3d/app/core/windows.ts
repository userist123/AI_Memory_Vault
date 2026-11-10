// Ferestre ca produse: tâmplăria aleasă pe fiecare gol de fereastră (toc, canate, geam), cu prețul ei din catalogul de
// materiale, comparată cu golul și desenată în 3D după produs (culoarea tocului, numărul de canate, fixă sau mobilă,
// aluminiul mai subțire decât PVC-ul). Golul rămâne al planului: produsul nu-l mută și nu-l redimensionează.
import type { Floor, Material, MaterialsCatalog, Snapshot, WindowChoice, WindowSpec } from './types';
import { floors } from './levels';
import { materialOf } from './finishes';

/** Toleranța față de gol: tâmplăria se comandă cu 1–2 cm sub gol; peste 5 cm diferență nu mai e același gol. */
export const WINDOW_RULES = { sizeTolM: .05 };
export const WINDOW_SILL = .9, WINDOW_HEIGHT = 1.3;
export interface WindowOpening { id: string; level: number; widthM: number; heightM: number; sillM: number; wallIndex: number; openingIndex: number; roomIds: string[]; exterior: boolean }

export function windowOpenings(snap: Snapshot): WindowOpening[] {
  return floors(snap).flatMap((fl: Floor, level) => fl.walls.flatMap((w, wi) => { if (w.status === 'demolish') return [];
    const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) || 1, ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L;
    return w.openings.flatMap((o, oi) => { if (o.kind !== 'window') return [];
      const m = o.offset + o.width / 2, x = w.a[0] + ux * m, z = w.a[1] + uz * m, e = .01, sill = o.sill ?? WINDOW_SILL;
      const roomIds = fl.rooms.filter(r => x >= r.rect.x0 - e && x <= r.rect.x1 + e && z >= r.rect.z0 - e && z <= r.rect.z1 + e).map(r => r.id);
      return [{ id: o.id, level, widthM: o.width, heightM: Math.min(fl.ceilingHeight - .05, sill + (o.height ?? WINDOW_HEIGHT)) - sill, sillM: sill, wallIndex: wi, openingIndex: oi, roomIds, exterior: w.exterior }]; }); }));
}
export const windowSpec = (m: Material | null | undefined): WindowSpec | null => m?.category === 'window' ? m.specs?.win ?? null : null;

export interface WindowIssue { key: string; openingId: string; vars?: Record<string, string | number> }
/** Ce nu se potrivește între produs și gol: mărimea (lățime și înălțime), ușa de balcon pe un gol cu parapet. */
export function windowIssues(snap: Snapshot, mc: MaterialsCatalog): WindowIssue[] {
  const out: WindowIssue[] = [];
  for (const o of windowOpenings(snap)){ const m = materialOf(mc, snap.windowUnits?.[o.id]?.product); if (!m) continue;
    const sz = m.specs?.sizeCm, w = sz ? sz[0] / 100 : 0, h = sz ? sz[1] / 100 : 0;
    if (w && h && (Math.abs(w - o.widthM) > WINDOW_RULES.sizeTolM || Math.abs(h - o.heightM) > WINDOW_RULES.sizeTolM))
      out.push({ key: 'win.size', openingId: o.id, vars: { win: `${sz![0]}×${sz![1]}`, gap: `${Math.round(o.widthM * 100)}×${Math.round(o.heightM * 100)}` } });
    if (windowSpec(m)?.kind === 'balcony_door' && o.sillM > .15) out.push({ key: 'win.balconySill', openingId: o.id, vars: { sill: Math.round(o.sillM * 100) } });
    if (!o.exterior) out.push({ key: 'win.interior', openingId: o.id }); }
  return out;
}
/** Produsele care se potrivesc golului (± toleranță), primele; restul după diferența de mărime. */
export function windowCandidates(mc: MaterialsCatalog, o: Pick<WindowOpening, 'widthM' | 'heightM'>): { m: Material; fits: boolean; diffCm: number }[] {
  return mc.materials.filter(m => m.category === 'window').map(m => { const sz = m.specs?.sizeCm, dw = sz ? Math.abs(sz[0] / 100 - o.widthM) : 9, dh = sz ? Math.abs(sz[1] / 100 - o.heightM) : 9;
    return { m, fits: dw <= WINDOW_RULES.sizeTolM && dh <= WINDOW_RULES.sizeTolM, diffCm: Math.round((dw + dh) * 100) }; })
    .sort((a, b) => Number(b.fits) - Number(a.fits) || a.diffCm - b.diffCm || a.m.unitPrice - b.m.unitPrice);
}
/** Păstrează doar golurile de fereastră existente și id-uri scurte; altceva → mesaj de eroare (400). */
export function sanitizeWindows(x: unknown, snap: Snapshot): { value?: Record<string, WindowChoice>; error?: string } {
  if (x == null) return {}; if (typeof x !== 'object' || Array.isArray(x) || Object.keys(x).length > 400) return { error: 'Ferestrele sunt invalide.' };
  const ids = new Set(windowOpenings(snap).map(o => o.id)), out: Record<string, WindowChoice> = {};
  for (const [k, v] of Object.entries(x as Record<string, unknown>)){
    if (!v || typeof v !== 'object' || Array.isArray(v)) return { error: 'Ferestrele sunt invalide.' };
    const p = (v as { product?: unknown }).product; if (p != null && (typeof p !== 'string' || p.length > 80)) return { error: 'Ferestrele sunt invalide.' };
    if (ids.has(k) && p) out[k] = { product: p as string }; }
  return { value: out };
}
/** Ce desenează motorul 3D pe golul de fereastră: culoarea tocului, canatele, fixă / mobilă, materialul profilului. */
export function windowVisual(snap: Snapshot, mc: MaterialsCatalog, level: number){
  return windowOpenings(snap).filter(o => o.level === level).flatMap(o => { const m = materialOf(mc, snap.windowUnits?.[o.id]?.product), sp = windowSpec(m); if (!m || !sp) return [];
    return [{ wallIndex: o.wallIndex, openingIndex: o.openingIndex, color: m.specs?.color ?? '#f4f4f2', sashes: Math.max(1, Math.min(4, Math.round(sp.sashes || 1))), fixed: sp.kind === 'fixed', frame: sp.frame, triple: sp.glazing === 'triple',
      ...(sp.fixedSashes ? { fixedSashes: Math.max(0, Math.min(3, Math.round(sp.fixedSashes))) } : {}) }]; });
}
