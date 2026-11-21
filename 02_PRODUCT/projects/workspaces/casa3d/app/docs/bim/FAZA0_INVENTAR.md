# Casa3D BIM — inventar, stare și limite

Ghidul aplicat: `docs/bim/GHID_BIM.md` (SHA-256 `0244b68be6bde79a9d13e733fe99cd4af5c415381aefe7a50c990cf4cc290b0b`).
Bază constituțională: `CONSTITUTION.md` art. 14 (pista BIM, amendament 2026-11-21). Adaptoare: `docs/bim/ADAPTERS.md`.
Bază de pornire: `origin/main` `aa42387f2` (2026-11-19); branch `claude/casa3d-bim-faza0-1`.

Legendă: **REAL** = implementat și acoperit de teste (și verificat în aplicație unde se spune); **PARTIAL** = există, cu limitele
numite; **UNKNOWN** = neimplementat. Totul de mai jos e marcat *experimental* în interfață (art. 10).

## 1. Starea pe cerințele ghidului

| Ghid | Stare | Unde | Limite |
|---|---|---|---|
| Coordonate și unități explicite (§2, §5) | REAL | metri în model; mm în DXF; IFC în metri, X = x, Y = −z, Z = cota | — |
| Identitate stabilă (§3.2) | REAL | id pe fiecare element; `core/elements.ts` `duplicateElementIds`; traseele și fundațiile unice pe toată casa (`checkSnapshot`) | camerele/pereții vechi verifică unicitatea doar între ei (regula existentă) |
| Disciplină și tip (§3.2, §3.6) | REAL | `elementIndex`, `elementsOf` | derivate din tip, nesalvate |
| Proveniență (§3.2) | PARTIAL | `Underlay.source` (SHA-256, nume, mărime, tip, dată); `Wall.origin` pentru pereții din DXF (metodă, amprentă, strat, grosime măsurată sau manuală) | elementele desenate în aplicație nu au proveniență declarată (nu se inventează una) |
| Stări candidate / revizuit / aprobat (§3.2) | PARTIAL | importul DXF: candidați în panou, intră în proiect doar la aprobare | starea nu se salvează per element; în proiect intră doar ce e aprobat |
| Versiuni, diff, rollback (§3.6, §7) | REAL | revizii, `core/diff.ts` (camere, pereți, structură, instalații, trasee, mobilier, cost, aspect), restore | scările, nișele, pereții de sticlă și zonele nu au contor separat în diff |
| Import raster + scară (§3.1) | REAL | `core/underlay.ts`, calibrare din două puncte, amprenta originalului | imaginea de lucru e redusă la ≤ 2000 px |
| Import DXF vectorial (§3.1) | REAL | `core/dxf-import.ts`, `DxfImportPanel.tsx`; unitatea obligatorie când lipsește; perechi de linii → pereți cu grosime măsurată | arce, blocuri, goluri și camere nu se recunosc; verificat în aplicație pe un DXF de test |
| PDF vectorial, DWG, import IFC, OCR (§3.1–3.2) | UNKNOWN | — | vezi `ADAPTERS.md` „Neacoperit” |
| Model arhitectural (§3.3) | REAL | pereți, goluri, niveluri, scări, tavane, nișe (existent) | acoperișul lipsește |
| Structură (§3.4) | REAL ca reprezentare | stâlpi, grinzi, `Wall.structural`, `Floor.footings`; fără dimensionare (art. 12) | planșee structurale ca elemente separate: UNKNOWN |
| Instalații (§3.5) | REAL ca reprezentare | `Floor.mepRuns` (7 sisteme), propunere ortogonală între puncte tehnice, diametru/material/pantă declarate sau necunoscute, verificări de pantă | fără fitinguri, fără conectivitate de rețea, fără dimensionare |
| Model federat, filtre (§3.6, §6) | REAL | vederea 3D: federat / disciplină / revizie interferențe; filtre pe disciplină în panoul de coordonare | planul 2D nu are încă filtru pe disciplină |
| Hard clash, clearance (§3.7) | REAL | `core/clash.ts`: pereți tăiați la goluri, stâlpi, grinzi, fundații, tavane false, mobilier, trasee; toleranță = suprapunerea ignorată; clearance doar cu distanțe definite de proiect | stâlpul rotund aproximat cu cutia; scările și corpurile tehnice (calorifer, AC) nu intră; ramificațiile în T ale aceluiași sistem apar ca avertisment |
| Rule clash (§3.7) | PARTIAL | regulile existente (validatoare, puncte tehnice, finisaje, prețuri) + proprietățile nedeclarate ale traseelor | reguli definite de utilizator: UNKNOWN |
| Issue-uri cu status, justificare, înainte/după, raport (§3.7) | REAL | `core/issues.ts`, `Snapshot.issues`, `CoordinationPanel.tsx`, raport Markdown | captura 3D per constatare: UNKNOWN |
| „Zero” doar cu domeniul testat (§3.7) | REAL | fraza de raport numește regulile rulate și nerulate și limita asistivă | — |
| Adaptor Blender (§4.1) | REAL | id pe obiect (`c3d_id`), colecții `Casa3D_<disciplină>`; verificat pe Blender 5.2 | sens unic (randare) |
| DXF export (§4.2) | REAL | straturi `MEP_<SISTEM>`, `STRUCT_FOOTINGS`, `STRUCT_WALLS` | — |
| IFC4 export (§4.2, §5) | REAL (experimental) | `core/ifc-export.ts`, `/api/projects/[id]/export.ifc`; validat cu IfcOpenShell | vezi `ADAPTERS.md` |
| Registru de adaptoare / plugin API (faza 7) | REAL | `core/adapters.ts`, `docs/bim/ADAPTERS.md`, teste de compatibilitate | adaptoarele se adaugă în cod, nu se încarcă dinamic |
| GIS, AI Orchestrator (§4.2, §5) | UNKNOWN | — | în afara produsului actual |

