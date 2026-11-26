# Rulări de analiză imuabile și diferențe (master plan 6.16)

**Unde se află:**
- Cod: `LogAnalyzer.Dfir.Core/Case/AnalysisRuns.cs`, apelat la sfârșitul `InvestigationPipeline.RunCore`, înainte de ancora capetelor de lanț.
- Interfață: tab-ul „Rulări și diferențe” din „Investigație completă” și butonul „Reanalizează cazul deschis”.
- Teste: `AnalysisRunsTests` (16, dintre care unul cap-coadă cu pipeline-ul real) și `RunsViewModelTests`.

## Ce se înregistrează

După fiecare analiză se scriu fișierele de mai jos în `Analysis/Runs/RUN-aaaallzz-hhmmss/`. Se scriu o singură dată (`CreateNew`), doar pentru citire, și sunt înregistrate în lanțul de custodie. Reverificarea la deschidere le prinde dacă sunt modificate.

- `run_manifest.json` cuprinde:
  - **unelte:** versiunea aplicației, contractul regulilor și al constatărilor, parserele (nume, versiune, maturitate, înregistrări), regulile de detecție (id, versiune, SHA-256);
  - **intrări:** probele, cu amprenta din indexul probelor;
  - **configurație:** fusul orar, perioada scopului, setările rulării (profilul de proceduri, registrul de medii, registrul de utilizatori, seturile de amprente, profilul de colectare);
  - **ieșiri:** fișierele principale din `Analysis/`, cu SHA-256;
  - **constatări:** instantaneul constatărilor.
- `diff_previous.json`: diferența față de ultima rulare intactă.

Amprenta manifestului se trece în auditul cazului (`analysis.run_recorded`). Lista rulărilor compară fiecare manifest cu amprenta din audit. Se iau în calcul doar liniile din partea verificată a lanțului, în ordinea din fișier: o linie falsificată inserată nu este crezută. Rularea apare ca:
- „intact”;
- „MODIFICAT după înregistrare”;
- „neînregistrat în auditul cazului”;
- „folderul rulării LIPSEȘTE”.

## Cum se compară

- **Cheia constatării:** o constatare se recunoaște după regulă și după locatorii probelor pe care se sprijină, nu după numărul F-nnnn, care se poate schimba la o rulare nouă. Constatările cu aceeași regulă și aceleași probe (de exemplu una pe utilizator) primesc `#1`, `#2`… în ordinea conținutului. Niciuna nu o poate ascunde pe alta.
- **Câmpuri comparate:** severitate, încredere, clasificare, titlu, prima și ultima apariție, utilizator, stație, numărul probelor, descriere (prin amprentă).
- **Cauza probabilă:**
  - probe diferite (amprentă, probă nouă sau lipsă, scop, fus orar);
  - unelte sau setări de analiză diferite;
  - **NEEXPLICATĂ**: aceleași probe și aceleași unelte, dar alt rezultat. Diferența se marchează de verificat.
- **Setări de colectare:** profilul de colectare și colectarea se înregistrează, dar nu se compară. Ele descriu cum au intrat probele, nu cum au fost analizate.

## Reanaliza

- **Ce face:** „Reanalizează cazul deschis” rulează din nou analiza pe probele deja din caz, fără colectare. Folosește registrul de medii semnat din caz, ca rularea inițială.
- **Ieșirile:** ultimele ieșiri din `Analysis/` se rescriu, iar custodia păstrează fiecare versiune. Rulările vechi rămân neatinse. După reanaliză cazul se reverifică.
- **Refuzuri:** reanaliza este refuzată pentru un caz doar pentru citire și pentru un caz închis în fluxul de lucru. Un caz închis se redeschide întâi.

## Verificat în laborator (2026-11-08)

- **Insula, LAB-WS02:** două reanalize pe 159 de probe și 20.895 de evenimente. Ambele rulări sunt intacte, iar diferența este „Aceleași constatări (83)”, deci analiza este deterministă.
- **Arhipelag, apoi Continent, LAB-WS01:** aceeași probă a fost analizată de două ediții (188 de probe, 50.943 de evenimente). Diferența este „Aceleași constatări (74)”, fără schimbări de unelte, iar fișierele rulărilor sunt doar pentru citire.

## Limite

- Auditul cazului nu este sigilat cu cheie. Cine rescrie împreună manifestul, auditul și custodia este prins numai de o ancoră a capetelor de lanț păstrată în afara cazului. Ancora din `schema_manifest` acoperă acum și înregistrarea rulării.
- Deciziile din fluxul de lucru (6.1) sunt legate de numărul F-nnnn. După o reanaliză care renumerotează constatările, ele trebuie revăzute. Diferența rulărilor arată perechile vechi→nou.
- Manifestul conține contul operatorului și numele originale ale probelor, la fel ca auditul și custodia. La export trebuie mascate.
