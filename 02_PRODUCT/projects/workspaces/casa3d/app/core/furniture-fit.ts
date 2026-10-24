// Mobilarea unei camere, pentru designer: ce grupe au sens în fiecare tip de cameră (dormitorul începe cu pat, dulap și
// noptieră; mobila de baie nu apare niciodată într-un dormitor), numele de afișat al unui produs fără repetiții și un mesaj
// clar când o variantă nu încape (ce e prea mare și cu cât). Funcții pure; editorul le folosește la „Adaugă mobilier”.

/** Grupele potrivite, în ordinea în care le alege un designer, pe tip de cameră. */
export const ROOM_GROUPS: Record<string, string[]> = {
  dormitor: ['pat', 'dulap', 'noptiera', 'comoda', 'oglinda', 'birou', 'scaunBirou', 'fotoliu', 'biblioteca', 'comodaTv'],
  living: ['canapea', 'fotoliu', 'masuta', 'comodaTv', 'biblioteca', 'comoda', 'masa', 'dulap'],
  bucatarie: ['bucatarie', 'frigider', 'plita', 'cuptor', 'hota', 'masa', 'boiler'],
  baie: ['lavoar', 'wc', 'dus', 'cada', 'dulapOglinda', 'oglinda', 'coloanaBaie', 'caloriferBaie', 'bideu', 'boiler'],
  hol: ['pantofar', 'dulap', 'oglinda', 'comoda'],
  birou: ['birou', 'scaunBirou', 'biblioteca', 'dulap', 'fotoliu', 'comoda'],
};
/** Grupele care țin de baie sau de bucătărie: nu se propun în alte camere (doar dacă designerul le alege explicit din „Toate”). */
const WET_ONLY = new Set(['lavoar', 'wc', 'dus', 'cada', 'dulapOglinda', 'coloanaBaie', 'caloriferBaie', 'bideu']);
const KITCHEN_ONLY = new Set(['bucatarie', 'frigider', 'plita', 'cuptor', 'hota']);

/**
 * Grupele pentru „Adaugă mobilier” într-o cameră: întâi cele potrivite tipului ei (în ordine), apoi restul care nu sunt
 * nepotrivite. Prima grupă e alegerea implicită. Un tip necunoscut păstrează toate grupele, fără cele de baie și bucătărie.
 */
export function groupsForRoom(roomType: string | undefined, available: string[]): { suggested: string[]; others: string[] } {
  const want = ROOM_GROUPS[roomType ?? ''] ?? [], have = new Set(available);
  const suggested = want.filter(g => have.has(g));
  const fits = (g: string) => roomType === 'baie' ? !KITCHEN_ONLY.has(g) : roomType === 'bucatarie' ? !WET_ONLY.has(g) : !WET_ONLY.has(g) && !KITCHEN_ONLY.has(g);
  return { suggested, others: available.filter(g => !suggested.includes(g) && fits(g)) };
}
export const defaultGroupForRoom = (roomType: string | undefined, available: string[]) => { const g = groupsForRoom(roomType, available); return g.suggested[0] ?? g.others[0] ?? available[0] ?? null; };

/**
 * Numele de pe cardul de catalog, fără repetiții: varianta conține de obicei numele produsului („KIVIK canapea 3 locuri,
 * Tibbleby bej”), deci titlul e varianta, iar produsul apare doar dacă aduce ceva în plus.
 */
export function cardNames(productName: string, variantName: string): { title: string; sub: string | null } {
  const p = productName.trim(), v = variantName.trim(), fold = (s: string) => s.toLocaleLowerCase('ro').replace(/\s+/g, ' ');
  if (!v || fold(v) === fold(p)) return { title: p || v, sub: null };
  if (fold(v).startsWith(fold(p))) return { title: v, sub: null };
  return { title: p, sub: v };
}

/**
 * De ce nu încape o variantă nouă în locul celei vechi: dimensiunea care crește cel mai mult și cu câți cm.
 * null dacă varianta nouă nu e mai mare pe nicio latură (atunci motivul e altul: ușă, fereastră, circulație).
 */
export function swapFitHint(oldCm: { w: number; d: number }, newCm: { w: number; d: number }): { dim: 'width' | 'depth'; overCm: number; newCm: number; oldCm: number } | null {
  const dw = Math.round(newCm.w - oldCm.w), dd = Math.round(newCm.d - oldCm.d);
  if (dw <= 0 && dd <= 0) return null;
  return dw >= dd ? { dim: 'width', overCm: dw, newCm: Math.round(newCm.w), oldCm: Math.round(oldCm.w) } : { dim: 'depth', overCm: dd, newCm: Math.round(newCm.d), oldCm: Math.round(oldCm.d) };
}
