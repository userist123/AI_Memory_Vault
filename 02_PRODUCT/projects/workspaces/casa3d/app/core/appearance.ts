// Aspectul ales de utilizator (culori și materiale pentru pereți, pereți-accent, podele, tavane, goluri și mobilier)
// și dimensiunile pe comandă. Funcții pure: motorul 3D, planul, consilierul și exportul citesc de aici.
import type { Appearance, Catalog, Finish, Floor, FurniturePlacement, Snapshot } from './types';
import { resolve } from './catalog';

export const DEFAULT_LOOK = { wall: '#f4f3ef', exterior: '#ecebe6', ceiling: '#fbfbf9', floorTint: '#ffffff', frame: '#ffffff' } as const;
const HEX = /^#[0-9a-f]{6}$/;
export const normalizeHex = (x: unknown): string | null => { if (typeof x !== 'string') return null; const h = x.trim().toLowerCase(); return HEX.test(h) ? h : /^#[0-9a-f]{3}$/.test(h) ? '#' + [...h.slice(1)].map(c => c + c).join('') : null; };

/** Palete de culori pentru alegere rapidă (pe lângă selectorul liber). */
export const PALETTES: { id: string; name: { ro: string; en: string }; colors: string[] }[] = [
  { id: 'neutral', name: { ro: 'Neutre', en: 'Neutrals' }, colors: ['#f4f3ef', '#e8e4dc', '#d6d0c4', '#bfb8ab', '#8e8a83', '#4a4a48'] },
  { id: 'warm', name: { ro: 'Calde', en: 'Warm' }, colors: ['#f3e3cf', '#e9c9a5', '#d9a57b', '#c07a52', '#9a5b3c', '#6e3f2c'] },
  { id: 'cool', name: { ro: 'Reci', en: 'Cool' }, colors: ['#e3ecef', '#c5d8e0', '#9cbccb', '#6f98ad', '#3f6e86', '#26475a'] },
  { id: 'nature', name: { ro: 'Natură', en: 'Nature' }, colors: ['#e6ebdf', '#c9d4b8', '#a3b58b', '#7d9068', '#56684a', '#3a4733'] },
  { id: 'accent', name: { ro: 'Accent', en: 'Accent' }, colors: ['#c94f3d', '#e0a33a', '#2f6f73', '#3b4f8c', '#7a4a7f', '#1f1f1f'] },
];

/** Materialele pe care fiecare model 3D le poate schimba; celelalte modele își schimbă doar culoarea. */
export const MATERIAL_OPTIONS: Record<string, string[]> = {
  sofa: ['fabric', 'velvet', 'leather'],
  bed: ['wood', 'paint'], tv: ['wood', 'paint'], shelf: ['wood', 'paint'], night: ['wood', 'paint'], wardrobe: ['wood', 'paint'],
  desk: ['wood', 'paint'], shoe: ['wood', 'paint'], vanity: ['wood', 'paint'], fridge: ['paint', 'metal'],
};
export const MATERIAL_LABEL: Record<string, { ro: string; en: string }> = {
  fabric: { ro: 'Textil', en: 'Fabric' }, velvet: { ro: 'Catifea', en: 'Velvet' }, leather: { ro: 'Piele', en: 'Leather' },
  wood: { ro: 'Lemn', en: 'Wood' }, paint: { ro: 'Vopsit / lăcuit', en: 'Painted / lacquered' }, metal: { ro: 'Inox / metal', en: 'Stainless / metal' },
};

// ---------- pereți: ce cameră vede fiecare față ----------
const inRect = (r: { x0: number; x1: number; z0: number; z1: number }, x: number, z: number) => x > r.x0 && x < r.x1 && z > r.z0 && z < r.z1;
/** Pentru fiecare perete (în ordinea din plan): camerele de pe fața A (normala (-uz, ux)) și de pe fața B.
 *  Un perete lung poate trece prin mai multe camere pe aceeași față, deci se eșantionează pe toată lungimea. */
export function wallFaceRooms(floor: Floor): { wallId: string; a: string[]; b: string[] }[] {
  return floor.walls.map(w => { const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) || 1, ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L;
    const off = w.thickness / 2 + 0.1, nx = -uz, nz = ux, a = new Set<string>(), b = new Set<string>(), n = Math.max(2, Math.ceil(L / 0.25));
    for (let i = 0; i < n; i++){ const t = (i + 0.5) / n * L, px = w.a[0] + ux * t, pz = w.a[1] + uz * t;
      for (const r of floor.rooms){ if (inRect(r.rect, px + nx * off, pz + nz * off)) a.add(r.id); if (inRect(r.rect, px - nx * off, pz - nz * off)) b.add(r.id); } }
    return { wallId: w.id, a: [...a], b: [...b] }; });
}
const color = (f: Finish | undefined) => normalizeHex(f?.color);
/** Culoarea unei fețe de perete văzute din cameră: accentul feței, apoi culoarea pereților camerei, apoi implicitul. */
export function wallFaceColor(snap: Snapshot, wallId: string, roomId: string | null, exterior = false): string {
  const a = snap.appearance; if (!roomId) return exterior ? DEFAULT_LOOK.exterior : DEFAULT_LOOK.wall;
  return color(a?.wallFaces?.[`${wallId}@${roomId}`]) ?? color(a?.rooms?.[roomId]?.walls) ?? DEFAULT_LOOK.wall;
}
export function roomLook(snap: Snapshot, roomId: string): { walls: string; floorTint: string; ceiling: string } {
  const r = snap.appearance?.rooms?.[roomId];
  return { walls: color(r?.walls) ?? DEFAULT_LOOK.wall, floorTint: color(r?.floor) ?? DEFAULT_LOOK.floorTint, ceiling: color(r?.ceiling) ?? DEFAULT_LOOK.ceiling };
}
export const openingColor = (snap: Snapshot, openingId: string) => color(snap.appearance?.openings?.[openingId]) ?? DEFAULT_LOOK.frame;

