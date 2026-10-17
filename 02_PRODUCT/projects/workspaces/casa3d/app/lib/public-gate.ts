// Poarta adresei publice: aplicația rulează pe calculatorul designerului (127.0.0.1) și poate fi deschisă din afară doar
// printr-un tunel (ex. cloudflared). Prin tunel se ajunge numai la linkurile pentru client și la ce le trebuie lor;
// editorul, proiectele, randările Blender și administrarea rămân doar pe acest calculator.
const LOCAL_HOST = /^(localhost|127\.\d+\.\d+\.\d+|\[::1\])(:\d+)?$/i;
const PUBLIC_PATHS = [
  /^\/share\/[A-Za-z0-9_-]{43}\/?$/,                 // prezentarea pentru client
  /^\/api\/share\/[A-Za-z0-9_-]{43}(\/feedback|\/choices|\/changes(\/[0-9a-f-]{36})?)?$/,  // revizia partajată, părerile, alegerea variantelor pe cameră și modificările de aprobat
  /^\/_next\//, /^\/tex\//, /^\/models\//, /^\/favicon\.(ico|svg)$/,
];
/** Cererea vine de pe acest calculator (nu prin tunel): gazda e locală, fără antetul pus de tunel; x-forwarded-host
 *  (Next.js îl completează singur din Host) trebuie să fie și el local, dacă există. */
export function isLocalRequest(host: string | null, headers: { get(k: string): string | null }){
  const fwd = headers.get('x-forwarded-host');
  return !!host && LOCAL_HOST.test(host) && !headers.get('cf-connecting-ip') && (!fwd || fwd.split(',').every(h => LOCAL_HOST.test(h.trim())));
}
/** Numele gazdei din bara de adresă (location.hostname, fără port) e al acestui calculator; aceeași regulă ca LOCAL_HOST. */
export const isLocalHostname = (h: string) => LOCAL_HOST.test(h) || h === '::1';
export const isPublicPath = (path: string) => PUBLIC_PATHS.some(re => re.test(path));

// După login (sesiune validă) se deschide toată aplicația, în afară de ce rămâne doar pe calculatorul designerului.
const LOGIN_PATHS = [/^\/login\/?$/, /^\/api\/auth\/login$/];
const LOCAL_ONLY = [/^\/api\/auth\/password$/, /^\/api\/admin(\/|$)/, /^\/admin(\/|$)/];
export type GateDecision = 'allow' | 'login' | 'unauthorized' | 'notfound';
// Demo public: fără login, toată aplicația, în afară de ce e doar pe calculator și de randarea Blender (ține placa video ocupată).
const DEMO_BLOCKED = [/^\/api\/auth(\/|$)/, /^\/login\/?$/, /^\/api\/projects\/[^/]+\/renders(\/|$)/];
export function gate(o: { local: boolean; path: string; method: string; session: boolean; demo?: boolean }): GateDecision {
  if (o.local) return 'allow';
  if (o.demo){ if (LOCAL_ONLY.some(re => re.test(o.path)) || (DEMO_BLOCKED.some(re => re.test(o.path)) && !(o.method === 'GET' && /\/renders$/.test(o.path)))) return 'notfound'; return 'allow'; }
  if (isPublicPath(o.path) || LOGIN_PATHS.some(re => re.test(o.path))) return 'allow';
  if (LOCAL_ONLY.some(re => re.test(o.path))) return 'notfound';
  if (o.session) return 'allow';
  return !o.path.startsWith('/api/') && o.method === 'GET' ? 'login' : 'unauthorized';
}
/** Unde se întoarce după login: doar o cale din aplicație (nu alt site). */
export const safeNext = (v: unknown) => typeof v === 'string' && /^\/(?![/\\])[\w\-./%?=&]*$/.test(v) && !v.startsWith('/login') ? v : '/';

/** Cerere trimisă de aplicația însăși (fetch din aceeași origine), nu de o pagină străină: Sec-Fetch-Site (pus de
 *  browser, nu se poate falsifica dintr-o pagină) trebuie să fie same-origin când există, Origin (dacă există) trebuie
 *  să fie aceeași gazdă, iar corpul JSON (un formular străin nu poate trimite application/json fără preflight CORS). */
export function sameOriginJson(headers: { get(k: string): string | null }, noBody = false){
  const site = headers.get('sec-fetch-site'); if (site && site !== 'same-origin') return false;
  const origin = headers.get('origin'), host = headers.get('host');
  if (origin){ try { if (new URL(origin).host !== host) return false; } catch { return false; } }
  return noBody || /^application\/json(;|$)/i.test(headers.get('content-type') ?? '');
}
