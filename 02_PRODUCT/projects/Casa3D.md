---
id: "casa3d-project-20261001"
type: project
lifecycle: REVIEW
category: digital_twin
tags: [project, casa3d, digital_twin, interior_design, geometry, boq, budget, ai, monetization, romania]
created: 2026-10-01
updated: 2026-10-07
review_note: "AI-written continuity record; ACTIVE/verified requires owner attestation (attest()), not self-declaration."
provenance:
  source_type: user_provided_project_artifacts
  source_ref: "Casa3D Faza 0-3 ZIP artifacts + supplied Claude conversation"
  confidence: high
  verification: unverified
relations:
  - type: depends_on
    target: "[[AI_Memory_System]]"
---

# Casa3D

## Identitate

Casa3D urmareste fluxul:

`spatiu real -> Digital Twin -> design -> BOQ -> buget -> executie`

Principiul central: **AI-ul propune, Geometry Engine valideaza, utilizatorul aproba.** Digital Twin-ul este sursa de adevar geometric.

## Starea fazelor

| Faza | Continut | Stare |
|---|---|---|
| F0 | motor geometric, catalog, schema, migrari, teste | DONE |
| F1 | aplicatie persistenta, editor, 3D, API, DB, revizii, undo/redo | DONE |
| F2 | BOQ, materiale, cantitati, buget, provenance | DONE |
| F3 | Design Brief, 3 variante, validare, preview, reject/apply, AI optional | DONE |
| F4 | monetizare/link layer, oferte si retaileri | NEXT / TEST MODE |

F0-F3 sunt sustinute de artefactele furnizate. F4 nu este tratata ca implementata.

## Constitutia

1. Digital Twin-ul este sursa de adevar geometric; unitatea este metrul.
2. AI-ul propune; nu decide geometria.
3. Geometry Engine valideaza toate modificarile.
4. Utilizatorul aproba modificarile importante.
5. ERROR blocheaza; WARNING necesita confirmare.
6. AI-ul selecteaza numai produse/materiale existente in catalog prin ID.
7. AI-ul nu furnizeaza coordonate.
8. Nu se inventeaza preturi, dimensiuni, disponibilitate sau certificari; UNKNOWN este preferat.
9. Datele comerciale externe trebuie sa pastreze source, sourceUrl, verifiedAt, verificationType si confidence.
10. Scraping-ul automat este permis numai cu baza legala; sunt prevazute feed/API, fisiere furnizor, catalog verificat manual si platforme de afiliere.
11. Modelele 3D externe necesita drepturi/licenta verificabile.
12. Schimbarile motorului geometric necesita teste.
13. Functionalitatile simulate trebuie marcate experimental.
14. Romania este prima jurisdictie; verificarile de conformitate sunt asistive, nu certificari juridice.
15. Monetizarea initiala prevazuta: affiliate + Pro Designer + furnizori; AR/IFC/enterprise ulterior.

## F0 — Fundatie geometrica

Artefacte: `CONSTITUTION.md`, `AUDIT.md`, `core/layout.js`, `data/catalog.v1.json`, `data/floor.v1.json`, migrari si teste.

Dovezi furnizate:
- motor extras fara schimbare de comportament;
- 68 variante de catalog verificate;
- PLAN -> schema -> PLAN fara pierderi;
- 13/13 teste.

Auditul a identificat: acoperirea ferestrelor la mutare manuala, ignorarea circulatiei, ID-uri dependente de ordine, variant global per grup, efect secundar in `placeDining`, camere dreptunghiulare/pereti neconectati, zona fixa de usa, lipsa undo/redo si date comerciale incomplete/aproximative.

## F1 — Aplicatia persistenta

Include Next.js, editor 2D, 3D, API, DB/PGlite/Postgres, revision history, undo/redo, validare si provenance catalog.

Dovezi furnizate: typecheck, 24/24 teste, production build si browser test.

Nu erau incluse: auth completa, colaborare, buget complet, AI, camere L-shape, legare automata perete-camera si joystick mobil.

## F2 — BOQ si buget

Include cantitati derivate din geometrie, pierderi si pachete intregi, pereti/tavane, vopsea, gresie/faianta, plinte, adeziv, iluminat, manopera, categorii de buget, TVA, rezerva, buget tinta, over-budget si provenance comercial.

Dovezi furnizate: typecheck/build/browser, 39/39 teste, 16 materiale si 6 rate de manopera verificate in artefactele F2 la 2026-10-01.

Limite: preturile nu sunt live; manopera variaza; unele categorii de lucrari si costuri raman neacoperite sau UNKNOWN.