// ---------- mobilier ----------
/** Stilul piesei pentru motorul 3D: stilul variantei cu culoarea și materialul alese de utilizator. */
export function itemStyle(snap: Snapshot, p: FurniturePlacement, base: Record<string, any> | undefined, model: string): Record<string, any> {
  const s = { ...(base || {}) }, f = snap.appearance?.items?.[p.id], c = color(f), m = f?.material;
  if (c) s.col = c;
  if (m && (MATERIAL_OPTIONS[model] || []).includes(m)){
    if (model === 'sofa') s.mat = m; else if (model === 'fridge') s.inox = m === 'metal'; else s.wood = m === 'wood'; }
  return s;
}
/** Dimensiunile reale ale piesei (cm): cele pe comandă dacă există, altfel ale variantei. */
export function itemSizeCm(cat: Catalog, p: FurniturePlacement): { w: number; d: number; h: number } | null {
  if (p.size) return p.size; const dm = resolve(cat, p.variantId)?.variant.dimensionsCm; return dm ? { w: dm.w, d: dm.d, h: dm.h } : null;
}
export const isCustomSize = (p: FurniturePlacement) => !!p.size;
export const SIZE_LIMITS_CM = { min: 10, max: 400 } as const;

// ---------- potrivirea culorilor cu produse reale ----------
function hexToLab(hex: string): [number, number, number] {
  const n = parseInt(hex.slice(1), 16), c = [(n >> 16) & 255, (n >> 8) & 255, n & 255].map(v => { const x = v / 255; return x <= 0.04045 ? x / 12.92 : ((x + 0.055) / 1.055) ** 2.4; });
  const [r, g, b] = c as [number, number, number];
  const X = (r * 0.4124 + g * 0.3576 + b * 0.1805) / 0.95047, Y = r * 0.2126 + g * 0.7152 + b * 0.0722, Z = (r * 0.0193 + g * 0.1192 + b * 0.9505) / 1.08883;
  const f = (t: number) => t > 216 / 24389 ? Math.cbrt(t) : (24389 / 27 * t + 16) / 116;
  return [116 * f(Y) - 16, 500 * (f(X) - f(Y)), 200 * (f(Y) - f(Z))];
}
/** Diferența percepută între două culori (ΔE CIE76): sub ~10 culorile par apropiate. */
export function colorDistance(a: string, b: string): number { const x = hexToLab(a), y = hexToLab(b); return Math.hypot(x[0] - y[0], x[1] - y[1], x[2] - y[2]); }
/** Variantele reale din aceeași categorie, ordonate după cât de aproape e culoarea lor de cea aleasă. */
export function similarVariants(cat: Catalog, group: string, hex: string, limit = 5): { variantId: string; name: string; color: string; distance: number; price: number | null; currency: string | null }[] {
  const h = normalizeHex(hex); if (!h) return [];
  return cat.variants.filter(v => v.id.replace(/-\d+$/, '') === group && normalizeHex(v.style?.col))
    .map(v => { const o = cat.offers.find(x => x.variantId === v.id); const c = normalizeHex(v.style.col)!; return { variantId: v.id, name: v.name, color: c, distance: Math.round(colorDistance(h, c) * 10) / 10, price: o?.price ?? null, currency: o?.currency ?? null }; })
    .sort((a, b) => a.distance - b.distance).slice(0, limit);
}

