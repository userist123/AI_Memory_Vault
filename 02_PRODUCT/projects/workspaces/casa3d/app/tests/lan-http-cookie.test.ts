// Acces de pe telefon în aceeași rețea, pe `next dev` și http simplu: cookie-ul de sesiune fără Secure (altfel browserul îl aruncă
// și login-ul nu ține). Excepția e opt-in (CASA3D_LAN_HTTP=1), doar în dezvoltare, doar pe http și doar pe o adresă privată.
import { test, afterEach } from 'vitest';
import assert from 'node:assert/strict';
import { wantsSecureCookie, lanHttpDev } from '../lib/share-public-http';

const env0 = { lan: process.env.CASA3D_LAN_HTTP, node: process.env.NODE_ENV, trust: process.env.CASA3D_LOCAL_TRUST };
afterEach(() => { for (const [k, v] of [['CASA3D_LAN_HTTP', env0.lan], ['NODE_ENV', env0.node], ['CASA3D_LOCAL_TRUST', env0.trust]] as const){ if (v == null) delete (process.env as any)[k]; else (process.env as any)[k] = v; } });
const req = (host: string, extra: Record<string, string> = {}, url = `http://${host}/api/auth/login`) => new Request(url, { method: 'POST', headers: { host, 'x-forwarded-for': '192.168.0.51', ...extra } });
const set = (lan: boolean, env = 'development') => { if (lan) process.env.CASA3D_LAN_HTTP = '1'; else delete process.env.CASA3D_LAN_HTTP; (process.env as any).NODE_ENV = env; };

test('http din rețeaua locală, cu opțiunea activă, în dezvoltare: fără Secure (cookie-ul ține pe telefon)', () => {
  set(true); for (const h of ['192.168.0.50:3221', '10.0.0.4:3221', '172.20.1.2', '192.168.0.10']) assert.equal(wantsSecureCookie(req(h)), false, h);
});
test('fără opțiune, în producție, pe https, prin tunel sau pe o gazdă publică: Secure rămâne', () => {
  set(false); assert.equal(wantsSecureCookie(req('192.168.0.50:3221')), true, 'fără CASA3D_LAN_HTTP');
  set(true, 'production'); assert.equal(wantsSecureCookie(req('192.168.0.50:3221')), true, 'producție');
  set(true); assert.equal(wantsSecureCookie(req('192.168.0.50:3221', {}, 'https://192.168.0.50:3221/api/auth/login')), true, 'https');
  assert.equal(wantsSecureCookie(req('192.168.0.50:3221', { 'x-forwarded-proto': 'https' })), true, 'x-forwarded-proto https');
  assert.equal(wantsSecureCookie(req('192.168.0.50:3221', { 'cf-connecting-ip': '1.2.3.4' })), true, 'tunel');
  for (const h of ['abc.trycloudflare.com', '8.8.8.8:3221', '172.32.0.1', '192.169.1.1', '11.0.0.1']) assert.equal(wantsSecureCookie(req(h)), true, h);
  assert.equal(lanHttpDev(null, new Headers()), false);
});
