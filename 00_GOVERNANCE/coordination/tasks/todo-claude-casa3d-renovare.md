# todo-claude-casa3d-renovare
STATUS: IN_REVIEW (PR draft)        UPDATED: 2026-11-17
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
  - Hota de insulă (2026-11-17): fără suspendate, baldachin centrat pe plită și coș până la tavanul camerei (sticla stă pe
    ramă); cu suspendate, avertismentul kit.islandHoodUpper în panoul bucătăriei. Verificat în aplicație (tur Bucătărie, hota-7).
  - Advising Fable (2026-11-17, read-only) aplicat: hota de perete cu sticlă nu mai plutește (sticla înclinată pe ramă, coșul din
    ea); coșul hotei de perete urcă până la tavanul camerei (ceil trimis și pentru hood_chimney); coș omis la tavan sub baldachin;
    lățimea din titlu când pagina n-are dimensiuni (widthInName: hota-7 „90cm”, hota-3/5 „60 cm”); test extins (kitchen absent,
    upper closed, cameră fără bucătărie, insula + upper none în viewerInput). Capturi: D:/casa3d-render/renovare/verificare-2026-11-17/.
  - Ușa de 140 cm: titlul paginii Hornbach e chiar „Still 627 1 2 geamuri” (fără „1/2” sau „canat”); nu setez doorTypes.
  - Runda 2 (2026-11-17), alese de Fable (read-only), cercetare web + implementare Opus:
    - fix: fixedSashes ajunge în g.fer (viewer-input) — fereastra „fixă + deschidere” are canatul fix și în 3D.
    - U1 ușa de balcon pe gol de ușă: DoorOpening.exterior, doorOptions(mc, entrance, exterior) cu ușile de balcon (window
      kind balcony_door) primele doar pe perete exterior, avertisment door.balconyInside, fără door.noHandle, deviz „Ușă de balcon”
      + montaj (glaf, pervaz, prag) fără preț, desen 3D: cercevea PVC în culoarea produsului + geam clar.
    - U2 produse pe punctele tehnice: data → data_socket (prize RJ45/TV din seriile existente), vent_exhaust → bath_fan; serie pe
      toată casa; deviz: ventilator fără ramă + montaj fără preț; corp 3D doar cu cele 3 cote de pe pagină (specs.bodyCm).
      17 produse (Dedeman 12, Hornbach 5) în materials.v1.json, dovezi în data/catalog-evidence.2026-11-17.json, note brute în
      D:/w/research_notes/casa3d-produse/ventilatie-date.json; 3 prețuri reverificate (Vents X1, Sedna RJ45, Gewiss GW12423).
    - Capturi 11-16 în D:/casa3d-render/renovare/verificare-2026-11-17/. Suita 143/986, tsc curat.
  - Next.js: PR separat #391 (16.4.3 → 16.4.6, GHSA-mfr8 + GHSA-vc7v), build verde local.
  - Runda 3 (2026-11-17): audit extern ChatGPT confruntat de Fable cu codul (multe propuneri existau deja: I5 tab. 6.1.1.1,
    istoric prețuri, STALE 14 zile). Implementat: cada intră în BATH_ZONE; ventilator / punct de lumină sub 0,60 m de duș sau
    cadă → notă TIP cu IP-ul declarat (fără IP minim: SR HD 60364-7-701 nu e disponibil integral); VENT_BATH în 3 stări după
    ventilatorul ales (fără / debit nedeclarat / sub cerință; debitul liber e limită de sus); fereastra: diferență cu semn —
    mai mare decât golul cu > 1 cm nu mai e „apropiată” și primește win.tooBig. Capturi 17-18. Suita 143/988, tsc curat.
  - Verificat pe surse (2026-11-17): Dedeman T&C — „Preluarea conținutului … în alte scopuri decât pentru utilizarea în interes
    personal” e sancționată; Hornbach T&C 21.01.2026 — acces „limitat, în interes personal”, exploatarea comercială cere
    „acordul scris al proprietarului”; ANAF: TVA 21% / 11% de la 1 august 2025; Ordinul 959/2023 → DDR ≤ 30 mA (pct. 4.1.5.2.1).
  - Decizia proprietarului (2026-11-17): fără acorduri cu magazinele — doar link + preț observat cu data. Implementat: sursa
    (magazin, URL, data citirii) trece deviz → profit → proformă (core/commercial.ts PriceSource); proforma o arată sub fiecare
    produs + nota pf.observedNote („nu sunt oferte ale magazinelor…”); CSV-ul de facturare rămâne fără linkuri. Verificat: catalogul
    nu conține imagini/descrieri de la magazine; public/ are doar CC0 Poly Haven. Test tests/price-source.test.ts; captura 19.
