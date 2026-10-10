# Casa mea 3D — Faza 1 (nucleul persistent)

Plan 2D editabil, amenajare automată cu produse reale, vizualizare 3D (machetă + tur), validare (ERROR/WARNING),
undo/redo, salvare automată și revizii, catalog în baza de date cu proveniență pentru fiecare preț.
Faza 2: cantități de materiale calculate din plan (BOQ), finisaje pe cameră, manoperă cu interval, buget complet cu TVA, rezervă, buget țintă și export CSV (vezi `PHASE2.md`).
Faza 3: Design Brief și 3 variante (Economic, Echilibrat, Premium) propuse de Claude sau de motorul de reguli, validate geometric și bugetar, cu previzualizare, respingere și aplicare cu revizie (vezi `PHASE3.md`). Pentru AI setează `ANTHROPIC_API_KEY`.
Faza 4 (mod testare): linkuri spre magazine doar prin redirectul `/go/`, contorizare anonimă, istoric de preț, linkuri de afiliere opționale cu informare automată (vezi `PHASE4.md`). Pentru administrare setează `ADMIN_TOKEN`.

## Rulare locală
```bash
npm install
npm run dev          # http://localhost:3000 — fără DATABASE_URL folosește PGlite în ./.data
npm test             # 24 de teste (paritate cu prototipul, reguli, migrare, persistență)
npm run typecheck && npm run build
```

## Publicare pe Vercel
1. Urcă folderul într-un repo GitHub.
2. Vercel → Add New → Project → importă repo-ul (framework detectat: Next.js).
3. În proiect: Storage → Create Database → Neon (Postgres). Vercel setează singur `DATABASE_URL`.
4. Redeploy. Tabelele și catalogul se creează automat la primul request.

Fără baza de date, aplicația pornește tot, dar afișează un banner: „Mod demonstrativ… proiectele se pierd”.

## Structură
- `core/` — motorul de amenajare extras din prototip (`layout.js`, neschimbat), validare, geometrie, undo/redo, operații pe proiect
- `lib/` — baza de date (Postgres sau PGlite), schema, repository, cookie de proprietar
- `app/` — pagini și API (`/api/projects`, `/api/projects/[id]`, `/api/projects/[id]/revisions`, `/api/catalog`, `/api/health`)
- `components/` — editorul 2D (SVG), scena 3D (portată din prototip), panourile
- `data/` — planul demo și catalogul migrat (68 variante, proveniență completă)
- `legacy/` — prototipul original, folosit de testele de paritate
- `CONSTITUTION.md`, `PHASE1.md` — reguli și raportul fazei
