// Randarea la cerere a viewport-ului 3D (plan A1, rândul 5): regulile pure după care motorul decide dacă mai cere un cadru.
// Orice cauză de mișcare care lipsește de aici ar îngheța imaginea, iar una rămasă activă ar ține bucla pornită degeaba.

/** Sub această distanță (1 mm) camera și ținta ei sar exact pe destinație, iar ultima imagine rămâne stabilă. */
export const SETTLE_M = .001;
const NAV_KEYS = new Set(['w', 'a', 's', 'd', 'arrowup', 'arrowdown', 'arrowleft', 'arrowright', '+', '=', '-', '_', '0']);
/** Tastele citite de bucla motorului (`e.key` cu litere mici). */
export const isNavKey = (key: string) => NAV_KEYS.has(key);
// Tasta fizică (`e.code`) ține minte ce caracter a pornit. Caracterul de la eliberare poate fi altul (Shift apăsat sau ridicat între
// timp, orice aranjament de tastatură), iar caracterul de la apăsare ar rămâne altfel activ și ar ține bucla pornită.
export type KeyMaps = { keys: Record<string, boolean>; held: Map<string, string> };
export function pressKey(m: KeyMaps, code: string, key: string){ const id = code || key, was = m.held.get(id); if (was !== undefined && was !== key) m.keys[was] = false; m.held.set(id, key); m.keys[key] = true; }
export function releaseKey(m: KeyMaps, code: string, key: string){ const id = code || key, was = m.held.get(id); if (was !== undefined) m.keys[was] = false; m.keys[key] = false; m.held.delete(id); }

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
