# Casa3D — faza A1.3: layoutul spațiului de lucru (rândul 4 din A1)

Sursa: `docs/research/UI_UX_MOTION_APLICATII_COMPLEXE_2026.md` §A1 rândul 4 (PR #318). Checkpoint:
`00_GOVERNANCE/coordination/tasks/todo-claude-casa3d-a1-roadmap.md`, faza 2.
Bază: `origin/main` `a32613fba`. Ramura `claude/casa3d-a1-3-workspace`, worktree `D:/w/a13`.

## Deciziile proprietarului (2026-10-24)
- Noul layout e **activ pentru toți**; preferința „Layout clasic” (meniul ⋯ și Ctrl+K) readuce exact ecranul de azi.
- Catalogul **rămâne în dreapta** (etapa Mobilier); panoul stâng are doar structura.
- Pe telefon și tableta îngustă, foaia de jos primește comutarea **Structură / Panou**; fără coloane noi.

## Ce există deja (inventar pe main, 2026-10-24)
- `Editor.tsx`: `.body` = grilă `ToolRail 64px | canvas | aside` (aside 340–420 px; „panou lat” 480–760 px, memorat în
  `casa3d.sideWide`). Aside = titlul etapei + `StageMeta` + inspectorul selecției sau, fără selecție, panourile etapei
  (proiect, catalog, concept, prezentare). Deci inspectorul e deja contextual (scenă fără selecție, obiect cu selecție).
- Pe telefon (`useCompact`): aside devine foaie de jos `peek/half/full`.
- `core/plan-describe.ts` `describePlan` → grupuri pe cameră (camera, zone, piese, obiecte pe perete), piese în afara
  camerelor, Structură (pereți urmați de golurile/nișele/obiectele lor, stâlpi, grinzi, sticlă, scări), Note.
  `PlanObjects.tsx` le arată ca listă ascunsă până la focus (a11y P3-16); `levelView(house, i)` dă orice nivel.
- Niciun outliner vizibil, niciun panou redimensionabil, niciun mod Focus. Ordinea câmpurilor din inspector nu e păzită.

## Decizii (și de ce)
- **Starea layoutului e pură și validată**, `lib/workspace-layout.ts`: lățimi stânga/dreapta cu limite (stânga 200–420,
  implicit 260; dreapta 320–760, implicit 380), outliner deschis/închis, modul (`studio` | `classic`). Se citește din
  `localStorage` (`casa3d.workspace`, JSON versionat) și orice valoare stricată cade pe implicit; scrierea tolerează
  stocarea blocată. Implicitul modului vine dintr-un flag de build `NEXT_PUBLIC_CASA3D_LAYOUT` (`studio` implicit,
  `classic` = comutator de urgență), iar alegerea utilizatorului îl bate.
- **Outlinerul e arborele aceluiași model ca lista a11y**, `core/outliner.ts` `buildOutliner(levels)`: nivel → camere
  (camera e nodul, copiii: zone, piese, obiecte pe perete) → piese în afara camerelor → Structură (perete → goluri, nișe,
  obiecte) → Note. `describePlan` primește `parent` (id-ul peretelui) pe golurile/nișele/obiectele de sub un perete, ca
  arborele să nu ghicească din ordine. Etichetele sunt cele din `mirror.*` (aceleași cu cititorul de ecran).
- **`components/Outliner.tsx`**: `role="tree"` (APG): o singură oprire Tab, săgeți sus/jos, dreapta deschide/intră,
  stânga închide/urcă, Home/End, Enter = selectează și încadrează, Space = selectează, Escape = focus în plan. Clic pe un
  rând selectează (pe alt nivel: schimbă nivelul, apoi selectează); hover pe o piesă o evidențiază în plan și 3D. Calea
  spre selecție se deschide singură, rândul ales intră în vedere. În layoutul nou outlinerul ține locul listei ascunse
  (`PlanObjects` rămâne în layoutul clasic): două opriri Tab pentru același lucru ar fi zgomot.
- **Panouri andocate, redimensionabile:** separatoare `role="separator"` cu `aria-valuenow/min/max`, tragere cu
  pointerul, săgeți ±16 px, Shift ±64, Home/End, dublu clic = implicit; lățimea se salvează la eliberare. Canvasul păstrează
  minimum 360 px (lățimile se strâng la fereastră îngustă, nu se suprapun). În layoutul nou butonul „panou lat” dispare
  (redimensionarea îl înlocuiește); în cel clasic rămâne cum e.
- **Focus** (Ctrl+\, ca „ascunde UI” din Figma): ascunde outlinerul, inspectorul și bara etapelor; rămân bara de sus și
  uneltele; un buton „Ieși din Focus” stă vizibil. Nu se memorează (e o stare de moment, ca Focus Mode în Blender).
- **Registrul de comenzi:** `ws.focus` (Ctrl+\), `ws.outliner` și `ws.classic` (fără tastă) intră în `lib/commands.ts`
  și în Ctrl+K, cu eticheta RO/EN; scurtătura apare în tooltip și în paletă, ca restul.
- **Ordinea câmpurilor din inspector e înghețată:** fiecare bloc de inspector din `Editor.tsx` primește un marcaj
  `{/* inspector:<tip> */}`; `tests/inspector-order.test.ts` extrage, în ordinea sursei, cheile `t('…')` ale fiecărui
  bloc și le compară cu un snapshot scris în test. Reordonarea unui câmp pică testul și cere schimbarea explicită a
  snapshotului (cercetarea: „nu reordona câmpurile frecvente între versiuni”).
- **Telefon:** în foaia de jos, un control segmentat Structură / Panou; „Structură” arată outlinerul în foaie.

## Felii (test întâi, apoi cod)
1. `lib/workspace-layout.ts` + `tests/workspace-layout.test.ts`: implicite, limite, JSON stricat, versiune necunoscută,
   flagul, alegerea utilizatorului peste flag, strângerea la fereastră îngustă.
2. `core/outliner.ts` + `parent` în `describePlan` + `tests/outliner.test.ts`: nivel → cameră → copii, perete → goluri,
   piese libere, niveluri multiple, calea spre o selecție.
3. `components/Outliner.tsx` + separatoarele + cablarea în `Editor.tsx` (grilă nouă, Focus, preferința clasică, comenzile
   noi, foaia de telefon), CSS cu tokenii existenți; `scripts/check-a11y.mjs` auditează outlinerul (și lista clasică).
4. Marcajele inspectorului + `tests/inspector-order.test.ts`.

## Verificare
- `npx tsc --noEmit`, `npx vitest run --maxWorkers=2`.
- Browser pe aplicația reală (`npm run dev`, 127.0.0.1:3200): outlinerul pe un proiect cu 2 niveluri, clic și tastatură,
  redimensionare și reîncărcare (lățimile rămân), Focus cu Ctrl+\ și buton, „Layout clasic” dus-întors, telefon 390 px
  (Structură / Panou), RO+EN, textul încape, contrast; capturi trimise proprietarului.
- `vault-reviewer` (Opus) pe diff înainte de PR; cel mult 2 runde.
