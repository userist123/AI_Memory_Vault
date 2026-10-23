# Casa3D — faza A1.1: comenzi, precizie, undo dovedit

Sursa: `docs/research/UI_UX_MOTION_APLICATII_COMPLEXE_2026.md` §A1, rândurile 1–3 („o singură fază coerentă”).
Proprietarul a ales A1 pe 2026-10-22. Checkpoint: `00_GOVERNANCE/coordination/tasks/todo-claude-casa3d-a1.md`.
Bază: `origin/main` `f0ee40d67`. Ramura `claude/casa3d-a1-precision`, worktree `D:/w/a1`.

## Ce există deja (inventar pe `origin/main`, 2026-10-22)
- Paleta Ctrl/⌘K (`components/CommandPalette.tsx`, `lib/fuzzy.ts`, `lib/palette-keys.ts`), dar comenzile sunt o listă
  inline în `Editor.tsx` (~L436), tastele sunt un `if/else` separat (`Editor.tsx` ~L311-325), ajutorul e un tabel static
  (`components/KeyboardHelp.tsx` `ROWS`). Doar undo/redo/paleta își arată scurtătura în tooltip. Uneltele nu au taste.
- Desenare: grilă 5 cm (`PlanView.tsx` `G`), snap la capete de pereți 0,2 m (`core/geometry.ts` `snapPoint`), `ortho()`
  la 15 % abatere. Fără bypass, fără unghi, fără lungime tastată, fără indicator de snap.
- Undo: instantanee complete, 100 de pași (`core/history.ts`), autosave pe server la 700 ms, revizii pe server.
  Niciun test dedicat pentru `History`.

## Decizii (și de ce)
- **Registru = date, nu componente.** Un tabel pur `lib/commands.ts` (id, cheie de etichetă, grup, scurtături, domeniu).
  Tastatura, paleta, tooltip-urile și ajutorul citesc același tabel, deci nu mai pot diverge. Testabil fără DOM.
- **Undo rămâne pe instantanee.** Pentru un proiect de zeci de KB e corect și simplu; raportul cere invariantul, nu
  pattern-ul de comandă în sine. Dovedim invariantul cu teste; trecerea la comenzi doar dacă un test o cere.
- **Bypass cu Shift**, nu Alt: Alt+drag face deja pan în plan (`PlanView.tsx` `down`).
- **Taste pentru unelte** (litere, fără modificator, ignorate în câmpuri și dialoguri ca acum prin `editorIgnoresKey`):
  V selecție, P perete, C cameră, U ușă, O fereastră (F încadrează planul, decizia proprietarului 2026-10-23), T stâlp, G grindă, L perete de sticlă, E scară (nu S: W/A/S/D merg prin vederea 3D), M măsurare, I instalații.
  Literele urmează numele românești; aceleași taste în engleză (afișate în ajutor). R rămâne rotire.
- **Indicatorul de precizie e o cotă de arhitect** (limbajul planșei, nu o etichetă plutitoare): linie de cotă paralelă cu
  segmentul în lucru, decalată, cu tick-uri la 45°, lungimea pe linie, unghiul lângă capătul liber; la punctul de snap un
  glif mic: pătrat = capăt de perete, cruce = grilă, arc = unghi, cerc gol = liber (Shift). Doar tokenii existenți ai
  planului (`PlanView.tsx` folosește deja `#B8432C` pentru cote), fără culori noi, fără animație.
- **Lungime tastată:** cât timp un segment e în lucru, cifrele (și `,` `.`) intră într-un câmp afișat pe cotă; Enter
  fixează segmentul la acea lungime pe direcția curentă; Backspace corectează; Esc golește câmpul, al doilea Esc anulează.
  Unitatea urmează preferința `units` existentă.

## Felii (test întâi, apoi cod)
1. **Registru de comenzi.** `lib/commands.ts` + `tests/commands.test.ts`. Gata când: fiecare scurtătură e unică în
   domeniul ei; fiecare comandă are etichetă în `ro` și `en`; tabelul de regresie al tastelor vechi (Ctrl+Z/Y/Shift+Z/D,
   ?, săgeți, Shift+săgeți, Delete/Backspace, R, Esc, Ctrl/⌘K) dă aceleași acțiuni; `KeyboardHelp` se generează din registru;
   paleta listează toate uneltele cu scurtătura; tooltip-urile uneltelor conțin scurtătura. 100 % din acțiunile din
   toolbar în registru (test care compară `Tool` cu registrul).
2. **Precizie.** `core/snap.ts` (pur) + `tests/snap.test.ts`, apoi `PlanView.tsx`. Prioritate: capăt de perete >
   unghi (pași de 15°, lungimea rotunjită la grilă pe direcție) > grilă; Shift = liber (rotunjit la 1 mm).
   `parseLengthInput(text, units)` cu cazuri: „4”, „4,25”, „4.25”, „425” (fără unitate: ≤ 30 = metri, peste 30 = cm, după review: „80” e 80 cm, nu 80 m), „4,25 m”, gol, „abc”, negativ, zero.
   Gata când: testele trec, iar în browser peretele de 4,00 m tastat are exact 4,000 m în proiect.
3. **Undo dovedit.** `tests/history.test.ts`: N editări aleatoare (seed fix) → undo N → copie → redo N = document identic;
   limita de 100 aruncă cel mai vechi; `begin`/`rollback` restaurează redo; `rollback` refuzat după altă operație.

## Verificare (bucla mea, nu a proprietarului)
- `npm run typecheck`, `npm test` (Vitest), `next build`.
- Pe PC-ul real, aplicația reală (nu demo), cu capturi: desenez o cameră 4,00 × 5,00 m numai din tastatură, Shift
  dezactivează snap-ul, paleta arată uneltele cu tastele, tooltip-urile arată tasta, ajutorul `?` e complet, RO și EN,
  textul încape, contrastul cotei trece AA pe fundalul planului.
- `vault-reviewer` (Opus) pe diff înainte de PR.

## Ce măsurăm (din raport)
- % acțiuni din toolbar în registru (țintă 100 %).
- Timp pe „cameră 4,00 × 5,00 m cu cote exacte” înainte/după; erori de cotă = 0.
- Invariantul undo ca test automat.
