export interface Rect { x0: number; x1: number; z0: number; z1: number }
export interface Fp extends Rect { rot: number }
export interface EnginePlacement { id: string; key: string; room: string; side?: string; along?: number; free?: boolean; x?: number; z?: number; rot?: number; fp: Fp; front?: Rect | null }
export interface LegacyRoom { id: string; nume: string; tip: string; x0: number; z0: number; x1: number; z1: number }
export interface LegacyPlan { nume: string; inaltime: number; camere: LegacyRoom[]; pereti: { a: [number, number]; b: [number, number]; ext?: boolean; goluri: { tip: 'usa' | 'fereastra'; la: number; l: number; intrare?: boolean }[] }[] }
export interface EngineVariant { nume: string; w?: number; d?: number; h?: number; s?: Record<string, any>; chairs?: number; pret?: number; url?: string; mag?: string; aprox?: boolean }
export interface EngineGroup { eticheta: string; model?: string; inclus?: string; v: EngineVariant[] }
export interface LayoutEngine {
  run(): { placed: EnginePlacement[]; notFit: { key: string; room: string }[] };
  openingsOnSide(room: LegacyRoom, side: string): { tip: string; a: number; b: number }[];
  rectHit(a: Rect, b: Rect): boolean; inside(room: LegacyRoom, r: Rect): boolean; doorZones(room: LegacyRoom): Rect[];
  footprint(room: LegacyRoom, side: string, along: number, w: number, d: number): Fp;
  findSpot(room: LegacyRoom, key: string, placed: EnginePlacement[], o: Record<string, any>): EnginePlacement | null;
  fpFrom(x: number, z: number, rot: number, w: number, d: number): Fp;
  validSpot(p: EnginePlacement, fp: Fp, others: EnginePlacement[]): boolean;
  current(key: string): (EngineVariant & { model?: string }) | null;
}
export function createLayoutEngine(o: { plan: LegacyPlan; catalog: Record<string, EngineGroup>; selection: Record<string, number>; picked?: Set<string>; wallThickness?: number }): LayoutEngine;
