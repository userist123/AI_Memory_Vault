# Rules: LogAnalyzer Forensic & Threat Intelligence WPF Project Guidelines

These guidelines are automatically applied to the development of the **LogAnalyzer** application suite. All future development, refactoring, and debugging must conform to these constraints.

---

## 1. Clean Architecture Decoupling
- **Decoupling Constraint:** The `LogAnalyzer.Core` project must have **zero** dependencies on external packages (like SQLite or UI frameworks) and **zero** direct references to the `LogAnalyzer.Infrastructure` or `LogAnalyzer.UI` projects.
- **Dependency Flow:** Dependency must flow inward. UI depends on Core and Infrastructure. Infrastructure depends on Core.
- **Interface-Driven Design:** Core must define all interfaces (e.g., parsers, databases, services). Infrastructure must implement them. UI must resolve them via Dependency Injection (DI) registered in `App.xaml.cs`.

## 2. Asynchronous Execution (UI Responsiveness)
- **UI Thread Safety:** WPF UI thread must never freeze. CPU-bound or disk/network I/O-bound operations (such as EVTX log parsing, registry hive reading, or syslog reception) must be run asynchronously (`Task.Run` or `async/await`).
- **Cancellation & Progress:** Async operations in parsers and engines should accept a `CancellationToken` and support `IProgress<T>` for UI progress bars.

## 3. MVVM Architecture Compliance
- **Code-behind Restriction:** No business or presentation logic is allowed in XAML code-behind (e.g., `MainWindow.xaml.cs`). Events must be handled using bindings, commands, or behavior triggers.
- **MVVM Toolkit:** ViewModels must use `CommunityToolkit.Mvvm` source generators. Expose observable properties with `[ObservableProperty]` and commands with `[RelayCommand]`.
- **UI Notifications:** Long-running VM tasks must set a boolean flag (e.g., `IsLoading`) to trigger a loading overlay in the View.

## 4. Air-Gapped / Offline Forensics Policy
- **No External Network Calls:** The command center operates in air-gapped/offline environments. Block all direct web HTTP calls, online DNS queries, or live threat-intelligence APIs (e.g., online VirusTotal, external IP lookup services) from the application logic.
- **Local Databases:** All threat signatures (such as MITRE ATT&CK timelines, IOCs, IP reputations, or GPO compliance indicators) must be queried from localized, local resources (like SQLite or embedded JSON database files).

## 5. Chain of Custody & Cryptographic Auditing
- **Hash Computation:** Forensic integrity is crucial. Ingestion of EVTX or Registry files must compute a SHA-256 hash of the source file.
- **Audit Logging:** The SHA-256 hash, file path, file size, and timestamp of ingestion must be written to the local audit log database or secure local text stream (`AuditLogService`) to guarantee evidence preservation and chain of custody.
