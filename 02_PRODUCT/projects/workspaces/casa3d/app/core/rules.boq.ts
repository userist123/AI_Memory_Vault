// Reguli de calcul pentru cantități — ipoteze de planificare, afișate în interfață (nu sunt date de catalog).
export const WASTE: Record<string, number> = { parquet: .10, floor_tile: .10, wall_tile: .10, baseboard: .05, paint: .10, tile_adhesive: 0, lighting: 0 };
export const PAINT_COATS = 2;
export const DOOR_HEIGHT = 2.1;
export const WINDOW_HEIGHT = 1.3;            // între parapet (0,9 m) și buiandrug (2,2 m), ca în modelul 3D
export const BATH_TILE_HEIGHT = 2.1;         // faianță în baie până la 2,1 m
export const BACKSPLASH_HEIGHT = .6;         // faianță între blat și dulapurile suspendate
export const LIGHTS_EXTRA_PER_M2 = 12;       // un corp de iluminat pe cameră + încă unul la fiecare 12 m² peste prima
export const VAT_RATE = .21;                 // cota standard TVA România din 1 august 2025
export const WET_ROOMS = new Set(['baie', 'bucatarie']);
export const SANITARY = new Set(['dus', 'lavoar', 'wc']);
export const APPLIANCES = new Set(['frigider', 'plita', 'cuptor', 'hota']);
export const DEFAULT_BUDGET = { target: null, contingencyPct: 10, includeLabor: true, laborScenario: 'expected' as const, deliveryIkea: true, deliveryDedeman: null, furnitureAssembly: null, kitchenAssembly: true, design: 0 };
