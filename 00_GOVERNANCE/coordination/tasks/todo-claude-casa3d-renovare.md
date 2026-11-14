# todo-claude-casa3d-renovare
STATUS: IN_REVIEW (PR draft)        UPDATED: 2026-11-10
TASK: Casa3D, cerința proprietarului: „tot ce se poate renova și adăuga în casă trebuie să existe, să poată fi înlocuit și
  cumpărat; uși, geamuri, utilitare, electrocasnice — nu modele standard”.
BRANCH: claude/casa3d-renovare-produse, worktree D:/w/reno (bază origin/main 0d97d13c3). Dev: launch config casa3d-reno, port 3221.
DONE:
  - Cercetare: 92 de produse reale, cu prețul citit pe pagina produsului pe 2026-11-10 (eMAG, Hornbach, Dedeman, IKEA,
    Termix). Notele brute sunt în D:/w/research_notes/casa3d-produse/*.json (în afara repo-ului).
  - Import: 87 de produse în data/catalog.v1.json (doar adăugări) și în data/materials.v1.json. Dovezile, inclusiv cele 37 de
    produse lăsate deoparte și motivul, sunt în data/catalog-evidence.2026-11-10.json. Nicio dimensiune ghicită.
  - Ferestre ca produs pe gol (core/windows.ts, Snapshot.windowUnits): validare pe server, potrivire cu golul (± 5 cm),
    avertismente, linie în deviz, desen 3D după produs, WindowPanel în inspector.
  - Aparataj: produs ales pe priză, întrerupător și termostat; seria aplicată pe toată casa într-o singură modificare
    (onPatchMany); priza dublă = 2 module + ramă dublă nepreţuită; rama simplă nepreţuită doar când pagina nu o include.
  - Electrocasnice noi (masinaSpalat, uscator, masinaVase, microunde, centrala; hote, plite, cuptoare noi): modele 3D după
    tip și culoare, simboluri în plan, spațiu în față, camere, puncte tehnice; microundele stau pe blat (0,9 m).
  - Uși de intrare (2026-11-10, a doua etapă): 14 uși metalice reale (Dedeman 4, Hornbach 8, eMAG 2; 5 lăsate deoparte, cu
    motiv) ca produse `door` cu specs.entrance; pe golul de intrare apar primele (doorOptions); avertisment invers
    door.entranceInside; mânerul inclus nu mai cere mâner; desen 3D: foaie metalică groasă, panouri sau geam, vizor.
    Două prețuri reverificate independent (Dedeman 1859 lei, Hornbach 2050 lei).
  - Desen 3D mai fidel: fereastra „fixă + deschidere” are primul canat fix (win.fixedSashes, din titlul paginii, 4 produse);
    cuptorul ales (cu tip) dă culoarea / inoxul ramei.
  - Verificare vizuală: randări Blender Cycles în D:/casa3d-render/renovare/out/.
  - Revizie: runda 1 PARTIAL → reparat (532ee851f); runda 2 PARTIAL → rama dublă reparată de sesiunea principală.
  - Teste: tests/renovation-products.test.ts (11); suita 143 de fișiere / 983 de teste; tsc curat; build compilat.
RESIDUALS ACCEPTATE (decizia sesiunii principale după runda 2):
  - cota blatului e fixă 0,9 m (motorul desenează .86 + blat); microundele pe pardoseală primesc acum avertismentul ON_FLOOR.
  - hota de insulă e desenată ca hotă de perete; hota-3/5/7 fără dimensiuni (60 cm).
  - ușile de 120 și 140 cm (două foi) sunt desenate cu o singură foaie; prețul de la eMAG Prodias e la „stoc epuizat”.
  - câmpul evidence al ușilor de intrare e rezumat de WebFetch, nu text integral copiat.
  - sanitizeWindows acceptă orice id scurt pentru produs (la fel ca la uși).
NEXT: CI pe PR; decizia de merge e a proprietarului.
IDEAS AFTER: uși de balcon pe goluri de ușă; ușă dublă desenată cu două foi; corpuri de iluminat direct pe punctele de lumină;
  parchet, gresie și vopsele din mai multe magazine; obiecte sanitare noi; reverificare lunară a prețurilor.
KEY FILES: app/core/{windows,technical,boq,catalog}.ts, app/components/{viewer3d-engine.js,Editor.tsx,TechPanel.tsx},
  app/lib/viewer-input.ts, app/data/{catalog.v1,materials.v1,catalog-evidence.2026-11-10}.json, app/tests/renovation-products.test.ts.
BLOCKERS: Altex, Flanco, Media Galaxy și Leroy Merlin au blocat sau au depășit timpul la citire: fără produse de la ei.