## 2. Fișiere principale

`core/elements.ts`, `core/diff.ts`, `core/coordination-review.ts`, `core/dxf-import.ts`, `core/mep.ts`, `core/clash.ts`,
`core/issues.ts`, `core/ifc-export.ts`, `core/adapters.ts`, `core/scene-ids.ts`; `components/DxfImportPanel.tsx`, `MepPanel.tsx`,
`CoordinationPanel.tsx`, `Viewer3D.tsx`, `viewer3d-engine.js`, `PlanView.tsx`; `lib/repo.ts`, `lib/viewer-input.ts`, `lib/ifcExport.ts`;
`tools/blender/casa3d_render.py`; testele `tests/bim-*.test.ts`.

Câmpuri noi în model, toate opționale (proiectele vechi rămân neschimbate, verificat de testul golden din lotul 4):
`Underlay.source`, `Wall.origin`, `Wall.structural`, `Floor.mepRuns`, `Floor.footings`, `Snapshot.issues`.

## 3. Verificări făcute

- `npx tsc --noEmit` și `npx vitest run` la fiecare fază (rezultatele sunt în mesajele de commit și în PR).
- În aplicația pornită local (Playwright): import DXF (colțuri închise, proveniență salvată pe server), trasee și fundații,
  verificarea coordonării și salvarea statusului (justificarea obligatorie refuză salvarea fără ea), vederile pe disciplină în 3D,
  exportul IFC (200 pentru proprietar, 404 pentru alt proiect).
- Blender 5.2: scena exportată → obiectele în colecțiile disciplinelor, cu `c3d_id`.
- IfcOpenShell 0.9.0: schema IFC4 și regulile EXPRESS fără probleme; enumerările IfcSpace verificate; GlobalId unice
  (162/162 pe proiectul demo); geometria construită pentru toate elementele cu volum (42/42).
- Trei revizii independente (Opus): defectele găsite sunt corectate în commit-uri separate (între ele: argumentele IfcSpace
  inversate, pe care validatorul nu le prinde, și coliziuni de GlobalId); afirmația „importul DXF oglindește
  planul” a fost verificată cu un test dus-întors și respinsă.

## 4. Cum se repetă verificarea IFC

```powershell
python -m venv $env:TEMP\ifc-venv; & $env:TEMP\ifc-venv\Scripts\python.exe -m pip install ifcopenshell
# descarcă /api/projects/<id>/export.ifc, apoi:
# import ifcopenshell, ifcopenshell.validate; f = ifcopenshell.open('casa.ifc'); log = ifcopenshell.validate.json_logger()
# ifcopenshell.validate.validate(f, log); print(log.statements)   # listă goală = valid
```

## 5. Riscuri rămase

- Un raport „0 constatări” citit ca o conformitate: fraza de raport și panoul spun explicit că verificarea e asistivă.
- Performanța: verificarea rulează doar la cerere; cazul extrem (300 de trasee × 50 de puncte, 60 de pereți) durează ~1,6 s.
- Parserul DXF primește fișiere neîncredere: rulează în browser, cu limite de mărime, de entități și de coordonate.
