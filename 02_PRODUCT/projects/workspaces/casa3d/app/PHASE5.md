# Faza 5 — raport STOP GATE (Digital Twin, design pe DSL, partajare pe revizie, joystick)

Verificat în containerul cloud la 2026-10-10, Node 22. Nucleul `@casa3d/twin-core` (dependență `file:`, `transpilePackages`) este separat de aplicație; aplicația îl consumă prin adaptorul `lib/twin.ts`.

| Verificare | Rezultat |
|---|---|
| twin-core: `npx vitest run` | ✅ 58/58 |
| twin-core: `tsc --noEmit` | ✅ curat |
| twin-core: `npm audit` | ✅ 0 vulnerabilități |
| app: `npm run typecheck` | ✅ curat |
| app: `npm test` | ✅ 84/84 (64 anterioare + 20 noi, dintre care 13 regresii din cele două review-uri) |
| app: `npm run build` | ✅ |
| CI `.github/workflows/casa3d-build.yml` | ✅ definit pentru ubuntu și windows; rezultatul pe acest commit se citește pe GitHub Actions |

| Server de producție (HTTP, `next start`, PGlite în memorie) | ✅ 3 variante; aplicare → revizia 1; a doua aplicare → 409 stale; share public fără cookie 200 cu `no-store` și `noindex`; token greșit sau revocat → 404 |

## Teste obligatorii din promptul fazei
| Cerință | Rezultat |
|---|---|
| Proiectul demo → Digital Twin | twin valid, pereți interiori comuni (două camere), 19 piese care revin în ±1 mm (amprenta nu se schimbă la dus-întors) |
| Validarea brief-ului | date invalide → 400 |
| Generare de variante | 3 variante deterministe, DSL fără coordonate; generarea nu modifică proiectul |
| Aplicare | revizia 1 creată; celelalte camere rămân neatinse |
| A doua decizie pe aceeași variantă | 409 |
| Variantă veche după schimbarea twin-ului | 409 și listată ca STALE |
| Respingere | nu creează revizie |
| Partajare | link-ul arată exact revizia 1, chiar după ce există revizia 2 |
| Token greșit sau revocat | 404 |
| Alt proprietar | 404 |

## Ce s-a construit
**Pachetul `@casa3d/twin-core`** (`../packages/twin-core`, fără React/Next/bază de date):
- Digital Twin v1.0 pe poligoane rectilinii (camerele în L sunt acceptate în nucleu; aplicația editează încă dreptunghiuri), amprentă SHA-256, legarea automată perete–cameră.
- Geometry Engine: 16 coduri de probleme, ERROR/WARNING.
- Design DSL 1.1: validator care respinge coordonatele și id-urile inventate; solver semantic determinist; până la 3 variante, fără câștigător automat.
- Evaluare BOQ: totaluri cunoscute doar în RON, `UNKNOWN` numărat, verdict de buget `UNKNOWN` când lipsește un preț.
- Aprobare cu protecție la propuneri vechi (stale); partajare read-only prin token de 32 de octeți; import de feed furnizor, fără scraping; furnizor de design pe reguli (Economic/Echilibrat/Premium).
- Furnizorul bazat pe model NU este conectat, decizie a proprietarului: nu există încă apel AI real.

**Integrarea în aplicație:**
- `lib/twin.ts`: adaptor centru + radiani + cm ↔ colț minim + 0/90/180/270 + metri; `DOOR_CLEAR_DEPTH` = 0.95 (aliniat cu `doorZones()`); politica de suprapunere din `ALLOWED_OVERLAP`.
- Tabele noi: `design_proposals`, `shares`.
- `POST` și `GET /api/projects/[id]/design`.
- `POST /api/projects/[id]/design/[pid]` (index, `action` = `apply|reject`, `confirmWarnings`): 409 dacă varianta e veche, dacă avertismentele nu sunt confirmate sau dacă a fost deja decisă.
- `GET/POST/DELETE /api/projects/[id]/shares`; public `GET /api/share/[token]` și pagina `/share/[token]` (noindex, fără cookie de proprietar, 404 pentru token necunoscut sau revocat).
- Editor: fila „Twin”; „Partajează” pe fiecare revizie, cu listă de linkuri și revocare.
- Joystick virtual în turul 3D pe dispozitive tactile (`setMove` al motorului, axe limitate la [-1, 1]).

