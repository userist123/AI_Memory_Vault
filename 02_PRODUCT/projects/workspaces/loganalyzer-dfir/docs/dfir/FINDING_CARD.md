# Fișa constatării, glosarul, etichetele de stare și baza de accesibilitate (WP6a)

Închide R18.1-R18.12, U4, U5, U6, U7, U8 și baza U18 pentru pagina „Investigație completă”, Acasă și controalele noi din `CONTRACT_AUDIT_STAGE1.md`.
Stratul de resurse RO/EN (U17) este WP6b și nu face parte din acest pachet. Teste: `Wp6aGlossaryTests`, `Wp6aStateLabelTests`, `Wp6aFindingCardTests`, `Wp6aXamlTests` (Dfir.Tests, rulează și pe Linux),
`FindingCardViewModelTests` (App.Tests, doar pe Windows).

## 1. Glosar (R18.1-R18.12)
`LogAnalyzer.Dfir.Core/Language/Glossary.cs`: un singur tabel (termen tehnic, expresie română, expresie engleză, explicație scurtă în română). Cei 12 termeni ai R18 sunt minimul:
EVTX → „Jurnale Windows”, Prefetch → „Istoricul pornirii programelor”, BAM → „Activitatea programelor”, Amcache → „Istoricul aplicațiilor”, Evidence Graph → „Legături între evenimente”,
Provenance → „De unde provine informația”, Chain of Custody → „Istoricul probei”, IOC → „Indicator suspect”, Sigma → „Regulă de detecție de securitate”, YARA → „Regulă de detecție pe fișiere/conținut”,
Correlation → „Evenimente legate”, Verification → „Verificarea probelor”. Un termen care nu este în tabel se afișează neschimbat (glosarul nu inventează traduceri).
Vederile obișnuite arată expresia română; numele tehnic rămâne în tooltip (`Glossary.Tooltip`) și în vederile avansate (`Glossary.Display(term, advanced: true)` → „EVTX (Jurnale Windows)”).
În XAML: `{views:Term evtx}` (expresia), `{views:Term evtx, Tooltip=True}` (numele tehnic + explicația), `{views:Term evtx, Advanced=True}`. Aplicat în bara laterală (Jurnale Windows, Istoricul probei),
pe pagina de investigație (tab „Legături între evenimente”), în matricea de acoperire de pe Acasă (`CoverageRow.HumanFamily` / `FamilyTooltip`) și în fișa constatării („De unde provine informația”, „Verificarea probelor”).
Engleza din glosar este folosită de WP6b; până atunci interfața rămâne în română.

## 2. Etichetele de stare (U7)
Un singur tabel: `StateLabels.RomanianTable` (Observat, Corelat, Susținut de dovezi, Verificat, Deducție, Nedemonstrat, Contrazis, Respins, Necunoscut, Neevaluat), plus `StateLabels.Meaning` (o propoziție
despre ce înseamnă și ce nu înseamnă starea). O clasificare se afișează prin `StateLabels.ForClassification` (aceeași mapare ca starea constatării, `FindingContract.StatusFor`). Au fost eliminate
`GraphExplorer.Wording` (cuvinte englezești pentru 4 din 10 stări) și folosirea lui în convertor; marginile grafului arată acum etichetele românești. Linia „Verificare:” din pagină și din PDF folosește
`VerificationReport.LineRomanian` („3 Verificat (VERIFIED), 2 Nedemonstrat (UNPROVEN)”); `Banner` rămâne forma de date. Observație: `BenignKnown` se mapează pe „Observat” (cum făcea deja `FindingContract`), deci
graful nu mai distinge „cunoscut benign” de „observat” în coloana de clasificare.
Auditul anterior citează un convertor în `ProcessContainmentView.xaml.cs`; în codul actual acolo nu există niciun convertor (rămăseseră doar `using`-uri), deci nu era nimic de eliminat acolo.

## 3. Fișa constatării (U4) și „De ce?” (U5)
`LogAnalyzer.Dfir.Core/Presentation/FindingCardModel.cs` construiește fișa din `Finding` și din datele cazului (`EvidenceContext`: probe, cronologie, parsări, goluri); controlul WPF
`Views/Controls/FindingCard.xaml` (DataContext `FindingCardViewModel`) doar o afișează: titlu, rezumat uman, severitate (cuvânt + formă de pictogramă prin `SeverityBadge`, nu doar culoare),
stare, stare de verificare, butoanele **De ce?**, **Arată dovezile**, **Verifică**, **Ce trebuie să fac** (taste de acces D, A, V, C) și panoul Ce știm / Ce suspectăm / Ce nu putem demonstra.
Grila de constatări rămâne; fișa este panoul elementului selectat (selectarea nu modifică cazul). „De ce?” are cinci părți: ce am observat (`Description`), dovezi, raționament (`ClassificationReason`, niciodată gol),
limite (probă lipsită, explicații alternative, dovezi contrare, `Limitations`) și verificarea (verdictul modulului WP4 cu motiv și controale). Un scor euristic vechi (`LegacyScore`) se arată doar
împreună cu factorii lui și cu precizarea că nu este o verificare; fără factori nu se arată deloc.

## 4. Dovezile pe trei niveluri (U6)
`EvidenceLevels.Build`: rezumat („4 probe din 2 surse”, cu acordul corect în română: „1 probă dintr-o sursă”, „20 de probe din 20 de surse”), listă (sursă, oră UTC, descriere scurtă) și tehnic (id probă, SHA-256,
cale sursă, parser + versiune, marcaj de timp, locator). Datele vin din `EvidenceRef`, `EvidenceItem`, `TimelineEvent` și `ParseResult`; ce lipsește se afișează „necunoscut în caz”, nu se completează.

## 5. Regula „Ce știm / Ce suspectăm / Ce nu putem demonstra” (U8)
Documentată în `KnowThinkDontKnow.cs` și testată pe regulă:
- **CE ȘTIM**: înregistrările citate de constatare (ce arată fiecare, cu EvidenceId + locator); afirmația constatării doar dacă are probe ȘI este observată (clasificare Direct sau BenignKnown) sau verificată (VERIFIED);
  pașii observați ai unei secvențe; dovezile contrare găsite.
- **CE SUSPECTĂM**: afirmația constatării când are probe dar nu este observată (Correlated, Candidate, Unproven) și nu a fost contrazisă sau respinsă, cu motivul clasificării; explicațiile alternative care nu pot fi excluse.
- **CE NU PUTEM DEMONSTRA**: proba lipsă, limitele constatării (cine a pornit, intenția, atribuirea), pașii de secvență neobservați („pas neobservat”, nu „absent”), golurile de probă ale surselor pe care se sprijină
  constatarea, o afirmație fără probe și o afirmație pe care verificatorul a contrazis-o sau a respins-o.

## 6. Baza de accesibilitate (U18)
Pe pagina de investigație, pe Acasă și în controalele noi: `AutomationProperties.Name` pe fiecare element interactiv (câmpuri, liste, grile, taburi), `HelpText` pe cele patru acțiuni, taste de acces pe acțiuni,
nicio stare dată doar prin culoare (text + pictogramă), niciun `FontSize` sub 12 în fișierele din domeniu, antet numit pentru fiecare coloană de grilă. Testul `Wp6aXamlTests` parsează XAML-ul și
pică dacă un element interactiv nou nu are nume. Nu sunt acoperite încă (rămân pentru pașii următori): contrastul ridicat (HighContrast), alternativa text a grafului pe celelalte pagini, restul paginilor aplicației.
