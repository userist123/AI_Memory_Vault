// Poarta adresei publice: aplicația rulează pe calculatorul designerului (127.0.0.1) și poate fi deschisă din afară doar
// printr-un tunel (ex. cloudflared). Prin tunel se ajunge numai la linkurile pentru client și la ce le trebuie lor;
// editorul, proiectele, randările Blender și administrarea rămân doar pe acest calculator.
const LOCAL_HOST = /^(localhost|127\.\d+\.\d+\.\d+|\[::1\])(:\d+)?$/i;
const PUBLIC_PATHS = [
  /^\/share\/[A-Za-z0-9_-]{43}\/?$/,                 // prezentarea pentru client
  /^\/api\/share\/[A-Za-z0-9_-]{43}(\/feedback)?$/,  // revizia partajată și părerile clientului
  /^\/_next\//, /^\/tex\//, /^\/models\//, /^\/favicon\.(ico|svg)$/,
];
/** Cererea vine de pe acest calculator (nu prin tunel): gazda e locală, fără antetul pus de tunel; x-forwarded-host
 *  (Next.js îl completează singur din Host) trebuie să fie și el local, dacă există. */
export function isLocalRequest(host: string | null, headers: { get(k: string): string | null }){
  const fwd = headers.get('x-forwarded-host');
  return !!host && LOCAL_HOST.test(host) && !headers.get('cf-connecting-ip') && (!fwd || fwd.split(',').every(h => LOCAL_HOST.test(h.trim())));
}
export const isPublicPath = (path: string) => PUBLIC_PATHS.some(re => re.test(path));
