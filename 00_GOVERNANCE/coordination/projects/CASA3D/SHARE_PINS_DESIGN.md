# Casa3D — Pini de comentariu pe linkul clientului, protejați cu PIN

Decizie owner (2026-10-21): comentariile cu pin cer un PIN scurt; citirea părerilor rămâne ca azi (doar cu tokenul linkului).
App root: `02_PRODUCT/projects/workspaces/casa3d/app`.

## Ancora
Prezentarea clientului (`components/PresentView.tsx`) arată imagini randate determinist din revizia partajată
(`renderView({roomId})` / overview). Un pin = `{ surface: 'room' | 'overview', u, v }`, u,v ∈ [0,1] relativ la imaginea afișată.
Același link + aceeași revizie → aceeași imagine → pinul cade în același loc și pentru designer.

## Date (lib/schema.ts, migrare idempotentă `add column if not exists`)
- `shares`: `pin_hash text`, `pin_salt text`, `pin_key text` (cheie HMAC per link, rotită la fiecare PIN nou/dezactivare),
  `pin_failures int not null default 0`, `pin_locked_until timestamptz`.
- `share_feedback`: `anchor jsonb` (null = părere obișnuită).

## Fluxuri
1. Designer (doar proprietar, `handle()`): `POST /api/projects/[id]/shares/pin {token}` → generează PIN 6 cifre cu
   `crypto.randomInt`, salvează scrypt(pin, salt) + `pin_key` nou, resetează eșecurile; întoarce `{ pin }` o singură dată.
   `DELETE` același → șterge hash/salt/key (pinii se dezactivează). Calea NU intră în PUBLIC_PATHS.
2. Client: `POST /api/share/[token]/pin {pin}` → la succes setează cookie `casa_share_pin`, `Path=/api/share/<token>`,
   HttpOnly, SameSite=Strict, Secure când cererea e https, Max-Age 12h, valoare `<exp>.<hmac(pin_key, token.exp)>`.
3. Client: `POST /api/share/[token]/pins {verdict, author, text, roomId, anchor}` → cere cookie valid; altfel 401.
   Validarea = cea din `addFeedback` (refolosită, nu copiată) + validarea ancorei; intră în plafonul `perShare`.
4. `GET /api/share/[token]/feedback` și `listFeedback` (proprietar) întorc și `anchor`; lista proprietarului primește și `token`
   ca designerul să deschidă `/share/<token>#pin-<id>`.
5. `POST /api/share/[token]/feedback` cu câmp `anchor` → 400 (nu se ocolește PIN-ul).

## Model de amenințări / listă de probe (aceeași listă primește și reviewer-ul)
P1 Brute-force PIN (10^6): limiter nou `ShareAction 'pin'` (per IP 10/10 min, per token 30/10 min) + blocare persistentă în DB:
   5 eșecuri consecutive → `pin_locked_until = now()+15 min`; în blocare răspunsul e 429 fără a verifica PIN-ul.
   Numărare/blocare în tranzacție cu `select … for update`.
P2 Timing: comparație scrypt cu `timingSafeEqual`; PIN-ul validat ca `/^\d{6}$/` înainte de scrypt.
P3 Sesiune falsificată: HMAC-SHA256 cu `pin_key` per link; `exp` verificat; PIN nou / dezactivare rotește cheia → cookie-urile vechi mor.
P4 Token necunoscut sau revocat → același 404 pe `/pin` și `/pins` ca pe celelalte rute; PIN neconfigurat → 403 neutru.
P5 Ocolire prin `/feedback` cu `anchor` → 400.
P6 XSS: autor/text rămân text React (fără `dangerouslySetInnerHTML`); `clean()` existent pe autor/text.
P7 Ancoră: obiectul se construiește pe server; `surface` enum; u,v numere finite în [0,1], rotunjite la 4 zecimale;
   `room` ⇒ roomId existent în revizie; `overview` ⇒ roomId null; chei în plus ignorate.
P8 DoS: corp ≤ 4 KB (`readJsonLimited`), plafon pe link existent, rate-limit `feedback` refolosit pentru `/pins`.
P9 CSRF: SameSite=Strict + `content-type: application/json` obligatoriu pe `/pin` și `/pins`.
P10 PIN-ul nu se loghează, nu apare în erori; răspunsurile au `cache-control: no-store`.
P11 Poarta publică: `lib/public-gate.ts` permite exact `/api/share/<token>/pin` și `/pins`; rutele proprietarului rămân locale.

## UI
- PresentView (mod share): pe imaginea camerei și pe overview, buton „Adaugă pin”; fără sesiune → câmp PIN
  (`inputMode="numeric"`, `autoComplete="one-time-code"`, 6 cifre); apoi click pe imagine → popover (verdict, nume, text).
  Pinii existenți = butoane numerotate poziționate în procente peste imagine (`aria-label` cu textul), id `pin-<id>`.
- Editor, panoul client: „PIN pentru comentarii” — Generează / Regenerează / Dezactivează; PIN-ul se arată o dată.
  În lista de păreri, pinii au eticheta „pin pe imagine” și link „Vezi pe imagine”.
- Toate textele în `lib/locales/{ro,en}.ts` (invariantele `tests/i18n.test.ts`); contrast ≥ 4.5:1.
