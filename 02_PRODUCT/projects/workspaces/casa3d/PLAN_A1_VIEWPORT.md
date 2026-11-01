# Casa3D — faza A1 viewport 3D (rândul 5 din A1)

Sursa: `RESEARCH_A1_VIEWPORT.md` (cercetarea și măsurătorile de referință). Checkpoint:
`00_GOVERNANCE/coordination/tasks/todo-claude-casa3d-a1-viewport.md`.
Bază: `origin/main` `07ad1e1c0`. Ramura `claude/casa3d-a1-viewport`, worktree `D:/w/a3d`.

## Ce există deja (inventar pe main, 2026-11-01)
- `components/viewer3d-engine.js` `createViewer`: buclă `requestAnimationFrame` continuă care simulează și desenează la fiecare
  cadru: ușile, alunecarea în tur, tastele, joystick-ul, amortizarea camerei din machetă (`F`, `cam.position`).
- Cost măsurat: 198–376 cadre/s în repaus fără plafon, 1799 de apeluri de desen la calitate înaltă, 898 la normal.
- Calitate: `normal` (fără compozitor) sau `high` (`EffectComposer` cu `RenderPass`, `SSAOPass`, `OutputPass`, densitate 1,5).
  `Viewer3D.tsx` alege `high` pe desktop și `normal` pe ecrane tactile; butonul „Calitate” comută.
- Densitate `min(devicePixelRatio, 2)`, umbre de 2048, `MAX_PL = 4` pe orice dispozitiv.
- Lumină: HDRI (`lebombo_1k.hdr`) ca IBL, soare, emisferă, lumini de cameră; umbre recalculate doar la schimbări; umbre de contact
  coapte la runtime (`buildContact`). Tone mapping ACES, calibrat pe r128 (`EXPOSURE_GAIN`, `PL`), cu inversare în `untone`.
- `createViewer` e folosit și de `PresentView`, `PrintView`, `lib/share-images.ts`, pe pânze ascunse. Densitatea lor e presupusă
  în `lib/present-render.ts`, deci **exporturile nu trebuie să se schimbe**.
- Asincron: texturile de normale (la nivel de modul), HDRI, modelele GLTF (`decoWaiters`), `onTexturesReady`.

## Decizii (și de ce)
- **Bucla pornește la cerere:** `invalidate()` programează un singur `requestAnimationFrame`. Cadrul simulează, desenează, apoi
  cere altul doar dacă ceva încă se mișcă. Fără cereri, nimic nu rulează, deci 0 cadre în repaus și pe pânzele ascunse.
- **Ce declanșează un cadru:** orice metodă din API-ul motorului (ambalaj unic, ca să nu se poată uita una), intrările de la
  pointer, roată și taste relevante, schimbarea hover-ului, încărcările asincrone (`DefaultLoadingManager`), contextul restaurat.
- **Când „încă se mișcă” (funcție pură, `core/render-demand.ts`):** taste de navigare ținute, joystick, uși neajunse la țintă,
  alunecare, cameră sau țintă la peste 1 mm de destinație. La sub 1 mm camera sare exact pe destinație, ca ultima imagine să fie stabilă.
- **Redimensionarea desenează sincron** în `ResizeObserver`: `setSize` golește bufferul, iar `requestAnimationFrame` din acel
  cadru a trecut deja (specificația WebGL, cercetare rândul 2).
- **Tastele se resetează la `blur`.** Înainte, o tastă rămasă apăsată doar mișca imaginea; acum ar ține bucla pornită la infinit.
- **Primul pas după repaus are dt de 1/60 s**, nu cel plafonat la 50 ms: altfel camera ar sări la fiecare trezire.
- **Niveluri, `core/viewport-tiers.ts` (pur):**

  | Nivel | Densitate maximă | SSAO | Umbre | Lumini de cameră |
  |---|---|---|---|---|
  | `desktop` | 2 | mereu | 2048 | 4 |
  | `laptop` | 1,5 | doar în repaus | 2048 | 4 |
  | `phone` | 1,5 | oprit | 1024 | 2 |