NEXT (sesiunea următoare): proforma emisă înghețată (copie aditivă la emitere, data prețului pe linie; SourceLine pierde
  verifiedAt/sourceUrl în core/commercial.ts) — atinge DB, cere vault-reviewer. Înainte: decizia proprietarului
  „proforma = estimare sau ofertă” și ce face cu termenii Dedeman/Hornbach (acord scris / afiliere / doar link + preț observat).
  - Teste: tests/renovation-products.test.ts (12); suita 143 de fișiere / 984 de teste; tsc curat; build compilat.
RESIDUALS ACCEPTATE (decizia sesiunii principale după runda 2):
  - cota blatului e fixă 0,9 m (motorul desenează .86 + blat); microundele pe pardoseală primesc acum avertismentul ON_FLOOR.
  - ramura „hotă de insulă fără sticlă” nu are produs în catalog: neverificată vizual.
  - ventilatoare: 7 din 8 nu declară culoarea → desen în alb-gri neutru (#f2f2f0); nu există ventilator negru/inox cu preț citit
    (eMAG și Hornbach blochează citirea automată); Panasonic Karre Plus fără prize de date cu preț pe pagină.
  - nu există avertisment de IP pentru ventilatorul pus deasupra dușului (zonele din băi nu sunt modelate).
  - revizia Fable (U1+U2) aplicată: ușa de balcon fără mâner/sistem în deviz și în panou, avertisment door.balconyType; stilul nu
    mai suprascrie ușa de balcon (core/styles.ts). Reziduuri: door.size compară doar lățimea (±10 cm); ușile de balcon pot fi
    puse și pe gol de fereastră (două căi); seria de date pe toată casa pune același produs și pe punctele „TV / internet”
    (socketKind nefolosit încă); adâncimea ventilatorului de pe pagină include probabil ștuțul (desen tăiat la 8 cm).
  - `npm run build` pe acest branch pică până la merge-ul #391 (Next.js); testele și tsc sunt verzi.
  - ușile de 120 și 140 cm: paginile Hornbach (recitite 2026-11-17) nu spun că au două foi și nici cum sunt împărțite (doar
    „trei balamale”, o direcție de deschidere), deci rămân desenate cu o singură foaie; prețul de la eMAG Prodias e la „stoc epuizat”.
  - câmpul evidence al ușilor de intrare e rezumat de WebFetch, nu text integral copiat.
  - sanitizeWindows acceptă orice id scurt pentru produs (la fel ca la uși).
NEXT: CI pe PR; decizia de merge e a proprietarului.
  `npm run build` (2026-11-17): compilează toate paginile, apoi Next.js 16.4.3 iese cu SecurityFatalError (vulnerabilitate
  cunoscută în registry) — dependență, nu codul PR-ului; cere upgrade Next într-o sarcină separată.
IDEAS AFTER: uși de balcon pe goluri de ușă; ușă de intrare dublă doar cu un produs care își declară foile; corpuri de iluminat direct pe punctele de lumină;
  parchet, gresie și vopsele din mai multe magazine; obiecte sanitare noi; reverificare lunară a prețurilor.
KEY FILES: app/core/{windows,technical,boq,catalog}.ts, app/components/{viewer3d-engine.js,Editor.tsx,TechPanel.tsx},
  app/lib/viewer-input.ts, app/data/{catalog.v1,materials.v1,catalog-evidence.2026-11-10}.json, app/tests/renovation-products.test.ts.
BLOCKERS: Altex, Flanco, Media Galaxy și Leroy Merlin au blocat sau au depășit timpul la citire: fără produse de la ei.
