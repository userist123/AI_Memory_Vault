// Biblioteca utilizatorului pentru componente parametrice (corpuri, forme primitive, dulapuri la comandă)
// și importul validat de modele 3D GLB/glTF, fără prețuri sau licențe inventate.
import type { Finish, FurniturePlacement, Snapshot } from './types';
import type { WardrobeSpec } from './joinery';
import { r3 } from './geometry';

export type ParametricShapeKind = 'box' | 'cylinder' | 'extrusion' | 'wardrobe' | 'cabinet' | 'glb';

export interface UserLibraryItem {
  id: string;
  name: string;
  category: string;
  group: string;
  baseVariantId: string;
  shape: ParametricShapeKind;
  dimensionsCm: { w: number; d: number; h: number };
  materialId?: string | null;
  colorHex?: string;
  points2dCm?: [number, number][];
  wardrobeSpec?: WardrobeSpec;
  price?: number | null;
  source?: string | null;
  license?: string | null;
  verifiedAt?: string | null;
  measurable: boolean;
  version: 1;
}

export const MAX_GLB_BYTES = 25 * 1024 * 1024;

export function createParametricItem(input: {
  id: string;
  name: string;
  category?: string;
  group?: string;
  baseVariantId?: string;
  shape: Exclude<ParametricShapeKind, 'glb'>;
  dimensionsCm: { w: number; d: number; h: number };
  materialId?: string | null;
  colorHex?: string;
  points2dCm?: [number, number][];
  wardrobeSpec?: WardrobeSpec;
  price?: number | null;
  source?: string | null;
  license?: string | null;
  verifiedAt?: string | null;
}): UserLibraryItem {
  const { w, d, h } = input.dimensionsCm;
  if (![w, d, h].every(n => typeof n === 'number' && Number.isFinite(n) && n >= 5 && n <= 1200)) {
    throw new Error('Dimensiunile componentei parametrice trebuie să fie între 5 cm și 1200 cm.');
  }
  if (input.shape === 'extrusion' && (!input.points2dCm || input.points2dCm.length < 3)) {
    throw new Error('Profilul extrudat necesită cel puțin 3 vârfuri 2D.');
  }
  return {
    id: input.id.trim(),
    name: input.name.trim() || 'Componentă personalizată',
    category: input.category ?? 'custom',
    group: input.group ?? 'dulap',
    baseVariantId: input.baseVariantId ?? 'dulap-0',
    shape: input.shape,
    dimensionsCm: { w: Math.round(w * 10) / 10, d: Math.round(d * 10) / 10, h: Math.round(h * 10) / 10 },
    ...(input.materialId !== undefined ? { materialId: input.materialId } : {}),
    ...(input.colorHex ? { colorHex: input.colorHex } : {}),
    ...(input.points2dCm ? { points2dCm: structuredClone(input.points2dCm) } : {}),
    ...(input.wardrobeSpec ? { wardrobeSpec: structuredClone(input.wardrobeSpec) } : {}),
    price: typeof input.price === 'number' && Number.isFinite(input.price) && input.price >= 0 ? input.price : null,
    source: input.source ?? 'user-parametric',
    license: input.license ?? 'owner-authored',
    verifiedAt: input.verifiedAt ?? null,
    measurable: true,
    version: 1,
  };
}

export function saveToUserLibrary(lib: UserLibraryItem[] | undefined, item: UserLibraryItem): UserLibraryItem[] {
  const current = lib ? structuredClone(lib) : [];
  const idx = current.findIndex(x => x.id === item.id);
  const copy = structuredClone(item);
  if (idx >= 0) current[idx] = copy;
  else current.push(copy);
  return current;
}

/**
 * Instanțiază o componentă din biblioteca utilizatorului într-o cameră/proiect.
 * Modificarea instanței (`overrides`) nu alterează definiția din bibliotecă și nici alte instanțe plasate.
 */
export function instantiateFromLibrary(
  snap: Snapshot,
  item: UserLibraryItem,
  roomId: string,
  x: number,
  z: number,
  newPlacementId: string,
  overrides: { dimensionsCm?: { w: number; d: number; h: number }; colorHex?: string; materialId?: string | null; rotation?: number } = {},
): Snapshot {
  const next = structuredClone(snap);
  const dims = overrides.dimensionsCm ? { ...overrides.dimensionsCm } : { ...item.dimensionsCm };
  const pl: FurniturePlacement = {
    id: newPlacementId,
    roomId,
    group: item.group,
    variantId: item.baseVariantId,
    x: r3(x),
    z: r3(z),
    rotation: overrides.rotation ?? 0,
    source: 'manual',
    size: { w: dims.w, d: dims.d, h: dims.h },
  };
  next.placements = [...next.placements, pl];
  const color = overrides.colorHex ?? item.colorHex;
  const mat = overrides.materialId !== undefined ? overrides.materialId : item.materialId;
  if (color || mat) {
    const finish: Finish = { ...(color ? { color } : {}), ...(mat ? { material: mat } : {}) };
    next.appearance = {
      ...(next.appearance || {}),
      items: { ...(next.appearance?.items || {}), [newPlacementId]: finish },
    };
  }
  if (item.wardrobeSpec) {
    next.joinery = { ...(next.joinery || {}), [newPlacementId]: structuredClone(item.wardrobeSpec) };
  }
  return next;
}