- **Clasarea e locală:** tactil pe ecran sub 600 px → `phone`; randator software → `phone`; tactil mai mare → `laptop`; altfel
  `desktop` de la 8 nuclee (și, când există, 8 GB), altfel `laptop`. Fără `detect-gpu`: ar face cereri către terți (rândul 16).
- **Adaptare doar în jos:** eșantioane de cadre consecutive în mișcare; mediana peste 40 ms pe 60 de cadre coboară un nivel
  (33 ms ar pedepsi un ecran de 30 Hz; p95 a coborât pe nedrept nivelul la măsurare, din cauza compilării shaderelor). Se numără doar cadrele din bucla continuă, nu cele trezite de un mouse lent. Nu urcă singur în aceeași sesiune, deci nu poate oscila (rândul 14). Alegerea manuală oprește adaptarea.
- **Alegerea utilizatorului:** butonul „Calitate” devine o listă „Automat / Desktop / Laptop / Telefon”, memorată în acest
  browser (`casa3d.viewerTier`). Un singur control în loc de două care s-ar contrazice.
- **Exporturile rămân neschimbate:** `createViewer` fără nivel se comportă ca azi; doar `Viewer3D.tsx` trimite un nivel.
- **SSAO doar în repaus (laptop):** comutarea reface și fundalul (`applyBackdrop`), fiindcă cele două căi tratează diferit culoarea lui.
- **Tone mapping: rămâne ACES.** Comparat pe aceleași cadre cu Neutral: la luminozitate egală, pereții ies până la de 2,4 ori mai galbeni
  decât vopseaua, chiar și cu luminile albite (cifrele sunt în cercetare). Nu s-a schimbat nimic în motor.
- **Lumina coaptă:** nu coacem dinainte (planurile se editează). Rămân harta de umbre statică și umbrele de contact (rândul 12).
- **Path tracing: nu.** Linia întreținută cere WebGPU (motive în cercetare).
- **WebGL2 rămâne baza; three.js rămâne pe r186.** r187 are 2 zile și schimbă densitatea de pixeli.
- **Reducerea apelurilor de desen e felie nouă,** cerută de măsurători (CPU = 95 % din cadru): mobilierul înghețat se unește pe
  material. Piesele originale rămân pentru selecție, coliziuni și export. Intră doar dacă selecția, oglinzile și exporturile
  rămân corecte; altfel se raportează ca rămas de făcut.

## Felii (test întâi)
1. **Randare la cerere.** `tests/render-demand.test.ts` (ce înseamnă „încă se mișcă”, saltul final, dt la trezire), apoi motorul.
   Dovadă: `measure-viewport.mjs`, 0 cadre în repaus în machetă, după orbitare și în tur.
2. **Niveluri.** `tests/viewport-tiers.test.ts` (clasare, parametri, adaptare, valori stricate din stocare), `setTier` în motor,
   lista din `Viewer3D.tsx`, RO + EN.
3. **Lumină și tone mapping.** Comparația de imagini: Neutral a pierdut, deci nu există `core/tone.ts`. Refacerea contextului generează din nou mediul IBL și aplică starea curentă (defect găsit la verificare).
4. **Apeluri de desen.** `components/static-batch.js` cu `tests/static-batch.test.ts`: unirea pe material și pe celule de 4 m, verificare de selecție, hover, coliziuni și export (originalele rămân).

## Verificare
`npx tsc --noEmit`, `npx vitest run --maxWorkers=2`, `scripts/check-a11y.mjs`, `scripts/measure-viewport.mjs` înainte și după,
aplicația reală în browser (RO + EN, telefon și desktop, încadrarea textului, capturi trimise proprietarului), comparația
vizuală cu aceeași cameră (`capture-renders.mjs`, `compare-renders.mjs`), `vault-reviewer` pe diff (cel mult 2 runde).

## În afara fazei
Rândurile 7 și 9; faza 4 (ISO 7200), care are nevoie de standardul cumpărat; mutarea pe WebGPU; GTAO.
