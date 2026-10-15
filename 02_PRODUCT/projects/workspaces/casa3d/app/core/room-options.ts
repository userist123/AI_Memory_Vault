// Variante A/B/C pe cameră: designerul păstrează starea unei camere (ce variantă are fiecare piesă și finisajele camerei)
// ca „varianta A”, încearcă altceva, o păstrează ca „B”, și le compară cu diferența de cost. Clientul alege pe link;
// designerul aplică varianta aleasă. Se schimbă doar produsele pieselor existente și finisajele, nu așezarea în plan.
import type { Catalog, MaterialsCatalog, Snapshot, RoomFinishes } from './types';
import { computeBudget, finishesOf } from './boq';
import { resolve } from './catalog';
import { floors } from './levels';
import { sanitizeFinishes } from './finishes';

export interface RoomOption { id: string; label: string; note?: string; variants: Record<string, string>; finishes: RoomFinishes; savedAt: string }
export const MAX_OPTIONS = 3;
export const OPTION_LABELS = ['A', 'B', 'C'];
export class OptionError extends Error {}

const roomOf = (s: Snapshot, roomId: string) => floors(s).flatMap(f => f.rooms).find(r => r.id === roomId);
const clean = (v: unknown, n: number) => typeof v === 'string' ? v.replace(/[\u0000-\u001f<>]/g, ' ').trim().slice(0, n) : '';

/** Starea curentă a camerei: varianta fiecărei piese din cameră și finisajele (cu valorile implicite completate). */
export function roomState(s: Snapshot, roomId: string): { variants: Record<string, string>; finishes: RoomFinishes } {
  const room = roomOf(s, roomId); if (!room) throw new OptionError('Cameră inexistentă.');
  return { variants: Object.fromEntries(s.placements.filter(p => p.roomId === roomId).map(p => [p.id, p.variantId])), finishes: structuredClone(finishesOf(s, room)) };
}
// comparație fără ordinea cheilor (baza de date le reordonează) și cu null = lipsă
const canon = (v: unknown): unknown => Array.isArray(v) ? v.map(canon) : v && typeof v === 'object'
  ? Object.fromEntries(Object.keys(v).sort().filter(k => (v as any)[k] != null).map(k => [k, canon((v as any)[k])])) : v;
const same = (a: unknown, b: unknown) => JSON.stringify(canon(a)) === JSON.stringify(canon(b));
/** Varianta care corespunde exact stării curente a camerei (sau null: camera s-a schimbat de la ultima variantă păstrată). */
export function activeOption(s: Snapshot, roomId: string): RoomOption | null {
  const opts = s.roomOptions?.[roomId] ?? []; if (!opts.length) return null; const cur = roomState(s, roomId);
  return opts.find(o => same(o.finishes, cur.finishes) && Object.entries(cur.variants).every(([id, v]) => !(id in o.variants) || o.variants[id] === v)) ?? null;
}

/** Păstrează starea curentă a camerei ca variantă nouă (prima literă liberă). */
export function captureOption(s: Snapshot, roomId: string, note: string, id: string, now: string): Snapshot {
  const opts = s.roomOptions?.[roomId] ?? []; if (opts.length >= MAX_OPTIONS) throw new OptionError(`O cameră poate avea cel mult ${MAX_OPTIONS} variante.`);
  const label = OPTION_LABELS.find(l => !opts.some(o => o.label === l))!;
  const o: RoomOption = { id, label, ...roomState(s, roomId), savedAt: now }; const n = clean(note, 120); if (n) o.note = n;
  return { ...s, roomOptions: { ...(s.roomOptions ?? {}), [roomId]: [...opts, o] } };
}
/** Suprascrie o variantă cu starea curentă a camerei (după ce designerul a mai lucrat la ea). */
export function updateOption(s: Snapshot, roomId: string, optId: string, now: string): Snapshot {
  const opts = s.roomOptions?.[roomId] ?? [], k = opts.findIndex(o => o.id === optId); if (k < 0) throw new OptionError('Variantă inexistentă.');
  const next = opts.slice(); next[k] = { ...opts[k]!, ...roomState(s, roomId), savedAt: now };
  return { ...s, roomOptions: { ...s.roomOptions, [roomId]: next } };
}
export function removeOption(s: Snapshot, roomId: string, optId: string): Snapshot {
  const opts = (s.roomOptions?.[roomId] ?? []).filter(o => o.id !== optId), all = { ...(s.roomOptions ?? {}) };
  if (opts.length) all[roomId] = opts; else delete all[roomId];
  const { roomOptions: _old, ...rest } = s; return Object.keys(all).length ? { ...rest, roomOptions: all } : rest;
}
/** Aduce camera la varianta aleasă: produsul fiecărei piese care mai există (cu dimensiunea din catalog) și finisajele camerei. */
export function applyOption(s: Snapshot, roomId: string, optId: string): Snapshot {
  const o = s.roomOptions?.[roomId]?.find(x => x.id === optId); if (!o) throw new OptionError('Variantă inexistentă.');
  const placements = s.placements.map(p => {
    if (p.roomId !== roomId || !(p.id in o.variants) || o.variants[p.id] === p.variantId) return p;
    const { size: _drop, ...rest } = p; return { ...rest, variantId: o.variants[p.id]!, source: 'manual' as const };
  });
  return { ...s, placements, finishes: { ...(s.finishes ?? {}), [roomId]: structuredClone(o.finishes) } };
}

