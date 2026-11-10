# Plan de design — „mai profi” (cerut de proprietar, 2026-11-10)

Fiecare pas are verificarea lui scrisă înainte. „Gata” înseamnă test trecut și captură de ecran de pe mașina virtuală (1024 × 720),
privită critic.

| Pas | Ce | Gata când | Stare |
|---|---|---|---|
| 1 | Antetul încape la 1000 px: etichetele auxiliare se strâng în pictograme sub 1280 px, fără bară de derulare | test pe XAML (praguri, nume accesibile) și captură în cele 3 ediții: tot antetul vizibil, nicio linie de derulare | făcut, verificat pe VM |
| 2 | Stil unic pentru titlul și explicația paginii, cu linia de registru, pe toate paginile | test: nicio pagină cu titlu la altă mărime; capturi | făcut (teste) |
| 3 | Fără emoji: pictograme vectoriale în butoane, alerte, bara de stare, arborele de procese | test: niciun caracter pictografic în XAML și în textele view-modelelor | făcut (teste) |
| 4 | Fără culori din tema veche: view-modelele și paginile folosesc tokenii | test: nicio culoare literală în afara tokenilor (excepție: generatoarele de PDF) | făcut (teste) |
| 5 | Etichete ca propoziții (fără majuscule integrale), fără engleză („Live Triage”) | test pe XAML | făcut (teste) |
| 6 | Text tăiat: carduri de cifre, capete de coloană MITRE, bifele din Colectare date, titluri suprapuse | capturi la 1024 px fără text tăiat | parțial: MITRE, Colectare date, Panou general (verificat pe VM) |
| 7 | Meniu lateral: o singură derulare, focus vizibil cu inel plin, fără indicii care repetă numele | capturi și test | parțial: o singură derulare, inel de focus (verificat pe VM) |
| 8 | Tabele: antet, înălțime de rând, trunchiere cu indiciu, cifre tabulare | capturi | de făcut |
| 9 | Notificarea live și bara de stare: stil sobru, fără să acopere conținutul | capturi | de făcut |

Decizii luate fără întrebare (reversibile): tema „Registru” a devenit „Dosar” (cadru închis, hârtie rece, cobalt; vezi DESIGN.md); pragul de strângere a antetului este 1280 px; pictogramele vin din
fontul de sistem (nimic de instalat).
