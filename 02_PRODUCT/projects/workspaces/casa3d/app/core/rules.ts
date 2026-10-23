// Reguli de circulație — aceleași valori ca în motorul prototipului (metri).
// bucătăria: culoarul de lucru NKBA (G6) de 1067 mm pentru un singur bucătar
export const FRONT_CLEARANCE: Record<string, number> = { canapea: .45, dulap: .9, pat: .6, biblioteca: .6, birou: .8, bucatarie: 1.07, frigider: .9, lavoar: .6, wc: .6, pantofar: .5, oglinda: .6, fotoliu: .45, comoda: .6, dus: 0, cada: .7, bideu: .6, coloanaBaie: .5, caloriferBaie: 0, dulapOglinda: 0, boiler: 0, noptiera: 0, comodaTv: 0, masuta: 0, scaunBirou: 0, masa: 0 };
export const DINING_CLEARANCE = { chairs4: .65, chairs2: .45, ends: .6 };
export const TALL_ITEM_M = .95;            // peste această înălțime, piesa nu are voie în fața ferestrei
export const WALL_PROXIMITY_M = .35;       // cât de aproape de perete trebuie să fie ca să „acopere” fereastra
export const FREE_STANDING = new Set(['masuta', 'masa', 'scaunBirou']);
export const ALLOWED_OVERLAP: [string, string][] = [['birou', 'scaunBirou']]; // scaunul intră sub birou
/** Piese montate sus pe perete (dulapul cu oglindă peste lavoar, boilerul peste WC): pot sta deasupra pieselor joase. */
export const WALL_HIGH = new Set(['dulapOglinda', 'boiler']);
// Tipurile de ușă (Opening.doorType): lățimea golului permisă pe tip (m), aplicată doar când tipul e ales, ca proiectele vechi să nu fie respinse.
// `std` = lățimea propusă când golul iese din interval la schimbarea tipului (dubla sub 1 m primește 1,2 m; o dublă salvată între 0,8 și 1 m rămâne, cu avertisment);
// pocket/sliding cer perete liber lângă gol (foaia + 10 cm).
export const DOOR_TYPES = ['swing', 'double', 'sliding', 'pocket', 'folding', 'pivot'] as const;
export const DOOR_TYPE_RULES: Record<(typeof DOOR_TYPES)[number], { minM: number; maxM: number; std: number }> = {
  swing: { minM: .5, maxM: 1.4, std: .8 }, double: { minM: .8, maxM: 2.4, std: 1.2 }, sliding: { minM: .5, maxM: 1.4, std: .8 },
  pocket: { minM: .5, maxM: 1.4, std: .8 }, folding: { minM: .5, maxM: 1.4, std: .8 }, pivot: { minM: .5, maxM: 1.4, std: .9 } };
export const DOOR_SLIDE_EXTRA_M = .1, POCKET_MIN_WALL_M = .1, FOLDING_FOUR_PANELS_M = .9, DOUBLE_NARROW_M = 1;
