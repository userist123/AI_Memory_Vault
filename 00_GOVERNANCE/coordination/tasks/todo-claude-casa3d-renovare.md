# todo-claude-casa3d-renovare
STATUS: IN_PROGRESS        UPDATED: 2026-11-10
TASK: Casa3D, cerința proprietarului: „tot ce se poate renova și adăuga în casă trebuie să existe, să poată fi înlocuit și
  cumpărat; uși, geamuri, utilitare, electrocasnice — nu modele standard”.
BRANCH: claude/casa3d-renovare-produse, worktree D:/w/reno (bază origin/main 0d97d13c3). Dev: launch config casa3d-reno, port 3221.
DONE:
  - Cercetare: 92 de produse reale, cu prețul citit pe pagina produsului pe 2026-11-10 (eMAG, Hornbach, Dedeman, IKEA,
    Termix). Notele brute sunt în D:/w/research_notes/casa3d-produse/*.json (în afara repo-ului).
  - Import: 87 de produse în data/catalog.v1.json (doar adăugări) și în data/materials.v1.json. Dovezile, inclusiv cele 37 de
    produse lăsate deoparte și motivul, sunt în data/catalog-evidence.2026-11-10.json. Nicio dimensiune ghicită: piesele cu
    dimensiuni lipsă sau contestate pe pagină nu intră în plan. Numele au diacritice; sursa e linkul.
  - Ferestre ca produs pe gol (core/windows.ts, Snapshot.windowUnits): validare pe server, potrivire cu golul (± 5 cm),
    avertismente, linie în deviz, desen 3D după produs (culoarea tocului, canatele, fixă / mobilă, aluminiu sau PVC),
    WindowPanel în inspector.
  - Aparataj: produs ales pe priză, întrerupător și termostat (TECH_PRODUCT_CATEGORY), seria aplicată pe toată casa
    (TechPanel SeriesBulk), linie în deviz.
  - Electrocasnice noi (masinaSpalat, uscator, masinaVase, microunde, centrala; hote, plite și cuptoare noi): modele 3D după
    tipul și culoarea produsului (washer, dishwasher, microwave, gasboiler; hota și plita pe gaz în piesa de bucătărie, doar
    pentru produsele cu tip, ca proiectele vechi să rămână identice), simboluri în plan, spațiu în față, camere, puncte tehnice.
  - Teste: tests/renovation-products.test.ts (6); suita 142 de fișiere / 978 de teste.
NEXT: verificare vizuală (randare Blender a scenei exportate), revizie (max. 2 runde), commit + push + PR draft.
IDEAS AFTER: uși de intrare (metal) și uși de balcon pe goluri de ușă; corpuri de iluminat ca produse pe punctele de lumină;
  parchet, gresie și vopsele din mai multe magazine; obiecte sanitare noi; un import automat al prețurilor (reverificare lunară).
KEY FILES: app/core/{windows,technical,boq,catalog}.ts, app/components/{viewer3d-engine.js,Editor.tsx,TechPanel.tsx},
  app/lib/viewer-input.ts, app/data/{catalog.v1,materials.v1,catalog-evidence.2026-11-10}.json, app/tests/renovation-products.test.ts.
BLOCKERS: Altex, Flanco, Media Galaxy și Leroy Merlin au blocat sau au depășit timpul la citire: fără produse de la ei.