/** Culorile efective din proiect, pentru consilier (armonie, contrast, prea multe culori). */
export function appearanceColors(snap: Snapshot, cat: Catalog): { roomId: string; kind: 'wall' | 'floor' | 'item'; refId: string; hex: string }[] {
  const out: { roomId: string; kind: 'wall' | 'floor' | 'item'; refId: string; hex: string }[] = [];
  for (const r of snap.floor.rooms){ const l = roomLook(snap, r.id); out.push({ roomId: r.id, kind: 'wall', refId: r.id, hex: l.walls });
    if (l.floorTint !== DEFAULT_LOOK.floorTint) out.push({ roomId: r.id, kind: 'floor', refId: r.id, hex: l.floorTint }); }
  for (const [key, f] of Object.entries(snap.appearance?.wallFaces || {})){ const [wallId, roomId] = key.split('@'); const c = color(f); if (c && roomId) out.push({ roomId, kind: 'wall', refId: wallId!, hex: c }); }
  for (const p of snap.placements){ const rv = resolve(cat, p.variantId), c = color(snap.appearance?.items?.[p.id]) ?? normalizeHex(rv?.variant.style?.col); if (c) out.push({ roomId: p.roomId, kind: 'item', refId: p.id, hex: c }); }
  return out;
}

/** Curăță aspectul venit din client: doar hex valid, materiale permise, chei care există în proiect. */
const ALL_MATERIALS = [...new Set(Object.values(MATERIAL_OPTIONS).flat())];
export function sanitizeAppearance(a: unknown, snap: Pick<Snapshot, 'floor' | 'placements'>, modelOf?: (p: FurniturePlacement) => string): Appearance | undefined {
  if (!a || typeof a !== 'object') return undefined; const x = a as any, out: Appearance = {};
  const fin = (f: any, materials: string[] = []): Finish | null => { if (!f || typeof f !== 'object') return null; const c = normalizeHex(f.color), m = typeof f.material === 'string' && materials.includes(f.material) ? f.material : null;
    if (!c && !m) return null; return { ...(c ? { color: c } : {}), ...(m ? { material: m } : {}) }; };
  const rooms = new Set(snap.floor.rooms.map(r => r.id)), walls = new Set(snap.floor.walls.map(w => w.id)), ops = new Set(snap.floor.walls.flatMap(w => w.openings.map(o => o.id))), items = new Map(snap.placements.map(p => [p.id, p]));
  if (x.rooms && typeof x.rooms === 'object') for (const [id, r] of Object.entries<any>(x.rooms)){ if (!rooms.has(id) || !r) continue;
    const e = { walls: fin(r.walls), floor: fin(r.floor), ceiling: fin(r.ceiling) }; const kept = Object.fromEntries(Object.entries(e).filter(([, v]) => v)); if (Object.keys(kept).length) (out.rooms ||= {})[id] = kept; }
  if (x.wallFaces && typeof x.wallFaces === 'object') for (const [k, f] of Object.entries<any>(x.wallFaces)){ const parts = k.split('@'), [w, r] = parts; const v = fin(f); if (v && parts.length === 2 && w && r && walls.has(w) && rooms.has(r)) (out.wallFaces ||= {})[k] = v; }
  if (x.openings && typeof x.openings === 'object') for (const [id, f] of Object.entries<any>(x.openings)){ const v = fin(f); if (v && ops.has(id)) (out.openings ||= {})[id] = v; }
  if (x.items && typeof x.items === 'object') for (const [id, f] of Object.entries<any>(x.items)){ const p = items.get(id); const v = p && fin(f, modelOf ? MATERIAL_OPTIONS[modelOf(p)] || [] : ALL_MATERIALS); if (v) (out.items ||= {})[id] = v; }
  return Object.keys(out).length ? out : undefined;
}
