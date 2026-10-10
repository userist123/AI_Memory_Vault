// Modelul de date al Fazei 1. Unități: metri (geometrie), cm (dimensiuni produse), RON (prețuri).
export type Confidence = 'HIGH' | 'MEDIUM' | 'LOW' | 'UNKNOWN';
export interface Provenance { source: string; sourceUrl: string | null; verifiedAt: string | null; verificationType: string; confidence: Confidence }
export interface RoomRect { x0: number; z0: number; x1: number; z1: number }
export interface Room { id: string; name: string; type: string; rect: RoomRect }
export interface Opening { id: string; kind: 'door' | 'window'; offset: number; width: number; entrance?: boolean; height?: number; sill?: number }
export interface Wall { id: string; a: [number, number]; b: [number, number]; thickness: number; exterior: boolean; openings: Opening[] }
export interface Floor { id: string; name: string; ceilingHeight: number; rooms: Room[]; walls: Wall[] }
/** `size` (cm) = piesă pe comandă cu altă dimensiune decât varianta din catalog; prețul ei devine necunoscut. */
export interface FurniturePlacement { id: string; roomId: string; group: string; variantId: string; x: number; z: number; rotation: number; source: 'auto' | 'manual'; size?: { w: number; d: number; h: number } }
/** Culoare (hex #rrggbb) și, unde modelul permite, material; lipsa înseamnă aspectul implicit. */
export interface Finish { color?: string; material?: string }
/** Aspectul ales de utilizator. Chei: pereți pe fețe `${wallId}@${roomId}`, camere, goluri (id), piese (id). */
export interface Appearance { rooms?: Record<string, { walls?: Finish; floor?: Finish; ceiling?: Finish }>; wallFaces?: Record<string, Finish>; openings?: Record<string, Finish>; items?: Record<string, Finish> }
/** Imagine de calc sub plan (scară din `widthM`, înălțimea vine din raportul imaginii). Nu intră în calcule, doar în desenul 2D. */
export interface Underlay { dataUrl: string; x: number; z: number; widthM: number; opacity: number; locked: boolean }
export interface Snapshot { name: string; floor: Floor; placements: FurniturePlacement[]; selections: Record<string, string>; picked: string[]; finishes?: Record<string, RoomFinishes>; appearance?: Appearance; underlay?: Underlay; tech?: import('./technical').TechPoint[]; budget?: BudgetSettings; brief?: import('./brief').DesignBrief }
export interface Supplier { id: string; name: string; country: string; website: string }
export interface Product { id: string; group: string; name: string; brand: string; category: string; model3d: string }
export interface ProductVariant { id: string; productId: string; name: string; legacyIndex: number; dimensionsCm: { w: number; d: number; h: number } | null; dimensionsConfidence: Confidence; style: Record<string, any>; chairs?: number; includedWith?: string }
export interface Offer { id: string; variantId: string; supplierId: string; price: number; currency: 'RON'; availability: 'UNKNOWN' | 'IN_STOCK' | 'OUT_OF_STOCK'; affiliateUrl: string | null; provenance: Provenance }
export interface Catalog { suppliers: Supplier[]; products: Product[]; variants: ProductVariant[]; offers: Offer[] }
export type Severity = 'PASS' | 'WARNING' | 'ERROR';
export interface Issue { code: 'OUT_OF_ROOM' | 'OVERLAP' | 'DOOR_ZONE' | 'WINDOW_BLOCKED' | 'CLEARANCE' | 'UNKNOWN_VARIANT' | 'OPENING_OUTSIDE_WALL' | 'WALL_TOO_SHORT'; severity: 'WARNING' | 'ERROR'; message: string; with?: string }

// ---------- Faza 2: materiale, manoperă, servicii, finisaje, buget ----------
export type MaterialCategory = 'parquet' | 'floor_tile' | 'wall_tile' | 'paint' | 'baseboard' | 'tile_adhesive' | 'lighting';
export interface Material { id: string; category: MaterialCategory; name: string; supplier: string; unit: 'm2' | 'ml' | 'L' | 'kg' | 'buc'; unitPrice: number;
  pack?: { size: number; label: string; price?: number }; coverage?: number; consumption?: number; sourceUrl: string | null; verificationType: string; confidence: Confidence; note?: string }
export interface LaborRate { id: string; label: string; unit: 'm2' | 'ml'; low: number; expected: number; high: number; sources: { name: string; url: string }[]; confidence: Confidence }
export interface Service { id: string; label: string; supplier: string; price?: number; pricePerMeter?: number; sourceUrl: string | null; verificationType: string; confidence: Confidence; note?: string }
export interface MaterialsCatalog { verifiedAt: string; materials: Material[]; labor: LaborRate[]; services: Service[] }
export interface RoomFinishes { floor: string; wallPaint: string; wallTile?: string | null; baseboard?: string | null; light: string; lights?: number }
export interface BudgetSettings { target: number | null; contingencyPct: number; includeLabor: boolean; laborScenario: 'low' | 'expected' | 'high';
  deliveryIkea: boolean; deliveryDedeman: number | null; furnitureAssembly: number | null; kitchenAssembly: boolean; design: number }