## F3 — Design AI asistat

### Brief

Stil, ocupanti, copii, animale, accesibilitate, prioritati, buget, magazine acceptate, culori preferate si excluderi.

### Variante

Economic, Echilibrat, Premium.

Fiecare afiseaza costul total/delta, paleta, modificarile, diferentele de pret/dimensiune, motivele si problemele de validare.

Preview-ul nu salveaza. Reject lasa proiectul neschimbat. Apply revalideaza proiectul curent, aplica varianta si creeaza revision.

### Contract AI

AI-ul primeste ID-uri de catalog si atribute. Trebuie sa foloseasca numai ID-uri furnizate, sa nu inventeze produse/preturi/dimensiuni/disponibilitate/certificari sau coordonate si sa returneze JSON conform schemei.

Pozitiile provin din Geometry Engine.

Serverul revalideaza: schema, catalog, magazine, geometrie, incadrare, usi, ferestre, circulatie si buget.

Dovezi furnizate: 52/52 teste; produs inventat -> ERROR; mobilier oversized -> DOES_NOT_FIT; over-budget -> WARNING; JSON invalid -> fallback; lipsa API key -> rules engine; reject/apply si revision verificate.

Integrarea Anthropic reala a fost configurata, dar testarea reala a fost simulata fara `ANTHROPIC_API_KEY`. Default model mentionat in cod: `claude-sonnet-5-5`. Nu se considera runtime-verified.

## F4 — Monetizare

Directia discutata: perioada de test fara conturi de afiliere si fara abonamente/plati.

Layer propus:
- `retailers`;
- `offers`;
- `offer_links`;
- redirect `/go/:offerId`;
- pret manual cu data verificarii;
- click tracking fara IP brut/cookies;
- fara copierea imaginilor retailerilor fara drept;
- afilierea ulterioara poate introduce `rel="sponsored"` si disclosure.

Aceasta este decizie de proiectare/test mode, nu dovada implementarii F4 si nici dovada curenta a programelor de afiliere ale retailerilor.

## Decizii canonice

**D1 — AI nu controleaza geometria.** AI-ul propune semantic; Geometry Engine calculeaza pozitiile si valideaza.

**D2 — Apply este revalidat pe proiectul curent.** Preview/Reject nu modifica proiectul; Apply actualizeaza Digital Twin-ul si creeaza revision.

**D3 — Catalogul este autoritatea comerciala.** Numai ID-uri existente; fara preturi/dimensiuni inventate.

**D4 — ERROR vs WARNING.** ERROR blocheaza; WARNING cere confirmare.

**D5 — Oferta este decuplata de UI.** Un layer intermediar permite afilierea ulterioara fara rescrierea interfetei.

## Roadmap

Urmatorul pas: F4 test mode cu retaileri/oferte, provenance pret, redirect tracking si teste.

Dupa validare comerciala: afiliere reala, Pro Designer, integrare furnizori.

Mai tarziu: AR, IFC, enterprise si multi-country.

Roadmap-ul nu reprezinta capabilitati implementate.


## Matrice de evidenta

| Nivel | Sursa | Ce sustine |
|---|---|---|
| F0 | `CONSTITUTION.md`, `AUDIT.md`, `core/layout.js`, `data/catalog.v1.json`, `data/floor.v1.json`, migrari/teste | reguli constitutionale, motor geometric, schema, catalog si rezultate F0 |
| F1 | aplicatia Next.js, editor 2D/3D, API, DB/PGlite/Postgres, revision history, undo/redo, validare, teste | persistenta, editor, revizii si validare |
| F2 | `core/boq.ts`, `core/rules.boq.ts`, `BudgetPanel.tsx`, catalog materiale | BOQ, cantitati, costuri, manopera si provenance |
| F3 | `core/brief`, `core/proposal`, `core/proposer-rules`, `lib/ai.ts`, DesignPanel si teste | Design Brief, propuneri, contract AI, validare si Apply/Reject |
| F4 | discutia de proiect furnizata de utilizator | design/test mode si roadmap; nu dovada de implementare |

## Gaps cunoscute si limite de continuitate

