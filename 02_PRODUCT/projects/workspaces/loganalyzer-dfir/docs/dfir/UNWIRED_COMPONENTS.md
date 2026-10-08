# LogAnalyzer - unwired components (stage 2, WP1)

Components below have **no production consumer** (no use outside tests in the compiled projects `LogAnalyzer.App`, `Core`, `Infrastructure`, `Dfir.Core`, `Dfir.Windows`), re-checked on 2026-10-08 with a whole-word search. They are not production-ready and nothing is built on them. Per owner decision 4 they are left untouched in stage 2: not deleted, not finished. Each source file carries a one-line `// UNWIRED` comment; no behaviour changed.

The authoritative list, with what each would need in order to be wired, is section 5 of `CONTRACT_AUDIT_STAGE1.md`.

Marked in code in this change:

- Parsers (`LogAnalyzer.Infrastructure/Parsers`): `MftParser`, `AntiForensicsArtifactsParser`, `UsbForensicsParser`, `BrowserForensicsParser`, `SrumParser`, `AmcacheShimcacheParser`, `LnkParserPlugin`, `PrefetchParserPlugin`, `ShimcacheParserPlugin`, `UserActivityParser`, `VolatilityBridgeParser`, `RdpBitmapCacheParser`, `CrossPlatformLogsParser`, `EvtxCarverEngine`, `M365EntraIdLogsParser`, `RegistryParser`, and the legacy `PrefetchParser` (same simple name as the wired `LogAnalyzer.Dfir.Windows.Parsers.PrefetchParser`).
- Services (`LogAnalyzer.Core/Services`): `DnsTunnelingClassifier`, `LivingOffTheCloudEngine`, `ProcessInjectionDetector`, `RansomwareDetectionEngine`, `SysmonCorrelationEngine`, `StixBundleExportService`, `CaseSnapshotService`, `AutomatedRuleGenerator`, `OfflineThreatFeedMatcher`, `SecurityEventIngestionService`.

Not edited, noted only:

- The workspace-root legacy tree (`Views/`, `ViewModels/`, `Services/`, `App.xaml.cs`, `MainWindow.xaml`, the root `*.csproj`) is not part of `LogAnalyzer.slnx` and is not built by `loganalyzer-dfir-build.yml`. Its `MainWindow.xaml` and `Views/DashboardView.xaml` still contain the static strings (`ALL SYSTEMS NORMAL`, `SHIELD ARMED & SECURE`, `EVIDENCE VAULT SECURED`) that WP1 removed from `LogAnalyzer.App`. They stay as they are until the owner decides wire-or-retire; they must not be shipped or presented as the product UI.
- `LogAnalyzer.Dfir.Cli` (`ladfir`) does not exist.
