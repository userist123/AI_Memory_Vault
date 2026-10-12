# LogAnalyzer.MVP — SOC Command Center Offline (versiune unificata)

Aplicatie WPF + MVVM (CommunityToolkit.Mvvm), 100% offline, pentru triage DFIR:
EVTX, Registry, reguli MITRE ATT&CK din JSON, IOC hunting, anti-tampering si
export de raport de incident.

## IMPORTANT - fix pentru eroarea CS0101 "already contains a definition"

Daca ai extras o versiune anterioara a arhivei in acelasi folder, e posibil sa
existe FISIERE DUPLICATE (ex. un subfolder LogAnalyzer.MVP\LogAnalyzer.MVP\...
sau o copie veche a LogAnalyzer.Core\Services\KnowledgeBaseService.cs).
MSBuild (SDK-style csproj) include automat TOATE fisierele .cs gasite recursiv
in folderul proiectului, deci orice copie ramasa duce la eroarea:

    error CS0101: The namespace '...' already contains a definition for 'KnowledgeBaseService'

**Solutie recomandata (curata):**

1. Sterge complet folderul vechi: `C:\Users\Marius\Downloads\LogAnalyzer.MVP`
2. Dezarhiveaza din nou aceasta arhiva noua intr-un folder **gol**.
3. Verifica sa NU existe o structura dubla, adica NU trebuie sa vezi
   `LogAnalyzer.MVP\LogAnalyzer.MVP\...` ci direct
   `LogAnalyzer.MVP\LogAnalyzer.Core\...` si `LogAnalyzer.MVP\LogAnalyzer.UI\...`.
4. Ruleaza din radacina proiectului:
   ```
   cd C:\Users\Marius\Downloads\LogAnalyzer.MVP
   dotnet clean
   dotnet build
   dotnet run --project LogAnalyzer.UI/LogAnalyzer.UI.csproj
   ```

Am adaugat suplimentar in ambele `.csproj` reguli `<Compile Remove>` care exclud
explicit orice copie nested accidentala (`**/LogAnalyzer.MVP/**/*.cs`,
`**/bin/**/*.cs`, `**/obj/**/*.cs`), astfel incat, chiar daca ramane un fisier
vechi ratacit, sa nu mai fie inclus automat in compilare.

## Structura solutiei

```
LogAnalyzer.MVP.sln
├── LogAnalyzer.Core/
│   ├── Interfaces/   IEventParser, IAnalysisEngine, IRegistryParser,
│   │                 IIocExtractionService, ILogIntegrityService, IIncidentReportGenerator
│   ├── Models/       ParsedEvent, RegistryArtifact, DetectedIssue (MITRE+SLA+TP/FP),
│   │                 TimelineItem, IocItem, DfirProfile, EventKnowledgeItem,
│   │                 DetectionRule + MitreMapping, LogIntegrityResult, IncidentReport
│   └── Services/     KnowledgeBaseService, EventParserService, RegistryParserService,
│                     AnalysisEngineService (reguli JSON generice), IocExtractionService,
│                     LogIntegrityService (anti-tampering), IncidentReportGenerator (Markdown)
└── LogAnalyzer.UI/
    ├── Categories/       knowledge_base_extins.json (5 reguli reale cu MITRE + praguri)
    ├── ViewModels/       MainViewModel
    ├── Views/            AlertDetailWindow (MITRE + checklist L1 + status/TP-FP)
    ├── App.xaml/.cs      protectii globale la crash
    └── MainWindow.xaml(.cs)  dashboard SOC (Timeline, EVTX, Registry, Integritate)
```

## Cum rulezi

```
dotnet restore
dotnet build
dotnet run --project LogAnalyzer.UI/LogAnalyzer.UI.csproj
```

Necesita .NET 8 SDK si (pe Windows) workload-ul WPF. "Incarca Folder Investigatie"
selecteaza un folder cu fisiere `.evtx` si/sau `.reg`.

## Puncte de extindere ramase

- `RegistryParserService.ParseNtUserDat` — parsare hive binar necesita o librarie
  externa (ex. `Registry` de Eric Zimmerman); returneaza acum lista goala, in siguranta.
- Editor grafic de reguli si constructor de filtre favorite — regulile se editeaza
  direct in `knowledge_base_extins.json`.
