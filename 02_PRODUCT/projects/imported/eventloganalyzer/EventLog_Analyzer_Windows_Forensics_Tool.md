---
title: EventLog Analyzer Windows Forensics Tool
type: application
status: active
category: product
---

# EventLog Analyzer — Offline Windows Forensics / IR Triage Tool (MVP)

A fully offline desktop application for security administrators and incident
responders that ingests exported `.evtx` logs, `NTUSER.DAT` hives, and
`HKLM` registry hives from a backup folder, explains what's in them in plain
language, flags suspicious/error patterns, and offers reviewable
(never auto-run) PowerShell remediation.

---

## 1. Tech stack decision

**Chosen: Option A — .NET 8 (C#), WPF, MVVM.**

Rationale:

- `System.Diagnostics.Eventing.Reader` gives first-class, offline `.evtx`
  parsing (`EventLogReader` + `PathType.FilePath`) with zero native
  dependencies and full fidelity access to `EventRecord` (Id, Level,
  Provider, TimeCreated, Keywords, UserId, formatted `Message`).
- Registry hive parsing (`NTUSER.DAT`, `HKLM-*.hiv`) is done **without
  mounting or loading the hive into the live registry** (which would need
  elevation and touch the host system) by using **Eric Zimmerman's
  `Registry` library** (NuGet: `Registry`), a mature, MIT-licensed,
  pure-.NET raw hive parser built exactly for offline DFIR use. This keeps
  the "no online services, fully offline, read-only" constraint trivially
  satisfiable — the app never calls `RegLoadKey` or needs admin rights
  just to *read*.
- WPF + MVVM gives a mature, native, fast-to-ship desktop UI on Windows
  with good data-virtualization support for large event grids (tens of
  thousands of rows), and is easier to make "visually ready for
  commercialization" than WinUI 3's still-maturing tooling.
- Everything runs as a single self-contained .NET executable — no browser
  runtime, no Node/Electron surface area, smaller attack surface, easier
  to certify for isolated/air-gapped networks.

If a cross-platform triage viewer is ever needed, the `Core` project below
has **zero WPF references** and can be reused as-is behind a Tauri/Electron
shell (Option B) later — see "Future versions" at the end.

---

## 2. Solution layout

```
EventLogAnalyzer/
├─ src/
│  ├─ Core/                          # EventLogAnalyzer.Core.dll — no UI deps, unit-testable
│  │  ├─ Models/Domain.cs            # EventRecordModel, Issue, Recommendation, UserActivityItem, enums
│  │  ├─ Parsers/
│  │  │  ├─ IArtifactParser.cs       # common parser contract
│  │  │  ├─ EvtxParser.cs            # .evtx -> EventRecordModel[]
│  │  │  ├─ NtUserHiveParser.cs      # NTUSER.DAT -> UserActivityItem[]
│  │  │  └─ HklmHiveParser.cs        # HKLM hive -> config/driver findings
│  │  ├─ KnowledgeBase/
│  │  │  └─ EventKnowledgeBase.cs    # loads data/event-knowledge-base.json, resolves EventId+Source -> explanation
│  │  ├─ Detection/
│  │  │  └─ Detectors.cs             # IIssueDetector + concrete detectors (crash loop, disk, logon, DNS/GPO)
│  │  └─ Remediation/
│  │     └─ RemediationScriptBuilder.cs  # Issue -> reviewable .ps1 text, never executes
│  ├─ App/                           # EventLogAnalyzer.App.exe — WPF, MVVM
│  │  ├─ ViewModels/ViewModels.cs    # DashboardViewModel, EventExplorerViewModel, RelayCommand
│  │  └─ Views/MainWindow.xaml(.cs)  # shell: sidebar nav + filter bar + content frame
├─ data/
│  └─ event-knowledge-base.json      # extensible EventID/Source -> explanation+severity+fix mapping
└─ tests/
   └─ CoreTests.cs                   # xUnit: parser mapping, KB resolution, crash-loop detector
```

### Responsibilities at a glance

| Module | Responsibility | Depends on |
|---|---|---|
| `Models` | Immutable-ish domain records, no logic | nothing |
| `Parsers` | Turn raw artifacts into `Models` objects, nothing else | `Models`, `System.Diagnostics.Eventing.Reader`, `Registry` NuGet |
| `KnowledgeBase` | Static lookup: `(EventId, Provider)` → human explanation, severity, root causes, fix | `Models` |
| `Detection` | Stateful pattern analysis over a *collection* of events → `Issue` objects | `Models`, `KnowledgeBase` |
| `Remediation` | `Issue` → PowerShell script text (string builder only, no `Process.Start`) | `Models` |
| `App/ViewModels` | Orchestrates Core, exposes `ObservableCollection`s + `ICommand`s to XAML | `Core` |
| `App/Views` | Pure XAML + code-behind wiring, no business logic | `ViewModels` |

This separation means `Core` can be unit tested with `dotnet test` on any
machine (including this Linux sandbox) with no WPF/Windows dependency, while
`App` is compiled only on Windows.

---

## 3. Data flow

```
User picks C:\BACKUPLOGS\2026\08\PC01
        │
        ▼
IngestionService (App layer, calls Core)
   ├─ discovers *.evtx           → EvtxParser         → IEnumerable<EventRecordModel>
   ├─ discovers NTUSER-*.dat     → NtUserHiveParser    → IEnumerable<UserActivityItem>
   └─ discovers HKLM-*.hiv/.reg  → HklmHiveParser      → IEnumerable<ConfigFinding>
        │
        ▼
EventKnowledgeBase.Explain(event)   →  attaches HumanTitle / Explanation / Severity / Recommendation to each EventRecordModel
        │
        ▼
IssueDetector[] .Detect(events)     →  IEnumerable<Issue>  (repeated crash, disk errors, logon storms, DNS/GPO errors)
        │
        ▼
DashboardViewModel / EventExplorerViewModel bind ObservableCollections
        │
        ▼
User selects an Issue → "Generate fix script" → RemediationScriptBuilder.Build(issue) → shown in a read-only TextBox
        │
        ▼
Explicit "I understand — Run" confirmation dialog → only then Process.Start("powershell.exe", scriptPath)
```

Nothing in this pipeline makes a network call. `EvtxParser`, both hive
parsers, and the knowledge base all operate purely on local files.

---

## 4. Knowledge base format

`data/event-knowledge-base.json` is a flat array so it's trivial to append
to, diff in code review, and ship updates for independently of the binary:

```json
[
  {
    "eventId": 7031,
    "provider": "Service Control Manager",
    "title": "Service crashed and Service Control Manager is restarting it",
    "severity": "Error",
    "explanation": "A Windows service terminated unexpectedly. SCM has a recovery action configured and is restarting it automatically.",
    "commonCauses": [
      "Unhandled exception in the service binary",
      "Missing or corrupted dependency (DLL, driver, config file)",
      "Resource exhaustion (memory/handles) in the service process"
    ],
    "recommendedAction": "Check the service's own event source for the underlying exception, verify binPath and dependencies with 'sc qc <service>', and confirm the account running the service has required permissions.",
    "docsUrl": "https://learn.microsoft.com/windows/win32/services/service-control-manager"
  }
]
```

See `EventKnowledgeBase.cs` for the loader/matcher and
`RepeatedServiceCrashDetector` for a detector that consumes this shape.

### Adding a new event signature
1. Append an object to `data/event-knowledge-base.json` (no rebuild needed —
   loaded at runtime from the app's working directory / `%ProgramData%`).
2. If it's a duplicate `(eventId, provider)`, the loader keeps the **last**
   entry and logs a warning — so overrides are just "append at the end."

### Adding a new detector
1. Implement `IIssueDetector` (`Detect(IReadOnlyList<EventRecordModel>)`).
2. Register it in `DetectionEngine`'s constructor list (composition root,
   see `App/App.xaml.cs` in the real project — omitted here for brevity).
3. Write a unit test in `tests/CoreTests.cs` following the
   `RepeatedServiceCrashDetector` example: build a small in-memory list of
   `EventRecordModel`, assert on the returned `Issue`.

### Adding a new remediation recipe
1. Add a case to `RemediationScriptBuilder.Build(Issue issue)` keyed on
   `issue.Category`.
2. Keep it string-templated PowerShell — the builder must never itself
   execute anything; execution is a separate, explicitly-confirmed step in
   the ViewModel.

---

## 5. Build & run (Windows)

Prerequisites: Windows 10/11, .NET 8 SDK.

```powershell
cd EventLogAnalyzer
dotnet restore
dotnet build -c Release

# Run the core unit tests (works on any OS, including CI on Linux):
dotnet test tests/EventLogAnalyzer.Tests.csproj

# Run the WPF app (Windows only):
dotnet run --project src/App/EventLogAnalyzer.App.csproj
```

> **Note on `System.Diagnostics.EventLog`:** since .NET 5, the
> `System.Diagnostics.Eventing.Reader` types (`EventLogReader`, `EventRecord`,
> `EventLogQuery`) live in a separate NuGet package, not the `net8.0-windows`
> shared framework by itself. `Core.csproj` references it explicitly
> (`System.Diagnostics.EventLog`, 8.0.2) - without it you'll get
> `CS1069: The type name 'EventRecord' could not be found ... consider
> adding a reference to that assembly`.

To produce a single-file, self-contained EXE for an isolated network:

```powershell
dotnet publish src/App/EventLogAnalyzer.App.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -o dist/
```

Copy `dist/EventLogAnalyzer.App.exe` plus `data/event-knowledge-base.json`
onto the target machine — no installer, no internet access required.

---

## 6. Unit tests included (see `tests/CoreTests.cs`)

- `EvtxParser` mapping: a hand-built `EventRecordModel` is checked for
  correct field mapping from the reader's `EventRecord` (mocked at the
  boundary since `EventLogReader` itself is a thin OS wrapper not worth
  mocking beyond the boundary interface).
