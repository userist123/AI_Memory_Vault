// P0 (SALES_READINESS_PLAN 3.1.3): antete de securitate pe toate răspunsurile, puse din middleware.ts.
// CSP cu nonce per cerere și 'strict-dynamic': rulează doar scripturile Next.js marcate cu nonce-ul cererii.
// Stilurile rămân 'unsafe-inline' (React folosește atributul style peste tot); asta nu permite execuție de cod.

/** Nonce aleator de 128 de biți, base64 (un nonce nou la fiecare cerere). */
export function newNonce(){ const b = new Uint8Array(16); crypto.getRandomValues(b); return btoa(String.fromCharCode(...b)); }

export function contentSecurityPolicy(nonce: string, dev = process.env.NODE_ENV === 'development'){
  return [
    "default-src 'self'",
    // în dezvoltare Next.js are nevoie de eval pentru reîncărcarea la cald
    `script-src 'self' 'nonce-${nonce}' 'strict-dynamic'${dev ? " 'unsafe-eval'" : ''}`,
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data: blob:",                 // previzualizări locale (fotografii de șantier, randări, planuri importate)
    "font-src 'self' data:",
    `connect-src 'self'${dev ? ' ws: wss:' : ''}`, // doar API-ul propriu; în dezvoltare și websocket-ul de HMR
    "media-src 'self' blob:",
    "worker-src 'self' blob:",
    "frame-src 'self' blob:",                     // PDF-uri și tipăriri generate local
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",                     // aplicația nu poate fi încadrată de alt site (clickjacking)
  ].join('; ');
}

export const STATIC_SECURITY_HEADERS: Record<string, string> = {
  'x-content-type-options': 'nosniff',
  'x-frame-options': 'DENY',                      // dublura frame-ancestors pentru browsere vechi
  'referrer-policy': 'strict-origin-when-cross-origin',
  'permissions-policy': 'camera=(), microphone=(), geolocation=(), payment=(), usb=(), serial=(), bluetooth=(), interest-cohort=()',
  'cross-origin-opener-policy': 'same-origin',
};

/** HSTS doar când cererea a sosit pe https (tunel/proxy cu x-forwarded-proto); niciodată pe http://127.0.0.1. */
export function hstsFor(headers: { get(k: string): string | null }){
  const proto = headers.get('x-forwarded-proto')?.split(',')[0].trim().toLowerCase();
  return proto === 'https' ? 'max-age=31536000; includeSubDomains' : null;
}

/** Răspunsurile API (JSON, fișiere, poze încărcate) nu sunt pagini: dacă sunt deschise direct în browser, nu pot rula
 *  nimic și nu pot fi încadrate. E cel puțin la fel de strict ca CSP-ul propriu al rutei de poze (`default-src 'none'; sandbox`). */
export const API_CSP = "default-src 'none'; frame-ancestors 'none'; sandbox";
/** Politica pe cale. Atenție: Next.js pune întâi antetele din middleware, iar un antet cu același nume pus de rută se
 *  ignoră (send-response: doar dacă lipsește). De aceea varianta cea mai strictă se alege aici, după cale, nu în rută. */
export function policyFor(path: string, nonce: string){
  const api = /^\/api\//.test(path);
  // tokenul linkului de client e în cale; /go/ redirecționează spre magazine externe: nu trimitem nici măcar originea
  const noReferrer = /^\/(api\/)?share\//.test(path) || /^\/go\//.test(path);
  return { csp: api ? API_CSP : contentSecurityPolicy(nonce), referrer: noReferrer ? 'no-referrer' : STATIC_SECURITY_HEADERS['referrer-policy'] };
}
/** Aplică toate antetele pe un răspuns din middleware. */
export function applySecurityHeaders<T extends { headers: Headers }>(res: T, policy: { csp: string; referrer: string }, reqHeaders: { get(k: string): string | null }){
  res.headers.set('content-security-policy', policy.csp);
  for (const [k, v] of Object.entries(STATIC_SECURITY_HEADERS)) res.headers.set(k, v);
  res.headers.set('referrer-policy', policy.referrer);
  const hsts = hstsFor(reqHeaders); if (hsts) res.headers.set('strict-transport-security', hsts);
  return res;
}
