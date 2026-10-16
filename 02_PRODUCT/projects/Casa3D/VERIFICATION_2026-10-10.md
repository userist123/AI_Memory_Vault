---
id: "28890ae9-095d-4d32-a374-54cd7996d9aa"
type: resource
lifecycle: REVIEW
category: digital_twin
tags: [casa3d, verification-ledger, project-ledger]
created: "2026-10-10"
updated: "2026-10-10"
provenance:
  source_type: user
  source_ref: "casa3d-faza0.zip, casa3d-faza2.zip, casa3d-faza4.zip supplied by the owner on 2026-10-10 (not stored in this repository)"
  source_date: "2026-10-10"
  redaction: not_applicable
  provenance_status: complete
confidence: high
verification: unverified
relations:
  - type: related_to
    target_id: "4e17d26a-74a6-408a-b0f4-069dfd20ac95"
  - type: related_to
    target_id: "d4785be1-7a70-4e45-b7ba-214e460501d4"
---

# Casa3D — verificare locala a artefactelor de faza (2026-10-10)

Rulata de Claude Code (Fable 5.1) pe masina proprietarului, Windows 11, Node v24.13.0, npm 11.12.1.
Nota ramane REVIEW/unverified: proprietarul atesteaza (`attest()`).

## Artefacte primite

| Fisier | SHA-256 | Continut |
|---|---|---|
| `casa3d-faza0.zip` | `1e8971e1dc0fe25f6eb95e96fd7cda07cc367a94cfc74f06023581b315185f10` | 15 fisiere: motor geometric `core/layout.js`, catalog, floor, migrari, 1 fisier de test |
| `casa3d-faza2.zip` | `8d5b6eeb89cd81f5899a1527b59f5c47d861f1001cb2790b98f6d9d3f33e6372` | 57 fisiere: aplicatia Next.js pana la BOQ (nerulata; faza4 o include) |
| `casa3d-faza4.zip` | `334d195a59a86aa3a28ebdbf402e41f013cb82a3ad9ba332f2cf6e4c256054d2` | 81 fisiere: aplicatia cumulativa F1-F4 (`PHASE1..4.md`, `core/outbound.ts`, rute `/go/*`, `/api/admin/*`) |
| `0001-casa3d-f4.patch` | - | patch pe `Casa3D.md` din 2026-10-02 (baza blob `e5462c97`, commit `cd0aa2fdf`); nu a fost aplicat niciodata in Vault |
| `Casa3D.md` (Downloads) | - | varianta notei din 2026-10-02, auto-declarata ACTIVE/verified; NU se importa ca atare |

Lipsesc: `casa3d-faza1.zip`, `casa3d-faza3.zip` si arhiva v8 (`7bb34bf3...9628bc`). `faza4` contine F1-F3, deci
F1-F3 sunt acoperite; v8 (Digital Twin v1.0, DSL 1.1, approval, share) ramane fara sursa.

Scanare Defender (MpCmdRun, ScanType 3) pe cele trei arhive si pe directorul extras: niciun rezultat.
Lockfile: 133 pachete, toate din `registry.npmjs.org`; singurul `hasInstallScript` este `fsevents` (macOS).
Instalare cu `npm ci --ignore-scripts`.

## Rezultate

Spatiu de lucru: `D:\w\casa3d\` (C: era plin 100% la momentul rularii; o prima instalare pe C: a esuat cu ENOSPC si a fost stearsa).

| Verificare | Arhiva | Rezultat | Nivel |
|---|---|---|---|
| `node --test tests/layout.test.mjs` | faza0 | 13/13 pass | TEST_VERIFIED |
| `npx vitest run` | faza4 | 6 fisiere, 64/64 pass, 3.8 s | TEST_VERIFIED |
| `npx tsc --noEmit` | faza4 | exit 0 dupa patch-ul local de mai jos | TEST_VERIFIED (cu patch) |
| `npx next build` | faza4 | exit 0, 22 rute, 7 pagini statice, dupa acelasi patch | TEST_VERIFIED (cu patch) |
| apel real al modelului (`ANTHROPIC_API_KEY`) | faza4 | nerulat; cheia nu e setata | CLAIMED_ONLY |
| verificare pe server/browser din `PHASE4.md` | faza4 | nerulata | CLAIMED_ONLY |

Testele acopera: outbound (afiliere/UTM/open-redirect/anonimizare/ADMIN_TOKEN), BOQ si buget, core
(validare ERROR/WARNING, undo/redo, lacunele 1-5 reparate), design (brief, 3 variante, produs inventat ->
ERROR, DOES_NOT_FIT, over-budget -> WARNING, respingere/aprobare/revizie), paritate cu prototipul (68 variante),
repo (PGlite, revizii, proprietar).

## Defect de portabilitate gasit

`components/Viewer3D.tsx` importa dinamic `./viewer3d.js`, iar `components/viewer3d.js` este un fisier
separat. Pe un sistem de fisiere insensibil la majuscule (Windows NTFS implicit, macOS implicit) TypeScript
rezolva `./viewer3d.js` la `Viewer3D.tsx` si raporteaza TS1149; `tsc` si `next build` esueaza. Pe Linux
(sandbox-ul in care au fost produse fazele) nu apare. `fsutil setCaseSensitiveInfo` a fost refuzat (acces).

Patch local aplicat doar pentru verificare, in `D:\w\casa3d\faza4\app` (git local, commit "faza4 as extracted"
inainte de patch): `components/viewer3d.js` -> `components/viewer3d-engine.js` si importul corespunzator in
`Viewer3D.tsx`. Nicio alta modificare. Recomandare pentru proiect: aceeasi redenumire in sursa canonica.

## Ce ramane deschis

1. Sursa v8 (Digital Twin, DSL, approval, share, boq-search, catalog-feed, `verify-f4/f5.mjs`): negasita.
2. Apel real al modelului: necesita `ANTHROPIC_API_KEY` setata de proprietar.
3. Verificarea server/browser din `PHASE4.md` (302 cu UTM, 401 fara token etc.) nu a fost reprodusa.
4. `casa3d-faza1.zip`, `casa3d-faza3.zip`: lipsa, acoperite indirect prin faza4.
