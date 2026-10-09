# Deschiderea unui caz existent, matricea de acoperire și pagina Acasă (WP5)

Închide R9.1, U3, U21, U22 din `CONTRACT_AUDIT_STAGE1.md`. Cod: `LogAnalyzer.Dfir.Windows/Investigation/CaseLoader.cs`, `LogAnalyzer.Dfir.Core/Coverage/CoverageMatrix.cs`,
`LogAnalyzer.Dfir.Core/Home/HomeSummary.cs`, `LogAnalyzer.Dfir.Core/Case/RecentCases.cs`, `LogAnalyzer.App/ViewModels/HomeViewModel.cs`. Teste: `Wp5CaseHomeTests` (Dfir), `HomeViewModelTests` (App, doar pe Windows).

## 1. Deschiderea unui caz (R9.1)

`CaseLoader.Open(folder)` reconstruiește un `InvestigationResult` din fișierele pe care le scrie deja pipeline-ul: `Analysis/findings.json` (constatări, goluri, rânduri de colectare),
`Analysis/timeline.csv`, `Analysis/parsing.json`, `Analysis/verification.json`, `Analysis/run_state.json` și `case.json` (scopul). Nu există un al doilea depozit de cazuri.

- **Ordinea.** Mai întâi se citesc toate fișierele; dacă unul are o versiune necunoscută (`schema_version` major peste 2, `case.json` altă versiune majoră decât 1) sau nu se poate citi,
  cazul este **refuzat cu motivul** și în folder nu se scrie nimic. Abia apoi `CaseWorkspace.Open` (care auditează deschiderea ca `OperatorIdentity`, adică contul autentificat), apoi `Recheck()` (WP3b).
  `CaseLoader.OpenAsync` rulează totul pe un `Task`, ca reverificarea unui caz mare să nu blocheze interfața.
- **Doar pentru citire.** Cazul se deschide doar pentru citire când: audit-ul conține `case.closed` (SIGILAT), toate probele sunt ARCHIVED/DISPOSED (ARHIVAT), reverificarea găsește rezultate
  `INVALIDATED`, sau reverificarea eșuează (probă MODIFICATĂ/LIPSĂ, lanț rupt). `CaseWorkspace.MarkReadOnly` face ca `ImportFile`, `RegisterStored`, `RecordOutput`, `ConfirmScope`, `TransitionState`,
  `MarkDisposed`, `SetLegalHold`, `SetRetention` să refuze; auditul și reverificarea rămân posibile. Motivele sunt în `LoadedCase.ReadOnlyReasons` și în auditul `case.opened_readonly`.
  Un caz „Legacy” sau „Unverified” NU devine doar pentru citire, dar pagina Acasă îl arată ca încredere limitată.
- **Ce nu se reconstruiește (spus în `LoadedCase.Notes`, niciodată ascuns).** Cronologia vine din `timeline.csv`, care nu poartă câmpurile specifice parserelor (`Fields`, `Hash`, `Task`...).
  Golurile adăugate de pipeline după scrierea `findings.json` (reguli neîncărcate, fișiere nescanate de YARA), detecțiile, regulile, graful și verificările anti-forensics nu sunt reîncărcate.
  Un caz fără `findings.json` se încarcă ca „analiza nu a fost rulată” (stare NOT_STARTED), nu ca „fără constatări”.
- **Cazuri recente.** `RecentCases` (implicit `%LOCALAPPDATA%\LogAnalyzer\recent_cases.json`, per utilizator) ține calea, titlul, ultima deschidere și starea. Un folder care nu mai există apare
  „LIPSĂ (folderul nu mai există)” și rămâne în listă până îl elimină operatorul. Un caz refuzat nu se memorează.

## 2. Matricea de acoperire (U21)

Un rând pe familie de artefacte: jurnale EVTX, Prefetch, Amcache, ShimCache, BAM, SRUM, hive-uri (NTUSER, SOFTWARE), servicii, USN, LNK/Jump Lists, USB, profiluri de rețea, captură de rețea,
browser, sarcini programate, starea live. Fiecare rând: stare, motiv, starea parserului (VALIDATED / TESTED / EXPERIMENTAL din descriptor, sau „fără parser”).

| Stare | Regula |
|---|---|
| COLLECTED | există probe ale familiei și toate parsările au reușit (SUCCESS), fără goluri de audit și fără erori de colector |
| PARTIAL | probe prezente dar neparsate; unele parsări parțiale/eșuate/goale; **o parsare fără înregistrări** (nu dovedește absența activității); goluri de audit pe Security; eroare de colector |
| UNAVAILABLE | nicio probă și colectorul a eșuat sau a rulat fără să producă sursa; sau toate parsările au eșuat |
| NOT_COLLECTED | nicio probă și niciun colector care o produce nu a rulat |
| NOT_SUPPORTED | aplicația nu are parser pentru familie (acum: profiluri de rețea) |

**Acoperirea generală** (calculată doar pe familiile suportate; scor = (COLLECTED + 0,5 · PARTIAL) / suportate):

- `UNKNOWN`: cazul nu are nicio probă, nicio colectare, nicio parsare;
- `FULL`: toate familiile suportate sunt COLLECTED (motivul listează totuși familiile nesuportate; FULL nu înseamnă „sistem curat”);
- `MINIMAL`: scor sub 1/3; `PARTIAL`: restul.

## 3. Agregatorul Acasă (U3)

`HomeAggregator.Build(HomeInputs)` răspunde la: există o problemă, cât de grav, sunt probele de încredere, ce s-a găsit, ce urmează. Nivelul de atenție nu are valoare SAFE/NORMAL/OK:
`Critical/High/Medium/Low` după cea mai gravă constatare; fără constatări, **„Nimic detectat în sursele analizate”** cu nivelul de acoperire alături (sau `Undetermined` când acoperirea
este MINIMAL/UNKNOWN sau analiza nu s-a terminat). Încrederea vine din reverificare (Valid = intact; Legacy/Unverified = limitată; Modified/Missing/ChainBroken = compromisă; fără reverificare = nedeterminat).
Verificarea automată lipsă = „nedeterminat”. Pașii următori (cel mult 5) sunt generați din date: integritate, constatări grave, constatări contrazise/nedovedite, surse lipsă, scop neconfirmat, caz doar pentru citire.

## 4. Pagina și intențiile (U22)

Pagina „Acasă” este prima din bara laterală și pagina implicită (tab 22, adăugat la sfârșit ca să nu se renumeroteze celelalte). Fără caz deschis arată cele trei intenții:
*Verifică acest calculator* (pagina Investigație completă cu colectare), *Analizează probe* (aceeași pagină, doar import), *Deschide caz existent* (secțiunea 1). Alegerea întrebărilor
(„A rulat un program?”) este WP7: în cod există doar cârligul gol `HomeViewModel.ChooseQuestion`.

## 5. Afirmații înșelătoare schimbate (U3)

`MainViewModel`: nivelul de risc implicit „SCĂZUT (Normal)” (verde) → „NEDETERMINAT (analiza nu a fost rulată)”. În interfața veche din rădăcina proiectului (nu face parte din soluție): „ALL SYSTEMS NORMAL”,
„SHIELD ARMED & SECURE”, „EVIDENCE VAULT SECURED”, „Lanț Criptografic Verificat” și „ACTIVE / armed” au fost înlocuite cu text neutru sau legat de starea reală. În `LogAnalyzer.App` celelalte texte erau deja
înlocuite de WP1.