- `EventKnowledgeBase` resolution: given an in-memory JSON snippet, resolves
  `(7031, "Service Control Manager")` to the expected title/severity, and
  falls back to a generic "Unmapped event" explanation when no signature
  matches.
- `RepeatedServiceCrashDetector`: feeds 5 synthetic `7031` events for the
  same service within a 10-minute window and asserts a single `Issue` with
  `Impact.High` and `Count == 5` is produced; asserts *no* issue is raised
  for 2 isolated crashes 3 hours apart.

---

## 7. Safety properties (explicit, by design)

- Parsers open all files with `FileMode.Open` / `FileAccess.Read` only —
  never write to source `.evtx`/`.dat`/`.hiv` files.
- Hive parsing never calls `RegLoadKey`/`RegRestoreKey` against the live
  registry — it parses the raw hive binary format directly, so the app does
  **not** require the elevation that mounting a hive would otherwise need
  purely to *read* it.
- `RemediationScriptBuilder` only ever returns a `string`. Nothing in
  `Core` has a reference to `System.Diagnostics.Process`. Execution lives
  solely in the `App` layer behind an explicit confirmation dialog, and the
  generated script is shown to the user first.
- No `HttpClient`, no `System.Net` usage anywhere in `Core`; `docsUrl`
  fields in the knowledge base are display-only strings, opened (if at all)
  via the OS default browser only on explicit user click, never fetched by
  the app itself.

