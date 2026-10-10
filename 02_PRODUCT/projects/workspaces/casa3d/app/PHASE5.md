# Faza 5 — raport STOP GATE (Digital Twin, design pe DSL, partajare pe revizie, joystick)

Verificat în containerul cloud la 2026-10-10, Node 22. Nucleul `@casa3d/twin-core` (dependență `file:`, `transpilePackages`) este separat de aplicație; aplicația îl consumă prin adaptorul `lib/twin.ts`.

| Verificare | Rezultat |
|---|---|
| twin-core: `npx vitest run` | ✅ 55/55 |
| twin-core: `tsc --noEmit` | ✅ curat |
| twin-core: `npm audit` | ✅ 0 vulnerabilități |
| app: `npm run typecheck` | ✅ curat |
| app: `npm test` | ✅ 71/71 (64 anterioare + 7 noi) |
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
