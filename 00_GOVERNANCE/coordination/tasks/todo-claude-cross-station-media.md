# todo-claude-cross-station-media

STATUS: PR OPEN (awaiting review)
Task: LogAnalyzer media history across stations (owner decision 2026-10-16): each station exports a signed history of removable media, the security
officer reconciles the exports of all stations on the officer's station.
Branch: claude/loganalyzer-cross-station-media

## Done
- Core: MediaHistoryExport (canonical JSON loganalyzer.media_history.v1, build from devices + A15 exits + transfer rows + present-file hashes, strict parse),
  MediaHistoryVerify (CMS over exact bytes, pinned officer, chain + CRL, fail closed), CrossStationReconciliation (transfer at source / destination / files by
  SHA-256, medium on other stations, unregistered media, clone suspicion by time and attributes, stale register, missing stations, honest limits).
- Windows: CrossStationReportPdf (findings by severity, exports used / refused, limits, reserved signature field) + CSV.
- App: MediaHistoryFlows (sign with card, audit-first export, load + verify, signed report), control-page button "Exportă istoricul mediilor pentru ofițer",
  "Medii de stocare" tab "Confruntare între stații"; audit events media_history.exported / media_history.reconciled.
- Tests: Dfir CrossStationMediaTests (13), App CrossStationFlowTests (3, real SignedCms). Docs: EDITIONS_AND_POSTURE.md, GHID_DE_UTILIZARE.md §6.1.

## Next
- Owner review; lab test with a real card and 3-4 stations (transport by the officer's medium).

## Key files
- LogAnalyzer.Dfir.Core/Registers/MediaHistoryExport.cs, MediaHistoryReconciliation.cs
- LogAnalyzer.Dfir.Windows/Audit/CrossStationReportPdf.cs
- LogAnalyzer.App/Services/MediaHistoryFlows.cs, ViewModels/CrossStationViewModel.cs, ViewModels/StationControlViewModel.MediaHistory.cs