export type GlbValidationResult =
  | { ok: true; item: UserLibraryItem; boundingBoxCm: { w: number; d: number; h: number } | null }
  | { ok: false; code: 'REMOTE_URL_FORBIDDEN' | 'INVALID_EXTENSION' | 'FILE_TOO_LARGE' | 'INVALID_GLB_MAGIC' | 'INVALID_GLB_VERSION' | 'CORRUPT_GLB_CHUNK'; message: string };

/**
 * Validează în siguranță un fișier binar GLB (glTF 2.0): interzice URL-uri externe (anti-SSRF), verifică antetul magic,
 * versiunea, lungimea chunk-ului JSON și extrage opțional dimensiunile din `accessors` min/max.
 * Devine `measurable: true` doar dacă dimensiunile, sursa și licența sunt furnizate explicit.
 */
export function validateGlbAsset(
  bytes: Uint8Array,
  meta: {
    id: string;
    name: string;
    fileName: string;
    maxBytes?: number;
    dimensionsCm?: { w: number; d: number; h: number } | null;
    source?: string | null;
    license?: string | null;
    group?: string;
    baseVariantId?: string;
  },
): GlbValidationResult {
  if (/^(https?:)?\/\//i.test(meta.fileName.trim()) || meta.fileName.includes('..')) {
    return { ok: false, code: 'REMOTE_URL_FORBIDDEN', message: 'Importul prin URL extern sau cale relativă nesigură este interzis.' };
  }
  if (!/\.glb$/i.test(meta.fileName.trim())) {
    return { ok: false, code: 'INVALID_EXTENSION', message: 'Doar fișierele binare .glb (glTF 2.0) sunt acceptate.' };
  }
  const limit = meta.maxBytes ?? MAX_GLB_BYTES;
  if (bytes.byteLength > limit) {
    return { ok: false, code: 'FILE_TOO_LARGE', message: `Fișierul depășește limita maximă de ${Math.round(limit / (1024 * 1024))} MB.` };
  }
  if (bytes.byteLength < 20) {
    return { ok: false, code: 'INVALID_GLB_MAGIC', message: 'Fișierul este prea scurt pentru un container GLB valid.' };
  }
  const dv = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const magic = dv.getUint32(0, true);
  if (magic !== 0x46546c67) {
    return { ok: false, code: 'INVALID_GLB_MAGIC', message: 'Antetul magic GLB („glTF”) lipsește sau este corupt.' };
  }
  const version = dv.getUint32(4, true);
  if (version !== 2) {
    return { ok: false, code: 'INVALID_GLB_VERSION', message: `Versiunea GLB ${version} nu este suportată (se cere glTF 2.0).` };
  }
  const totalLen = dv.getUint32(8, true);
  const chunkLen = dv.getUint32(12, true);
  const chunkType = dv.getUint32(16, true);
  if (totalLen > bytes.byteLength || chunkType !== 0x4e4f534a || 20 + chunkLen > bytes.byteLength) {
    return { ok: false, code: 'CORRUPT_GLB_CHUNK', message: 'Chunk-ul JSON din containerul GLB este incomplet sau corupt.' };
  }
  let json: any;
  try {
    const text = new TextDecoder('utf-8', { fatal: true }).decode(bytes.subarray(20, 20 + chunkLen));
    json = JSON.parse(text);
  } catch {
    return { ok: false, code: 'CORRUPT_GLB_CHUNK', message: 'JSON-ul intern al modelului GLB nu poate fi parsat.' };
  }
  if (!json || typeof json !== 'object' || json.asset?.version !== '2.0') {
    return { ok: false, code: 'CORRUPT_GLB_CHUNK', message: 'Metadatele glTF asset.version = "2.0" lipsesc.' };
  }
  let boundingBoxCm: { w: number; d: number; h: number } | null = null;
  if (Array.isArray(json.accessors)) {
    for (const acc of json.accessors) {
      if (Array.isArray(acc?.min) && Array.isArray(acc?.max) && acc.min.length === 3 && acc.max.length === 3) {
        const w = Math.round(Math.abs(acc.max[0] - acc.min[0]) * 1000) / 10;
        const h = Math.round(Math.abs(acc.max[1] - acc.min[1]) * 1000) / 10;
        const d = Math.round(Math.abs(acc.max[2] - acc.min[2]) * 1000) / 10;
        if (w > 0 && d > 0 && h > 0) {
          boundingBoxCm = { w, d, h };
          break;
        }
      }
    }
  }
  const dims = meta.dimensionsCm ?? boundingBoxCm ?? { w: 100, d: 100, h: 100 };
  const hasKnownScale = Boolean(meta.dimensionsCm || boundingBoxCm);
  const hasProvenance = Boolean(meta.source?.trim() && meta.license?.trim());
  const item: UserLibraryItem = {
    id: meta.id.trim(),
    name: meta.name.trim() || meta.fileName.replace(/\.glb$/i, ''),
    category: 'imported_glb',
    group: meta.group ?? 'dulap',
    baseVariantId: meta.baseVariantId ?? 'dulap-0',
    shape: 'glb',
    dimensionsCm: dims,
    price: null,
    source: meta.source?.trim() || null,
    license: meta.license?.trim() || null,
    verifiedAt: null,
    measurable: hasKnownScale && hasProvenance,
    version: 1,
  };
  return { ok: true, item, boundingBoxCm };
}
