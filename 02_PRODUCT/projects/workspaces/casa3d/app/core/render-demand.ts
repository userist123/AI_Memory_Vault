// Randarea la cerere a viewport-ului 3D (plan A1, rândul 5): regulile pure după care motorul decide dacă mai cere un cadru.
// Orice cauză de mișcare care lipsește de aici ar îngheța imaginea, iar una rămasă activă ar ține bucla pornită degeaba.

/** Sub această distanță (1 mm) camera și ținta ei sar exact pe destinație, iar ultima imagine rămâne stabilă. */
export const SETTLE_M = .001;
const NAV_KEYS = new Set(['w', 'a', 's', 'd', 'arrowup', 'arrowdown', 'arrowleft', 'arrowright', '+', '=', '-', '_', '0']);
/** Tastele citite de bucla motorului (`e.key` cu litere mici). */
export const isNavKey = (key: string) => NAV_KEYS.has(key);
const SHIFT_PAIR: Record<string, string> = { '+': '=', '=': '+', '_': '-', '-': '_' };
/** Tastele eliberate de un keyup: cu Shift ridicat primul, apăsarea vine ca „+” și eliberarea ca „=”; altfel „+” ar rămâne apăsat. */
export const releasedKeys = (key: string) => (Object.hasOwn(SHIFT_PAIR, key) ? [key, SHIFT_PAIR[key]] : [key]);

export type Demand = { keys: Record<string, boolean>; joyF: number; joyS: number; doorsMoving: boolean; glide: boolean; camGap: number; targetGap: number };
/** `camGap` și `targetGap` sub prag; o valoare nefinită nu se consideră oprită, ca o eroare să nu înghețe imaginea pe tăcute. */
export const settled = (camGap: number, targetGap: number) => camGap < SETTLE_M && targetGap < SETTLE_M;
export function stillMoving(d: Demand){
  if (d.doorsMoving || d.glide || d.joyF || d.joyS) return true;
  for (const k in d.keys) if (d.keys[k] && NAV_KEYS.has(k)) return true;
  return !settled(d.camGap, d.targetGap);
}
/** Pasul de simulare: timpul real, plafonat la 50 ms; după repaus, un singur cadru (timpul stat pe loc nu mișcă nimic). */
export const wakeDt = (now: number, last: number, asleep: boolean) => (asleep ? 1 / 60 : Math.min(.05, (now - last) / 1000));