/** `label`: grupul piesei (canapea, masa…) sau finisajul (floor, wallPaint, wallTile, baseboard, light, other) */
export interface OptionChange { kind: 'product' | 'finish'; label: string; from: string; to: string }
export interface OptionSummary { option: RoomOption; total: number; delta: number; changes: OptionChange[]; missing: number; active: boolean }
const SLOTS = ['floor', 'wallPaint', 'wallTile', 'baseboard', 'light'] as const;

/** Compară variantele camerei: totalul proiectului cu fiecare variantă și diferența față de prima (A), plus ce diferă de A. */
export function compareOptions(s: Snapshot, cat: Catalog, mc: MaterialsCatalog, roomId: string): OptionSummary[] {
  const opts = s.roomOptions?.[roomId] ?? []; if (!opts.length) return [];
  const act = activeOption(s, roomId), base = opts[0]!;
  const pname = (v?: string) => { if (!v) return '—'; const r = resolve(cat, v); return r ? r.variant.name || r.product.name : v; };
  const mname = (id: unknown) => typeof id === 'string' && id ? (mc.materials.find(m => m.id === id)?.name ?? id) : '—';
  const groupOf = (pid: string) => s.placements.find(p => p.id === pid)?.group ?? '';
  const totals = opts.map(o => computeBudget(applyOption(s, roomId, o.id), cat, mc).chosen.total);
  return opts.map((o, i) => {
    const changes: OptionChange[] = [];
    if (i > 0){
      for (const [pid, v] of Object.entries(o.variants)) if (base.variants[pid] && base.variants[pid] !== v) changes.push({ kind: 'product', label: groupOf(pid), from: pname(base.variants[pid]), to: pname(v) });
      for (const k of SLOTS) if ((base.finishes[k] ?? null) !== (o.finishes[k] ?? null)) changes.push({ kind: 'finish', label: k, from: mname(base.finishes[k]), to: mname(o.finishes[k]) });
      const rest = (f: RoomFinishes) => ({ ...f, floor: 0, wallPaint: 0, wallTile: 0, baseboard: 0, light: 0 });
      if (!same(rest(base.finishes), rest(o.finishes))) changes.push({ kind: 'finish', label: 'other', from: '', to: '' });
    }
    return { option: o, total: totals[i]!, delta: Math.round((totals[i]! - totals[0]!) * 100) / 100, changes, active: act?.id === o.id,
      missing: Object.keys(o.variants).filter(pid => !s.placements.some(p => p.id === pid && p.roomId === roomId)).length };
  });
}

/** Validarea la salvare: doar camere existente, cel mult 3 variante, produse și finisaje în formatul modelului. */
export function sanitizeRoomOptions(s: Snapshot & { roomOptions?: unknown }): string | null {
  const raw = s.roomOptions as any; if (raw == null){ delete s.roomOptions; return null; }
  if (typeof raw !== 'object' || Array.isArray(raw)) return 'Variantele camerelor sunt invalide.';
  const out: Record<string, RoomOption[]> = {};
  for (const [rid, list] of Object.entries<any>(raw)){
    if (!roomOf(s, rid)) continue;   // camera a fost ștearsă: variantele ei dispar
    if (!Array.isArray(list) || list.length > MAX_OPTIONS) return `O cameră poate avea cel mult ${MAX_OPTIONS} variante.`;
    const opts: RoomOption[] = [];
    for (const o of list){
      if (!o || typeof o.id !== 'string' || !/^[\w-]{1,60}$/.test(o.id) || !OPTION_LABELS.includes(o.label) || opts.some(x => x.id === o.id || x.label === o.label)) return 'O variantă de cameră e invalidă.';
      if (!o.variants || typeof o.variants !== 'object' || Array.isArray(o.variants) || Object.keys(o.variants).length > 200) return 'O variantă de cameră e invalidă.';
      const variants: Record<string, string> = {};
      for (const [pid, v] of Object.entries(o.variants)){ if (typeof v !== 'string' || pid.length > 80 || v.length > 80) return 'O variantă de cameră e invalidă.'; variants[pid] = v; }
      const bad = sanitizeFinishes({ [rid]: o.finishes }); if (bad || !o.finishes) return bad ?? 'O variantă de cameră e invalidă.';
      const r: RoomOption = { id: o.id, label: o.label, variants, finishes: o.finishes, savedAt: typeof o.savedAt === 'string' ? o.savedAt.slice(0, 40) : '' };
      const n = clean(o.note, 120); if (n) r.note = n; opts.push(r);
    }
    if (opts.length) out[rid] = opts;
  }
  if (Object.keys(out).length) s.roomOptions = out; else delete s.roomOptions;
  return null;
}

/**
 * Descrierea unei variante pe linkul clientului și în editor: nota designerului; fără notă, „Varianta B” plus ce schimbă
 * față de A (numele grupelor / finisajelor, fără dubluri). Nu întoarce niciodată „fără descriere”.
 */
export function optionDescription(note: string | undefined, label: string, changeLabels: string[]): { key: 'ropt.defaultNote' | 'ropt.defaultNoteChanges' | null; vars: Record<string, string>; text?: string } {
  if (note && note.trim()) return { key: null, vars: {}, text: note.trim() };
  const list = [...new Set(changeLabels.map(s => s.trim()).filter(Boolean))];
  return list.length ? { key: 'ropt.defaultNoteChanges', vars: { l: label, list: list.join(', ') } } : { key: 'ropt.defaultNote', vars: { l: label } };
}