## Review independent (2026-10-10)
Un reviewer separat (alt context, alt model) a cerut modificări; toate constatările de mai jos au regresie în `tests/design-review.test.ts` și `tests/design-warnings.test.ts`, iar cele din nucleu în testele twin-core.
- Id-uri: piesele noi primesc UUID; id-urile solver-ului sunt unice și nu ajung în proiect. BOQ-ul și referințele problemelor folosesc id-urile finale.
- Aplicare atomică: claim condiționat pe propunere (o singură variantă, o singură dată) și compare-and-swap pe draft; dublu-click și aplicări concurente dau 409, iar perdantul nu rămâne revendicat.
- Erorile din tot proiectul se verifică înainte de a scrie draftul (aceeași regulă ca la revizie), deci draftul nu se schimbă dacă revizia ar fi refuzată.
- Avertismentele validatorului aplicației trec prin poarta de confirmare.
- Camerele peste 30 m pe latură sau 400 m² sunt refuzate cu 422; grila de căutare se rărește peste 6000 de poziții.
- Piesele fără dimensiuni în catalog sunt păstrate la aplicare.
- Linkul partajat arată numele reviziei; prețul necunoscut e numărat separat, nu ca 0.
- Ruta de decizie acceptă doar `apply`/`reject` și un index întreg; joystick-ul se oprește la demontare și la ieșirea din modul de mers.
- Al doilea review: aplicarea rula pași separați după scrierea draftului. Acum claim-ul, compare-and-swap-ul pe draft (care dă și numărul reviziei), decizia și revizia sunt o singură tranzacție, iar revizia se scrie din proiectul aplicat, nu recitită. Un eșec face rollback complet (test: o revizie concurentă ocupă numărul → draftul, claim-ul și reviziile rămân neatinse; testul pică pe codul vechi). Salvarea manuală a reviziei folosește aceeași regulă. Referințele problemelor de la aplicare folosesc id-urile din proiect.
- Ramura `pg` a tranzacției e verificată și pe Postgres 16 real (o copie temporară a testelor `design-review` și `design-twin` cu `DATABASE_URL`: 12/12 și 4/4, inclusiv aplicările concurente prin pool); suita din CI rulează pe PGlite.
- Rămas deschis: nucleul nu modelează direcția feței piesei; validatorul aplicației o acoperă prin poarta de mai sus. Joystick-ul nu are test automat (fără mediu de test UI); verificarea e manuală.

## Decizii
- Twin-ul este sursa de adevăr geometrică pentru design; `Snapshot` rămâne formatul persistat al aplicației.
- AI-ul propune doar DSL fără coordonate; poziționarea o face solverul, validarea Geometry Engine (Constituția art. 2).
- Fără câștigător automat: utilizatorul alege, ERROR blochează, WARNING cere confirmare (art. 3).
- Prețurile necunoscute rămân `UNKNOWN`, nu sunt estimate (art. 4).
- Partajarea este legată de o revizie, nu de proiectul curent; un 404 uniform nu dezvăluie dacă tokenul a existat.
- Furnizorul AI rămâne neconectat până la decizia proprietarului.

## Limite
- Panoul Twin și joystick-ul nu au test în browser/UI; verificarea manuală pe PC-ul proprietarului este în așteptare.
- Furnizorul AI nu este conectat; funcționează doar furnizorul pe reguli.
- Camerele din aplicație rămân dreptunghiuri (nucleul acceptă forme în L).
- BOQ-ul din stratul twin acoperă doar mobilierul; materialele și manopera rămân în `core/boq.ts`.
- Pagina de partajare randează doar un plan 2D.
