# todo-claude-signed-media-register

STATUS: PR OPEN (awaiting review)
Task: LogAnalyzer signed media register (owner decision 2026-10-16): enrolment on the transfer station, officer card signature (detached CMS),
import with anti-rollback on every station, check D04, transfer-register linkage.
Branch: claude/loganalyzer-signed-media-register

## Done
- Research docs (standalone classified workstations, PDF signing library provenance) committed and allow-listed.
- Core: SignedMediaRegister (canonical JSON), SignedMediaRegisterImport (verify + anti-rollback + store), SignedMediaMatcher (multi-attribute identity,
  clone suspicion), SignedMediaEnrollment; ClassifiedAccess.SignedMedia (row approves only a registered medium).
- Windows: D04 in ControlEvaluator.SignedMedia.cs, applicability row, CheckGuide, StationFacts.ObservedMedia / SignedMedia, BitLocker volume id.
- App: MediaRegisterFlows (SignedCms sign / export / audit-first import), SignedMediaRegisterViewModel + "Registrul semnat" tab.
- Tests: Dfir SignedMediaRegisterTests (14), App SignedMediaRegisterFlowTests (4); annex regenerated; EDITIONS_AND_POSTURE.md.

## Next
- Owner review; lab test with a real qualified card and real USB media (VID/PID and capacity sources differ between history and live).

## Key files
- LogAnalyzer.Dfir.Core/Registers/SignedMedia*.cs
- LogAnalyzer.Dfir.Windows/Audit/ControlEvaluator.SignedMedia.cs
- LogAnalyzer.App/Services/MediaRegisterFlows.cs, ViewModels/SignedMediaRegisterViewModel.cs, Views/MediaCenterView.xaml
