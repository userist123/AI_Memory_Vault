---
id: "b45781a9-30bc-4389-aeb0-5e8c14828bc4"
type: lesson
lifecycle: REVIEW
category: DFIR_Development
tags: [wpf, evtx, dfir, eventlogreader]
created: 2026-08-11
updated: 2026-08-11
provenance:
  source_type: experience
  source_ref: 7ac31289-580c-4134-9fd8-62e86ceba1f3
confidence: very_high
verification: verified
relations: []
---

# WPF Splash Screens and EventLogReader Exception Handling in DFIR Apps

## 1. WPF Splash Screen Premature Shutdown
When implementing a custom `SplashWindow` at application startup in `App.xaml.cs`:
- **The Issue:** The default `Application.ShutdownMode` is `OnLastWindowClose`. If you show a splash screen, run validation checks, and then close it before showing the `MainWindow`, WPF detects that the number of open windows has dropped to zero and initiates a clean shutdown immediately. The application exits with code 0 before showing `MainWindow`.
- **The Fix:** Set `this.ShutdownMode = ShutdownMode.OnExplicitShutdown` before showing the splash screen, close the splash screen, instantiate and show `MainWindow`, and then restore `this.ShutdownMode = ShutdownMode.OnLastWindowClose`.

## 2. EventLogReader Metadata Exception
When reading offline EVTX files using `System.Diagnostics.Eventing.Reader.EventLogReader`:
- **The Issue:** Querying `record.LevelDisplayName` on an `EventRecord` from a file that was generated on another machine can throw a fatal `EventLogException` or `EventLogNotFoundException` if the specific event provider metadata is not registered on the system running the parser.
- **The Fix:** Always wrap access to provider-dependent properties (like `LevelDisplayName` and `FormatDescription()`) in a `try-catch` block, falling back to a default value (e.g., `"Info"`).

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[08 Memory Subsystems Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
