# Casa3D BIM — Faza 0: inventar, gap analysis și plan

Ghidul aplicat: `docs/bim/GHID_BIM.md` (SHA-256 `0244b68be6bde79a9d13e733fe99cd4af5c415381aefe7a50c990cf4cc290b0b`).
Bază constituțională: `CONSTITUTION.md` art. 14 (pista BIM, amendament 2026-11-21).
Data inventarului: 2026-11-21, pe `origin/main` `aa42387f2`.

Legendă: **REAL** = implementat și acoperit de teste; **PARTIAL** = există, cu limitări numite; **UNKNOWN** = neimplementat sau neverificat.

## 1. Ce există azi față de ghid

| Ghid | Stare | Unde | Limită / lipsă |
|---|---|---|---|
| Sistem de coordonate și unități (§2, §5) | REAL | `core/types.ts` (metri), `core/dxf.ts` (mm la export) | — |
| Identitate stabilă a elementelor (§3.2) | REAL | `id` pe cameră, perete, gol, scară, stâlp, grindă, nișă, zonă, balustradă, piesă, punct tehnic; unicitatea e verificată de `duplicateElementIds` în `core/elements.ts` | — |
| Disciplină și tip pe element (§3.2, §3.6) | REAL (din 2026-11-21) | `core/elements.ts` — `elementIndex`, `elementsOf` | derivat, nu salvat; disciplina vine din tipul elementului |
| Proveniență pe element: sursă, metodă, confidence, stare (§3.2) | UNKNOWN | — | elementele de azi nu au sursă declarată; nu se completează cu valori implicite (art. 4) |
| Versiuni, diff, rollback (§3.6, §7) | REAL | tabela `revisions` (`lib/schema.ts`), `core/diff.ts`, rutele `revisions/*` și `restore` | diff-ul acoperă structura și stratul tehnic din 2026-11-21; nu compară scări, nișe, pereți de sticlă, balustrade, zone |
| Import plan raster + calibrare (§3.1) | PARTIAL | `core/underlay.ts` (scară din două puncte, blocare până la confirmare), `lib/repo.ts` `checkUnderlay` | imaginea e redusă la ≤ 2000 px / JPEG în browser; originalul și hash-ul lui nu se păstrează |
| Import PDF vectorial, DXF/DWG, IFC (§3.1) | UNKNOWN | — | niciun parser în `package.json`; DWG cere licență (ODA cu plată, LibreDWG GPLv3) — de verificat la zi |
| Interpretare automată a planului, OCR (§3.2) | UNKNOWN | — | art. 14: doar local, experimental |
| Model arhitectural (§3.3) | REAL | pereți cu grosime și stare (existent/nou/demolat), goluri, niveluri, scări, tavane coborâte, nișe | acoperișul lipsește; „derivat / inferat / manual / aprobat” nu se distinge |
| Structură (§3.4) | PARTIAL | `Column` (structural / ghenă / casetare), `Beam` | fără fundații, planșee structurale, pereți structurali marcați; fără dimensionare (corect, conform art. 12) |
| Instalații (§3.5) | PARTIAL | `core/technical.ts` — puncte (priză, apă, scurgere, calorifer, AC) cu `circuitId`, `switchGroup`, `linkedIds` | puncte, nu trasee: fără conducte, tubulaturi, diametre, pante, conectivitate |
| Model federat (§3.6) | REAL (din 2026-11-21) | `elementsOf(snap, disciplină)` peste un singur Snapshot | filtrul nu e încă expus în UI |
| Clash detection hard / clearance (§3.7) | PARTIAL | `core/validate.ts` (suprapuneri în plan, zone de ușă, gabarite), `core/trade-checks.ts` | 2D/2,5D; fără verificare 3D între grinzi, tavane coborâte, ghene, corpuri tehnice și mobilier înalt |
| Issue-uri de coordonare (§3.7) | PARTIAL | `core/coordination-review.ts` — id stabil `cf-xxxxxxxx`, deduplicare, `runCoordination` cu revizia, regulile rulate și nerulate | fără status persistat (`new/reviewed/accepted/resolved/false_positive`), responsabil, captură; nu e afișat în UI |
| „Zero clashes” cu domeniul testat (§3.7) | REAL (din 2026-11-21) | `runCoordination(...).statement` | — |
| Adaptor Blender (§4.1) | PARTIAL | `lib/renders.ts` → `tools/blender/casa3d_render.py` | sens unic (randare); id-ul elementului ajunge pe obiect (`c3d_id`) pentru stâlpi, grinzi, puncte tehnice, obiecte pe perete și mobilier; pereții și plăcile nu au încă id (planul vechi `pereti` nu îl poartă) |
| Export (§4.2) | PARTIAL | `core/dxf.ts` (DXF R12, straturi), GLB din vederea 3D | fără IFC |
| Audit și aprobare (§7) | PARTIAL | revizii numerotate, `changes` cu aprobare de client | fără jurnal per element al modificărilor automate |

## 2. Ce s-a schimbat în această fază

- `core/elements.ts` — indexul de elemente (id, disciplină, tip, nivel) și verificarea de unicitate.
- `core/diff.ts` — `structure` (stâlpi, ghene, casetări, grinzi) și `tech` (strat tehnic); intră în `isEmpty`.
- `components/RevisionDiff.tsx`, `lib/locales/{ro,en}.ts` — rândurile noi în comparația reviziilor.
- `core/coordination-review.ts` — `id` stabil pe constatare, deduplicare, `findingId`, `runCoordination`, `COORDINATION_RULES`, `TECHNICAL_RULES`.
- `components/viewer3d-engine.js`, `tools/blender/casa3d_render.py` — id-ul elementului în scena exportată și pe obiectul Blender.
- `CONSTITUTION.md` — art. 14; `docs/bim/` — ghidul și acest inventar.
- Teste: `tests/bim-core.test.ts`.

Toate schimbările sunt aditive: nicio cheie nouă în Snapshot, nicio migrare, revizii vechi neatinse.

## 3. Ordinea fazelor următoare (câte una pe sesiune, cu STOP GATE)

1. **Faza 1b — proveniență și sursă.** Hash SHA-256 al fișierului original la încărcarea planului (calculat în browser înainte de
   reducere, salvat lângă underlay); câmpuri opționale `origin` (`source|inferred|manual|approved`) pe elementele create din import.
   Acceptare: hash-ul se păstrează la salvare și revizie; elementele vechi rămân fără câmp.
2. **Faza 2 — import vectorial.** DXF (linii/polilinii pe straturi) → pereți candidați cu `review_status: candidate`, revizie umană
   obligatorie. Parserul se alege după verificarea licenței și rulează pe server cu limite de mărime (fișier neîncredere).
3. **Faza 3 — Blender.** Id și pe pereți/plăci; colecții pe disciplină; test pe scena exportată.
4. **Faza 4 — structură și instalații.** Trasee (segmente cu diametru/secțiune declarate sau `UNKNOWN`), fără dimensionare.
5. **Faza 5 — coordonare 3D.** Hard clash și clearance cu teste analitice (cutii orientate) pe elementele 3D existente, toleranță
   explicită, intersecții intenționate excluse prin regulă; status persistat pe id-ul `cf-…`; panou în UI.
6. **Faza 6–7** — UI, livrabile, IFC export (spike separat), adaptoare.

Riscuri: un raport „0 constatări” citit drept conformitate (de aceea `statement` numește limita); regresie de performanță în vederea 3D
la geometrie de instalații; parsere noi pe fișiere neîncredere.
