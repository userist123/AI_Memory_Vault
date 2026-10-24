// Catalog contract: the only commercial source the solver may use (Constitution art. 6, 8, 9).
// Prices and availability are never invented: UNKNOWN is a first-class value.

export type Money = { amount: number; currency: 'RON' | 'EUR' } | 'UNKNOWN';

export interface Provenance {
  source: string;
  sourceUrl?: string;
  verifiedAt?: string;        // ISO date
  verificationType?: 'manual' | 'feed' | 'api' | 'unknown';
  confidence?: 'high' | 'medium' | 'low';
}

export interface CatalogItem {
  id: string;
  name: string;
  /** Footprint and height in metres. */
  w: number; d: number; h: number;
  /** Semantic role the solver may match against (bed, sofa, desk, wardrobe, table, chair...). */
  role?: string;
  price: Money;
  retailer?: string;
  provenance?: Provenance;
}

export interface Catalog {
  get(id: string): CatalogItem | undefined;
  byRole?(role: string): CatalogItem[];
}

export function catalogFromItems(items: readonly CatalogItem[]): Catalog {
  const map = new Map(items.map(i => [i.id, i]));
  return {
    get: id => map.get(id),
    byRole: role => items.filter(i => i.role === role),
  };
}
