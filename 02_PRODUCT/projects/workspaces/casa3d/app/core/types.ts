// Modelul de date al Fazei 1. Unități: metri (geometrie), cm (dimensiuni produse), RON (prețuri).
export type Confidence = 'HIGH' | 'MEDIUM' | 'LOW' | 'UNKNOWN';
export interface Provenance { source: string; sourceUrl: string | null; verifiedAt: string | null; verificationType: string; confidence: Confidence }
export interface RoomRect { x0: number; z0: number; x1: number; z1: number }
export interface Room { id: string; name: string; type: string; rect: RoomRect }
export interface Opening { id: string; kind: 'door' | 'window'; offset: number; width: number; entrance?: boolean; height?: number; sill?: number }
export interface Wall { id: string; a: [number, number]; b: [number, number]; thickness: number; exterior: boolean; openings: Opening[] }
/** Scară dreaptă pe un nivel, care urcă la nivelul următor (centrul x/z, lățime și lungime în m, rotație în multipli de 90°). */
export interface Stair { id: string; x: number; z: number; width: number; length: number; rotation: number }
export interface Floor { id: string; name: string; ceilingHeight: number; rooms: Room[]; walls: Wall[]; stairs?: Stair[] }
/** `size` (cm) = piesă pe comandă cu altă dimensiune decât varianta din catalog; prețul ei devine necunoscut. */
export interface FurniturePlacement { id: string; roomId: string; group: string; variantId: string; x: number; z: number; rotation: number; source: 'auto' | 'manual'; size?: { w: number; d: number; h: number } }
/** Culoare (hex #rrggbb) și, unde modelul permite, material; lipsa înseamnă aspectul implicit. */
export interface Finish { color?: string; material?: string }
/** Aspectul ales de utilizator. Chei: pereți pe fețe `${wallId}@${roomId}`, camere, goluri (id), piese (id). */
export interface Appearance { rooms?: Record<string, { walls?: Finish; floor?: Finish; ceiling?: Finish }>; wallFaces?: Record<string, Finish>; openings?: Record<string, Finish>; items?: Record<string, Finish> }
/** Imagine de calc sub plan (scară din `widthM`, înălțimea vine din raportul imaginii). Nu intră în calcule, doar în desenul 2D. */
export interface Underlay { dataUrl: string; x: number; z: number; widthM: number; opacity: number; locked: boolean }
/** `floor` = parterul (nivelul 0); `levels` = nivelurile de deasupra, în ordine (core/levels.ts). */
export interface Snapshot { name: string; floor: Floor; levels?: Floor[]; placements: FurniturePlacement[]; selections: Record<string, string>; picked: string[]; finishes?: Record<string, RoomFinishes>; appearance?: Appearance; underlay?: Underlay; tech?: import('./technical').TechPoint[]; doors?: Record<string, DoorChoice>; budget?: BudgetSettings; brief?: import('./brief').DesignBrief }
export interface Supplier { id: string; name: string; country: string; website: string }
export interface Product { id: string; group: string; name: string; brand: string; category: string; model3d: string }
export interface ProductVariant { id: string; productId: string; name: string; legacyIndex: number; dimensionsCm: { w: number; d: number; h: number } | null; dimensionsConfidence: Confidence; style: Record<string, any>; chairs?: number; includedWith?: string }
export interface Offer { id: string; variantId: string; supplierId: string; price: number; currency: 'RON'; availability: 'UNKNOWN' | 'IN_STOCK' | 'OUT_OF_STOCK'; affiliateUrl: string | null; provenance: Provenance }
export interface Catalog { suppliers: Supplier[]; products: Product[]; variants: ProductVariant[]; offers: Offer[] }
export type Severity = 'PASS' | 'WARNING' | 'ERROR';
/** `message` = textul românesc (compatibil cu API-ul); `key` + `vars` = același text în orice limbă (lib/i18n.ts issueText). */
export interface Issue { code: 'OUT_OF_ROOM' | 'OVERLAP' | 'DOOR_ZONE' | 'WINDOW_BLOCKED' | 'CLEARANCE' | 'UNKNOWN_VARIANT' | 'OPENING_OUTSIDE_WALL' | 'WALL_TOO_SHORT' | 'STAIR'; severity: 'WARNING' | 'ERROR'; message: string; with?: string; key?: string; vars?: Record<string, string | number> }

// ---------- Faza 2: materiale, manoperă, servicii, finisaje, buget ----------
export type MaterialCategory = 'parquet' | 'floor_tile' | 'wall_tile' | 'paint' | 'baseboard' | 'tile_adhesive' | 'lighting'
  | 'wallpaper' | 'wall_panel' | 'decorative_plaster' | 'brick_cladding' | 'stone_cladding' | 'plasterboard' | 'cornice' | 'led_strip' | 'spot'
  | 'curtain' | 'sheer' | 'blind' | 'rug' | 'door' | 'door_handle' | 'moulding'
  | 'countertop' | 'kitchen_sink' | 'kitchen_tap' | 'bath_tap' | 'shower_set' | 'towel_radiator' | 'led_mirror' | 'pendant' | 'wall_light' | 'led_profile';