---

## 8. Suggestions for future versions

- **Live agent mode**: a lightweight Windows service that tails the local
  event log in near-real-time and republishes the same `Issue`/`EventRecordModel`
  shapes over a local named pipe or gRPC-over-localhost — no new domain
  model needed, `Core` is already artifact-source-agnostic.
- **SIEM integration**: an optional exporter that serializes `Issue` and
  flagged `EventRecordModel`s to CEF/LEEF or a Splunk HEC-compatible JSON
  batch, kept as a separate opt-in project so the offline/air-gapped build
  has zero networking code by default.
- **Multi-host correlation**: since ingestion is already "point at a
  folder," support pointing at a folder-of-folders (`PC01/`, `PC02/`, ...)
  and add a `HostId` dimension to `EventRecordModel`/`Issue` so the
  Dashboard can show "same EventID across N hosts in T minutes" — a common
  lateral-movement/worm signature.
- **Shellbag and full UserAssist decoding**: MVP's `NtUserHiveParser`
  reads UserAssist and basic MRU lists; a v2 could add full shellbag
  timeline reconstruction (folder-access forensics) using the same
  `Registry` library's `ShellBagParser` primitives.
- **YARA/Sigma-style rule packs**: let advanced users author detection
  rules in a declarative format instead of C# `IIssueDetector` classes,
  loaded at runtime like the JSON knowledge base already is.
- **Chain-of-custody / report export**: one-click PDF/HTML incident report
  (timeline + issues + evidence hashes of the source artifacts) for
  handoff to management or law enforcement.
