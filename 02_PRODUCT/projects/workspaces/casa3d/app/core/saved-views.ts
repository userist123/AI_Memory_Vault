// Vederi 3D salvate (UI/UX P2-11, modelul Shapr3D „Saved Views”): poziția camerei, modul (machetă / tur) și înălțimea de
// tăiere, cu un nume. Se păstrează în browserul designerului, pe proiect; aici doar validarea și editarea listei (pure).
export type EngineView =
  | { mode: 'house'; th: number; ph: number; r: number; cut: number }
  | { mode: 'walk'; x: number; z: number; yaw: number; pitch: number; cut: number };
export type SavedView = { id: string; name: string; view: EngineView };

export const MAX_VIEWS = 12, NAME_MAX = 40, CUT_MIN = .12, CUT_MAX = 1;
export const viewsKey = (projectId: string) => `casa3d.views.${projectId}`;
export const clampCut = (f: number) => (Number.isFinite(f) ? Math.min(CUT_MAX, Math.max(CUT_MIN, f)) : .42);

const num = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v);
/** O vedere validă sau null (date vechi, editate de mână sau din altă versiune). */
export function parseView(v: unknown): EngineView | null {
  if (!v || typeof v !== 'object') return null; const o = v as Record<string, unknown>;
  if (o.mode === 'house' && num(o.th) && num(o.ph) && num(o.r) && o.r > 0) return { mode: 'house', th: o.th, ph: o.ph, r: o.r, cut: clampCut(num(o.cut) ? o.cut : .42) };
  if (o.mode === 'walk' && num(o.x) && num(o.z) && num(o.yaw) && num(o.pitch)) return { mode: 'walk', x: o.x, z: o.z, yaw: o.yaw, pitch: o.pitch, cut: clampCut(num(o.cut) ? o.cut : .42) };
  return null;
}
/** Lista din stocare: ce nu se poate citi se ignoră, fără să strice restul. */
export function parseViews(raw: unknown): SavedView[] {
  let a: unknown = raw; if (typeof raw === 'string'){ try { a = JSON.parse(raw); } catch { return []; } }
  if (!Array.isArray(a)) return [];
  const out: SavedView[] = [];
  for (const x of a){ if (!x || typeof x !== 'object') continue; const { id, name, view } = x as Record<string, unknown>, v = parseView(view);
    if (typeof id === 'string' && id && typeof name === 'string' && name.trim() && v && !out.some(o => o.id === id)) out.push({ id, name: name.trim().slice(0, NAME_MAX), view: v }); }
  return out.slice(-MAX_VIEWS);
}
/** Adaugă (sau înlocuiește, după nume, fără diferență de majuscule) o vedere; cel mult MAX_VIEWS, cele mai vechi ies. */
export function addView(list: readonly SavedView[], name: string, view: EngineView, id: string): SavedView[] {
  const n = name.trim().slice(0, NAME_MAX); if (!n) return [...list];
  const rest = list.filter(v => v.name.toLocaleLowerCase('ro') !== n.toLocaleLowerCase('ro'));
  return [...rest, { id, name: n, view }].slice(-MAX_VIEWS);
}
export const removeView = (list: readonly SavedView[], id: string) => list.filter(v => v.id !== id);
