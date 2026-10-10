// Reguli de circulație — aceleași valori ca în motorul prototipului (metri).
export const FRONT_CLEARANCE: Record<string, number> = { canapea: .45, dulap: .9, pat: .6, biblioteca: .6, birou: .8, bucatarie: 1.0, frigider: .9, lavoar: .6, wc: .6, pantofar: .5, oglinda: .6, fotoliu: .45, comoda: .6, dus: 0, noptiera: 0, comodaTv: 0, masuta: 0, scaunBirou: 0, masa: 0 };
export const DINING_CLEARANCE = { chairs4: .65, chairs2: .45, ends: .6 };
export const TALL_ITEM_M = .95;            // peste această înălțime, piesa nu are voie în fața ferestrei
export const WALL_PROXIMITY_M = .35;       // cât de aproape de perete trebuie să fie ca să „acopere” fereastra
export const FREE_STANDING = new Set(['masuta', 'masa', 'scaunBirou']);
export const ALLOWED_OVERLAP: [string, string][] = [['birou', 'scaunBirou']]; // scaunul intră sub birou
