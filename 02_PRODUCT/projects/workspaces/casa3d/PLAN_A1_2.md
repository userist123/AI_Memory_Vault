# Casa3D — faza A1.2: completarea rândurilor 1–3 din A1

Sursa: `docs/research/UI_UX_MOTION_APLICATII_COMPLEXE_2026.md` §A1 (PR #318), rândurile 1–2: ce a rămas după A1.1 (#323).
Proprietarul a cerut toate fazele pe rând (2026-10-23). Checkpoint: `00_GOVERNANCE/coordination/tasks/todo-claude-casa3d-a1-roadmap.md`.
Bază: `origin/main` `dd6c9ba34`. Ramura `claude/casa3d-a1-2-completion`, worktree `D:/w/a12`.

## Ce există deja (inventar pe main, 2026-10-23)
- Cota peretelui în plan: `<text>` cu `pointerEvents="none"` (`PlanView.tsx`, harta `floor.walls`); lungimea se schimbă
  doar din inspector (`Editor.tsx`, câmpul „Lungime”): mută capătul `b`, pereții lipiți de `b` rămân pe loc (colț rupt).
- Previzualizarea segmentului: o linie punctată de 15 cm, translucidă; nu arată grosimea reală și nici îmbinarea.
- Magnetul (`core/snap.ts`): capăt de perete › unghi › grilă. Nu prinde corpul unui perete (îmbinare în T).
- Clic dreapta în plan: doar anulează segmentul în lucru. Nu există meniu contextual. Bara plutitoare a piesei alese
  (`hud`) are rotire / dublare / 3D / mobilare / împărțire / ștergere, fără scurtături.
- Bug-uri: (1) Esc cu o selecție în timpul desenării doar deselectează (`plan-keys` întoarce `deselect` și oprește
  propagarea, deci ascultătorii care anulează desenul nu primesc tasta); (2) Delete, și Backspace fără text tastat, în
  timpul desenării ajung la `del()` și șterg selecția; (3) `typedEndpoint` nu are consumator în producție.

## Decizii (și de ce)
- **O singură funcție pentru lungimea peretelui**, `setWallLength(floor, wallId, len)` în `core/wall-edit.ts`, pură:
  capătul `a` rămâne fix, `b` se mută pe aceeași direcție, iar capetele altor pereți care coincideau cu vechiul `b`
  (sub 1 mm) se mută odată cu el, deci colțul rămâne închis. Refuză (eroare cu cheie i18n) sub 20 cm, peste 1 km și când
  un gol (ușă/fereastră) n-ar mai încăpea. Inspectorul și cota din plan o folosesc amândouă: un singur comportament.
- **Cota editabilă pe loc:** în unealta Selecție, dublu clic pe cota unui perete (sau „Editează lungimea” din meniul
  contextual) deschide un câmp mic, HTML, peste cotă, cu lungimea curentă selectată. Enter aplică prin
  `parseTypedLength` (aceeași regulă: întreg = cm, zecimal = m), Esc renunță, câmpul invalid se marchează și nu se
  închide. Un singur pas de undo (`mutate`). Dublu clic, nu clic simplu: clicul simplu selectează peretele, ca acum.
- **Previzualizarea îmbinării:** segmentul în lucru se desenează la grosimea reală a elementului (perete 15 cm, sticlă
  8 cm, grindă lățimea implicită) cu același capăt pătrat ca peretele final, deci colțul în L arată exact cum va fi.
  Magnetul primește un al patrulea tip, `wall` (punct pe axa unui perete existent, în rază; prioritate: capăt › perete ›
  unghi › grilă), cu glif propriu (T) — îmbinare în T fără să nimerești un capăt.
- **Meniul contextual din registru:** `lib/commands.ts` primește `contextCommands(kind)` (pur): ce comenzi se aplică
  selecției (perete: editează lungimea, încadrează, șterge; piesă: rotește, dublează, arată în 3D, încadrează, șterge;
  cameră: mobilează, împarte, încadrează, șterge; stâlp: rotește, formă, șterge; scară: rotește, șterge; grindă/sticlă:
  încadrează, șterge). Eticheta și scurtătura vin din registru (se adaugă `frame` = F, doar afișare, și comenzile fără
  tastă). Deschidere: clic dreapta pe un obiect în Selecție (îl și selectează), Shift+F10 sau tasta Menu cu planul în
  focus și o selecție (la centrul selecției). `role="menu"`, săgeți/Home/End, Enter, Esc închide și întoarce focusul pe
  plan; clic în afară închide. În timpul desenării clic dreapta rămâne „anulează segmentul”.
- **Bug-uri:** în timpul desenării Esc anulează desenul înainte de orice deselectare (ca fără selecție: anulează și
  revine la Selecție); Delete și Backspace sunt înghițite de plan cât timp un segment sau o cameră e în lucru;
  `typedEndpoint` se șterge (cu testele lui), `pointAtLength` îl acoperă.

## Felii (test întâi, apoi cod)
1. `core/wall-edit.ts` + `tests/wall-edit.test.ts`: colț în L urmărit, T neatins, gol care nu mai încape refuzat,
   limite, imutabilitate. Inspectorul trece pe `setWallLength`.
2. `core/snap.ts` tipul `wall` + teste în `tests/snap.test.ts`; previzualizarea la grosime reală și gliful T în `DrawHud`.
3. `contextCommands` + teste în `tests/commands.test.ts` (fiecare comandă are etichetă ro/en, fiecare tip de selecție are
   cel puțin „șterge”, scurtăturile afișate = registrul); `components/PlanContextMenu.tsx`; cablare în `PlanView`/`Editor`.
4. Cota editabilă în plan (dublu clic + meniu), bug-urile 1–3, `typedEndpoint` șters.

## Verificare
- `npx tsc --noEmit`, `npx vitest run --maxWorkers=2`.
- Browser, aplicația reală (`npm run dev`, 127.0.0.1:3200): colț 4×5 închis, dublu clic pe cota 400 → „450” → pereții
  rămân legați, un singur Ctrl+Z revine; perete care pornește în T pe alt perete; meniu cu mouse și cu Shift+F10, RO+EN,
  textul încape; Esc/Delete în timpul desenării; capturi trimise proprietarului.
- `vault-reviewer` (Opus) pe diff înainte de PR; cel mult 2 runde.