/** Modul de așezare a pardoselii (parchet sau plăci). */
export type FloorPattern = 'straight' | 'brick' | 'third' | 'diagonal' | 'herringbone' | 'chevron' | 'checker';
/** Date tehnice citite pe pagina produsului; lipsa unei valori înseamnă „nedeclarat”, nu „bun”. */
export interface MaterialSpecs { sizeCm?: [number, number]; rectified?: boolean; slip?: 'R9' | 'R10' | 'R11' | 'R12' | 'R13'; ip?: string; cctK?: number; lumens?: number;
  roll?: { widthM: number; lengthM: number; repeatCm: number }; patterns?: FloorPattern[]; wet?: boolean; pieceM?: number; color?: string;
  pieces?: number; opacity?: number; blind?: 'roller' | 'roman' | 'venetian'; door?: 'plain' | 'panel' | 'glass'; wood?: boolean;
  thicknessMm?: number; finish?: string; material?: string; diameterCm?: number }
export interface Material { id: string; category: MaterialCategory; name: string; supplier: string; unit: 'm2' | 'ml' | 'L' | 'kg' | 'buc'; unitPrice: number;
  pack?: { size: number; label: string; price?: number }; coverage?: number; consumption?: number; sourceUrl: string | null; verificationType: string; confidence: Confidence; note?: string; specs?: MaterialSpecs; verifiedAt?: string }
export interface LaborRate { id: string; label: string; unit: 'm2' | 'ml'; low: number; expected: number; high: number; sources: { name: string; url: string }[]; confidence: Confidence }
export interface Service { id: string; label: string; supplier: string; price?: number; pricePerMeter?: number; sourceUrl: string | null; verificationType: string; confidence: Confidence; note?: string }
export interface MaterialsCatalog { verifiedAt: string; materials: Material[]; labor: LaborRate[]; services: Service[] }
export interface FloorLayout { pattern: FloorPattern; angle?: 0 | 90; groutMm?: number; groutColor?: string }
export type WallFeatureKind = 'wallpaper' | 'slats' | 'plaster' | 'brick' | 'stone' | 'tile' | 'paint' | 'panel' | 'rail';
/** O bandă pe un perete al camerei (latura N/S/V/E): de la `fromM` (lipsă = pardoseala) până la `heightM` (lipsă = tavanul).
 *  Pe același perete pot sta mai multe benzi fără să se suprapună (ex. lambriu 0–1,1 m, baghetă la 1,1 m, tapet deasupra);
 *  `rail` e o baghetă orizontală la înălțimea `heightM`; `paint` e o vopsea de altă culoare pe banda ei. */
export interface WallFeature { side: 'N' | 'S' | 'W' | 'E'; kind: WallFeatureKind; material: string; color?: string; fromM?: number; heightM?: number }
/** Tavan: drept (vopsit), fals din gips-carton coborât cu `dropCm`, sau fals cu scafă luminoasă pe contur. */
export interface CeilingFinish { type: 'flat' | 'drop' | 'cove'; dropCm?: number; coveCm?: number; led?: string | null; cornice?: string | null; spot?: string | null; spots?: number }
export interface RoomFinishes { floor: string; wallPaint: string; wallTile?: string | null; baseboard?: string | null; light: string; lights?: number;
  floorLayout?: FloorLayout; wallFeatures?: WallFeature[]; ceiling?: CeilingFinish; windows?: WindowTreatment[]; rug?: RugChoice | null; kitchen?: KitchenSpec }
/** Bucătăria ca sistem: fronturi, mânere, corpuri suspendate, blat, placare între blat și suspendate, chiuvetă, baterie, LED sub suspendate. */
export interface KitchenSpec { frontColor?: string; frontFinish?: 'matt' | 'gloss' | 'wood'; handle?: 'bar' | 'knob' | 'profile' | 'none'; handleColor?: string;
  upper?: 'open' | 'closed' | 'none'; countertop?: string | null; countertopColor?: string; countertopMm?: number;
  backsplash?: 'tile' | 'countertop' | 'glass' | 'paint'; backsplashColor?: string; sink?: string | null; tap?: string | null; underLed?: string | null }
/** Ce se pune la o fereastră: draperie (pereche de panouri), perdea transparentă, stor/jaluzea; `fullness` = cât de încrețită e draperia. */
export interface WindowTreatment { openingId: string; curtain?: string | null; sheer?: string | null; blind?: string | null; fullness?: number }
/** Ușa aleasă pentru un gol de ușă (produs cu toc) și mânerul ei. */
export interface DoorChoice { product?: string | null; handle?: string | null }
/** Covorul camerei, centrat; `rotate` îl întoarce cu 90°. */
export interface RugChoice { material: string; rotate?: boolean }
export interface BudgetSettings { target: number | null; contingencyPct: number; includeLabor: boolean; laborScenario: 'low' | 'expected' | 'high';
  deliveryIkea: boolean; deliveryDedeman: number | null; furnitureAssembly: number | null; kitchenAssembly: boolean; design: number }
