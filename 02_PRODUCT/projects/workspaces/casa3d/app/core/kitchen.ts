// Bucătăria ca sistem: ce alege designerul pe lângă corpurile din catalog — fronturi, mânere, suspendate deschise/închise,
// blat (produs sau culoare + grosime), placarea dintre blat și suspendate, chiuvetă, baterie, bandă LED sub suspendate.
import type { Catalog, FurniturePlacement, KitchenSpec, Material, MaterialsCatalog, RoomFinishes, Snapshot } from './types';
import { resolve } from './catalog';
import { materialOf } from './finishes';

export const KITCHEN_DEFAULT: Required<Pick<KitchenSpec, 'frontColor' | 'frontFinish' | 'handle' | 'handleColor' | 'upper' | 'countertopColor' | 'countertopMm' | 'backsplash'>> =
  { frontColor: '#f4f4f1', frontFinish: 'matt', handle: 'bar', handleColor: '#222222', upper: 'open', countertopColor: '#cdb592', countertopMm: 38, backsplash: 'tile' };
export const BACKSPLASH_M = .6, MODULE_M = .6;
export const kitchenOf = (f: RoomFinishes): KitchenSpec & typeof KITCHEN_DEFAULT => ({ ...KITCHEN_DEFAULT, ...(f.kitchen || {}) });
export const kitchenPiece = (snap: Snapshot, roomId: string): FurniturePlacement | undefined => snap.placements.find(p => p.roomId === roomId && p.group === 'bucatarie');

/** Lungimea frontului de bucătărie (din varianta din catalog sau dimensiunea pe comandă) și câte module de 60 cm are. */
export function kitchenRun(snap: Snapshot, cat: Catalog, roomId: string): { lengthM: number; modules: number } | null {
  const p = kitchenPiece(snap, roomId); if (!p) return null; const rv = resolve(cat, p.variantId), w = p.size ? p.size.w / 100 : rv?.w ?? 0;
  return w > 0 ? { lengthM: w, modules: Math.max(1, Math.round(w / MODULE_M)) } : null;
}
/** Câte bucăți de blat (produsul are o lungime) și câte fronturi/mânere: un front pe modul jos, unul pe modul sus dacă suspendatele sunt închise. */
export function kitchenQuantities(run: { lengthM: number; modules: number }, k: KitchenSpec & typeof KITCHEN_DEFAULT, top?: Material){
  const topLen = (top?.specs?.sizeCm?.[0] ?? 0) / 100, fronts = run.modules + (k.upper === 'closed' ? run.modules : k.upper === 'open' ? Math.ceil(run.modules / 2) : 0);
  return { countertopPieces: topLen > 0 ? Math.ceil(run.lengthM / topLen - 1e-9) : null, fronts, handles: k.handle === 'none' ? 0 : fronts, ledM: Math.round(run.lengthM * 100) / 100,
    backsplashM2: Math.round(run.lengthM * BACKSPLASH_M * 100) / 100 };
}
export interface KitchenIssue { key: string; roomId: string; vars?: Record<string, string | number> }
export function kitchenIssues(snap: Snapshot, cat: Catalog, mc: MaterialsCatalog, roomId: string, f: RoomFinishes): KitchenIssue[] {
  const out: KitchenIssue[] = [], run = kitchenRun(snap, cat, roomId); if (!run || !f.kitchen) return out; const k = kitchenOf(f), top = materialOf(mc, k.countertop);
  if (k.backsplash === 'tile' && !f.wallTile) out.push({ key: 'kit.noTile', roomId });
  if (k.underLed && k.upper === 'none') out.push({ key: 'kit.ledNoUpper', roomId });
  if (top?.specs?.sizeCm && top.specs.sizeCm[1] / 100 < .6) out.push({ key: 'kit.topShallow', roomId, vars: { cm: top.specs.sizeCm[1] } });
  if (k.frontFinish === 'gloss' && k.handle === 'none') out.push({ key: 'kit.glossNoHandle', roomId });
  return out;
}
const HEX = /^#[0-9a-fA-F]{6}$/;
export function sanitizeKitchen(k: any): string | null {
  if (k === undefined) return null; if (!k || typeof k !== 'object' || Array.isArray(k)) return 'Bucătăria este invalidă.';
  const hex = (v: unknown) => v === undefined || (typeof v === 'string' && HEX.test(v)), id = (v: unknown) => v == null || (typeof v === 'string' && v.length <= 80);
  const one = (v: unknown, xs: readonly string[]) => v === undefined || xs.includes(v as string);
  if (!hex(k.frontColor) || !hex(k.handleColor) || !hex(k.countertopColor) || !hex(k.backsplashColor) || !one(k.frontFinish, ['matt', 'gloss', 'wood']) || !one(k.handle, ['bar', 'knob', 'profile', 'none'])
    || !one(k.upper, ['open', 'closed', 'none']) || !one(k.backsplash, ['tile', 'countertop', 'glass', 'paint']) || !id(k.countertop) || !id(k.sink) || !id(k.tap) || !id(k.underLed)
    || (k.countertopMm !== undefined && !(typeof k.countertopMm === 'number' && k.countertopMm >= 8 && k.countertopMm <= 80))) return 'Bucătăria este invalidă.';
  return null;
}
/** Ce desenează motorul 3D pe piesa de bucătărie. */
export function kitchenVisual(mc: MaterialsCatalog, f: RoomFinishes){
  if (!f.kitchen) return null; const k = kitchenOf(f), top = materialOf(mc, k.countertop), tile = materialOf(mc, f.wallTile), sink = materialOf(mc, k.sink), tap = materialOf(mc, k.tap), led = materialOf(mc, k.underLed);
  const topColor = top?.specs?.color ?? k.countertopColor, cct = led?.specs?.cctK ?? 3000;
  return { frontColor: k.frontColor, frontFinish: k.frontFinish, handle: k.handle, handleColor: k.handleColor, upper: k.upper, topColor, topWood: k.countertop ? !!top?.specs?.wood : k.countertopColor === KITCHEN_DEFAULT.countertopColor,
    topM: (top?.specs?.thicknessMm ?? k.countertopMm) / 1000, backsplash: k.backsplash, backsplashColor: k.backsplash === 'countertop' ? topColor : k.backsplash === 'tile' ? (tile?.specs?.color ?? '#e9e5dc') : (k.backsplashColor ?? '#e8e6e1'),
    backsplashTile: k.backsplash === 'tile' ? (tile?.specs?.sizeCm ?? [30, 60]) : null, sinkColor: sink?.specs?.color ?? '#c6c9cc', tapColor: tap?.specs?.color ?? '#c6c9cc', led: led ? (cct <= 3000 ? '#ffd29a' : '#fff1dc') : null };
}
