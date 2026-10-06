# _recovered — fișiere LogAnalyzer care nu fuseseră niciodată commit-uite

Recuperate pe 2026-10-04, la consolidarea tuturor copiilor aplicației. Fiecare fișier a fost comparat prin SHA-256 cu workspace-ul. Aici au ajuns doar fișierele care nu existau nicăieri sub control de versiune.
`PROVENANCE.csv` conține, pentru fiecare fișier: calea sursă, dimensiunea, ora ultimei modificări (locală) și SHA-256-ul verificat după copiere.

Conținutul este o **arhivă de referință**. Nu se compilează (folderul nu face parte din `LogAnalyzer.slnx`) și nu trebuie modificat. Ideile utile se portează în proiectele active.

| Folder | Sursă | Ce este | Folosire |
|---|---|---|---|
| `desktop_mvp_scripts/AuditCollector.ps1` | `Desktop\LogAnalyzer.MVP\Scripts` (2026-08-14) | Colector de triere pe tipuri de țintă: PC / Server / NAS / DataCenter. Colectează EVTX, Run keys, conexiuni, procese cu SHA-256, DNS cache, task-uri, utilizatori și administratori, firewall, drivere cu Authenticode, Defender, istoric PowerShell, USBSTOR, sesiuni, rute | Listă de referință pentru colectoarele din faza 3 (`LogAnalyzer.Dfir.Windows/Acquisition`). Scriptul folosește `ErrorActionPreference=SilentlyContinue` și nu raportează golurile, așa că nu se reutilizează ca atare |
| `desktop_mvp_root/knowledge_base_extins.json` | `Desktop\LogAnalyzer.MVP` (2026-08-06) | Variantă timpurie și mică (4,6 KB) a knowledge base-ului | Înlocuită de `LogAnalyzer.Infrastructure/Data/knowledge_base_extins.json` (401 KB). Păstrată pentru istoric |
| `downloads_mvp_prototype_2026-08-08/` | `Downloads\LogAnalyzer.MVP` (2026-08-08) | Prototipul inițial net8 (soluție Core + UI), strămoșul aplicației | Vezi mai jos |

## Ce merită portat din prototip

- **`LogIntegrityService`** detectează alterarea jurnalelor:
  - evenimentele de ștergere 1102/104;
  - salturile de `EventRecordId` peste un prag;
  - opririle repetate ale logging-ului (1100/1101/6005/6006/6008).

  Se potrivește direct cu findings-urile de tip „log tampering” și cu evidence gaps din spec. Candidat pentru faza 7/8 din `LogAnalyzer.Dfir.Core`, cu clasificarea DIRECT / CANDIDATE în loc de scorul fix.
- **`DetectionRule` + `AnalysisEngineService`**: reguli declarative cu prag, fereastră de timp și mapare MITRE (tactică, tehnică, încredere). Model util pentru motorul de corelare. `catch {}`-ul gol din buclă nu se preia.
- **`IncidentReportGenerator`**: raport Markdown pe secțiuni (rezumat, integritate jurnale, cronologie, alerte, IOC-uri relevante, note de analist). Șablon pentru exportul de rapoarte din faza 10. Valoarea implicită `GeneratLa = DateTime.Now` nu se preia (spec: timestamp-urile nu primesc niciodată „acum”).
- **`IocExtractionService`**: a fost deja înlocuit de `LogAnalyzer.Dfir.Core/Analysis/IocExtractor.cs`, care clasifică și IP-urile.

Restul fișierelor din prototip (MainWindow, MainViewModel, AlertDetailWindow, modele) au versiuni mai noi în ediții.
