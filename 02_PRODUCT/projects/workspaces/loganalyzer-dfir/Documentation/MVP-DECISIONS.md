# Decizii MVP — LogAnalyzer

Prima versiune a fost scrisă pe 2026-08-15 (LogAnalyzer.UI 137a626). Pe 2026-10-04 a fost actualizată pentru workspace-ul din vault: platforma DFIR și LicenseManager; edițiile AirGapped/Network au fost unite în aceeași zi într-o singură aplicație (decizia 11).

| # | Decizie | Alternativă respinsă | Motiv | Reevaluare |
|---|---------|----------------------|-------|------------|
| 1 | Self-contained single-file win-x64 | Framework-dependent | Stațiile țintă pot fi fără runtime .NET și fără internet | Post-MVP: installer MSIX |
| 2 | `PublishTrimmed=false` | Trimming | WPF împreună cu reflection/DI riscă erori la runtime | Doar cu teste e2e extinse |
| 3 | Fără Native AOT | Native AOT | WPF nu este suportat de Native AOT | Urmărim roadmap-ul .NET |
| 4 | Release manual din artefactul CI | Release automat pe tag | Artefactul se validează întâi pe o mașină curată | După 2–3 release-uri reușite |
| 5 | Fără semnare Authenticode în MVP | Certificat self-signed | Un certificat self-signed nu adaugă încredere reală | Certificat OV/EV la distribuție |
| 6 | Verificarea `Categories` în CI blochează build-ul | Doar warning (în 137a626) | Publicarea locală pe 2026-10-04 a confirmat că `Categories` ajunge în output | — |
| 7 | Soluție `LogAnalyzer.slnx`, cu edițiile în subfoldere | Proiect la rădăcină cu `Compile Remove` | Aplicația reală este în `LogAnalyzer.App/`. `.csproj`-urile de la rădăcină sunt moștenite și nu fac parte din soluție | Eliminarea lor după confirmarea proprietarului |
| 8 | Serviciile duplicate între ediții și Core sunt doar documentate | Refactor acum | Risc de regresie | Prima iterație post-MVP |
| 9 | Licențiere cu hash + salt (`Core LicenseService`), un singur algoritm pentru aplicație și pentru cele trei generatoare | Rescriere RSA-PSS acum | Schema este operațională și licențele emise trebuie să rămână valide. Trecerea la semnătură asimetrică invalidează toate cheile emise | Critic înainte de distribuție comercială, cu decizia proprietarului |
| 10 | Platforma DFIR în proiecte noi (`LogAnalyzer.Dfir.*`), edițiile rămân neatinse până la integrarea în UI | Rescrierea parserelor vechi pe loc | Parserele vechi sunt doar fațade (vezi `docs/dfir/DFIR_CURRENT_ARCHITECTURE_AUDIT.md`), iar cele noi se validează separat pe un corpus real | Faza 12 (integrare UI) |
| 11 | O singură aplicație; modul AirGapped / Network se alege la pornire prin detectare pasivă (Windows NLM), cu override și fail-closed spre AirGapped | Două executabile compilate cu `#if` | Cele două copii aveau 83 de fișiere identice și diferențe doar de afișare; operatorul nu trebuie să știe dinainte ce ediție să ducă pe teren. Detectarea nu trimite trafic, iar modul nu se schimbă singur în timpul rulării | — |

## Cunoscute, amânate conștient

- **Derive rezolvate pe 2026-10-04**:
  - `KeyGen` calcula alt Hardware ID decât aplicația; acum folosește `Core LicenseService`.
  - Generatorul și aplicația aveau verificări separate; acum folosesc împreună `BuildLicenseString` / `VerifyLicenseString`.
- **Încă deschise**:
  - `LicenseService`-ul RSA-PSS din `Services/` al aplicației (namespace `LogAnalyzer.UI.Services`) nu este conectat la fluxul de activare; îl acoperă doar testele.
  - `PHASE1-STATUS.md` descrie licențe RSA-PSS, deci trebuie corectat sau implementat (decizia 9).
- `license.lic` este urmărit în git la rădăcina workspace-ului (moștenire). Nu se livrează. Eliminarea lui se face după confirmarea proprietarului.
- `MainWindow.xaml` și `MainViewModel.cs` sunt god objects și trebuie sparte pe UserControl-uri post-MVP.
- Testul `SecurityEventIngestionServiceTests.ReadsValidMetadataOnlyEvent` pică pe `main` din #204 și trebuie investigat separat.