- F0 a avut probleme initiale privind ferestre/circulatie, ID-uri dependente de ordine, variante globale per grup, efecte secundare de placement, camere dreptunghiulare/pereti neconectati, zona fixa pentru usa si lipsa undo/redo. F1 documenteaza ca problemele 1-5 si 8 au fost remediate; camerele L-shape, legarea automata perete-camera si joystick-ul mobil au ramas in afara fazei.
- F1 nu trebuie interpretat ca avand auth completa sau colaborare multi-user.
- F2 nu reprezinta un feed comercial live si nu acopera toate categoriile de lucrari/materiale; valorile necunoscute trebuie pastrate ca UNKNOWN.
- F3 are integrarea AI reala configurata, dar fara `ANTHROPIC_API_KEY` executia modelului nu a fost runtime-verificata; fallback-ul rules engine si testele de siguranta sunt cele verificate.
- F4 ramane test mode pana cand exista implementare si dovezi pentru redirect, tracking, provenance si fluxurile comerciale.
- Orice informatie comerciala sau de afiliere din conversatii trebuie revalidata inainte de a deveni fapt curent.

## Gate-uri de faza

- **F0 gate:** geometria, schema si catalogul trebuie sa ramana deterministe si testate.
- **F1 gate:** modificarile persistente trebuie sa treaca prin validare si revision history.
- **F2 gate:** cantitatile si costurile trebuie derivate din Digital Twin si sa pastreze provenance/UNKNOWN unde lipsesc date.
- **F3 gate:** AI poate propune doar semantic; serverul revalideaza proiectul curent inainte de Apply.
- **F4 gate:** monetizarea nu devine productie doar din documentatie; fiecare integrare comerciala necesita dovada tehnica si provenance.

## Criteriu de actualizare a memoriei

La fiecare faza noua, memoria Casa3D trebuie actualizata numai dupa ce exista artefacte identificabile si dovezi de verificare. Conversatia poate actualiza intentia si roadmap-ul, dar nu poate promova singura o capabilitate in categoria "implementat".


## Implementare continua — Digital Twin / AI Designer

Dupa F3, implementarea Casa3D a fost continuata in artefactul de lucru al proiectului si este urmarita de acest PR pentru continuitate.

### Core Digital Twin

Implementat:
- model canonic Digital Twin v1.0;
- Geometry Engine deterministic;
- revalidare la persistenta si revision;
- ViewerState comun pentru 2D/3D;
- share read-only pe revizie;
- stale protection pentru propuneri.

### AI Design DSL 1.1

AI-ul lucreaza declarativ, fara coordonate:
- ADD / REMOVE / REPLACE / MOVE;
- constrangeri semantice: near, againstWall, alignedWith, keepClear, orientation;
- Solver deterministic pentru candidate geometry;
- Geometry Engine ramane autoritatea finala.

### AI Design Approval

Propunerea AI este separata de proiectul curent:
- preview-ul nu modifica proiectul;
- fiecare propunere are baza/fingerprint;
- daca Digital Twin-ul s-a schimbat, propunerea devine stale si nu poate fi aplicata;
- Accept creeaza revision numai dupa revalidare;
- ERROR blocheaza;
- WARNING cere confirmare.

### Alternative de design

Sunt suportate pana la 3 alternative DSL pentru acelasi brief. Comparatorul raporteaza masuratori si probleme, fara sa aleaga automat o varianta castigatoare.

### BOQ-aware evaluation

Fiecare alternativa poate fi evaluata prin motorul oficial de buget:
- cantitati;
- subtotal/total;
- costuri cunoscute;
- UNKNOWN;
- furnizor;
- provenance;
- diferenta fata de bugetul tinta.

Nu se inventeaza preturi sau date comerciale.

### Dovezi si limite

Au fost executate compilari izolate si smoke tests pentru Digital Twin, ViewerState, DSL, semantic Solver, approval si BOQ search. Testele Vitest complete si `next build` nu sunt declarate ca trecute deoarece mediul de lucru nu are o instalare npm completa, iar incercarile de instalare au expirat.

Ledger-ul de implementare si hash-urile artefactelor sunt pastrate in:
`03_IMPLEMENTATION/projects/Casa3D/CORE_IMPLEMENTATION_v8.md`

Aceste informatii actualizeaza starea tehnica a proiectului; nu transforma automat F4 sau capabilitatile comerciale in productie.

## Regula de continuitate pentru agenti

Ordinea autoritatii este:
1. artefactele implementate si testate ale fazei curente;
2. constitutia si regulile de validare;
3. testele fazei;
4. documentatia fazei;
5. conversatiile, pentru intentie si roadmap necodificate.

Un agent nu transforma o propunere de conversatie in functionalitate implementata fara dovada executabila.

## Provenienta

Memoria a fost reconstruita din `casa3d-faza0.zip`, `casa3d-faza1.zip`, `casa3d-faza2.zip`, `casa3d-faza3.zip` si conversatia Claude furnizata de utilizator.

Distinctiile implementat/verificat, proiectat/discutat, simulat si roadmap sunt intentionate pentru a preveni inflatia de capabilitati.
