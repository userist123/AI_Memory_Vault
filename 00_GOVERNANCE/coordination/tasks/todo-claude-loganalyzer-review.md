# todo-claude-loganalyzer-review

- **Task:** make LogAnalyzer fully useful for air-gap station control and DFIR; close the 87-requirement comparison
  (docs/dfir/RESEARCH_2026-10-12_COMPARATIE_AIRGAP_LOGANALYZER_INSPECTRA.md).
- **Branch:** `claude/loganalyzer-dfir-roles-ui-0a71fb` (pushed; no PR). Scrub of the rest of the repo: PR #280 (open, owner merges).
- **Stage:** local testing only; licences (QuestPDF) and accreditation before institutional use.
- **Updated:** 2026-10-12.

## Done (pushed, all four test suites green at 967ddcd48)
- Senior review items, report usefulness (page 1 verdict, glossary, grammar), five false statements fixed.
- Event-log export fix (EvtExportLog flag), collector labels evidence with the real station name.
- Transfer register page, reconciliation, content verification, stick inspection, scope at start, Timesketch export.
- Policy explanations (129 settings), code/event explanations, check guides.
- Vetted libraries Registry + Serilog (pinned, lock file, notices); deleted registry traces, VBR volume serial links, MountPoints2 times.
- Station checks P32–P39, N06–N10, A09, A10, D02, D03, S04; air-gap rules (adapter kinds, optical, print, screenshots, BadUSB, VM, archivers, uploads, covert hardware, zones, lifecycle); report signing, verifiable archive, footprint, PII masking, history and after-connect rules, Collect-all.
- Two independent reviews and their fixes; unique check-id test; privacy guard test (local untracked identifier list).

## Next
- E6/E7/E8 (read the stick itself, ShellBags/Recycle Bin, deleted $MFT) — candidate libraries are vetted.
- Run on a real station as administrator and as a standard user; recompute the 87-row table from real output (kept local).
- Remaining partial rows: C8, D7, D12, D13, D14, E10, G3; B12 is manual by nature.

## Blockers / owner decisions
- Git history still contains old private data (history rewrite or private repo = owner's decision).
- Commit author name still shows the Windows user name (owner may switch it to the GitHub login).

## CI fixes on PR #284 (2026-10-15)
- Locked restore: `global.json` in the LogAnalyzer project pins SDK 10.0.301 exactly (`rollForward: disable`); the three workflows read it with `global-json-file`. The runner image already has 10.0.303, whose implicit ILLink pack version (10.0.11) differs from the lock files (10.0.9).
- Rule catalog: contracts added for MEDIA-HISTORY, MEDIA-USAGE, MEDIA-AFTER-CONNECT; the test now also discovers rule ids emitted through `Chan`/`Plain`/`NotEvaluated` and `*RuleId` constants.
- Storage-engine readability: 22 new plain documents are on the allowlist; the state card note count is updated.
- Gitleaks history scan: two old false positives allowlisted by exact value in `.gitleaks.toml` (no history rewrite).

## Classified folders: access and exits (2026-10-15, branch `claude/loganalyzer-classified-folder-audit`, on PR #287 head; not pushed)
- Done: `ClassifiedAccess` engine (Dfir.Core/Analysis), `Wp14Rules.Classified` (CLASSIFIED-* findings with catalog entries), station checks A10 (SACL
  covers read/write/delete, success and failure, inherited), A14 (sources: Removable Storage, Handle Manipulation, Process Creation + command line, print
  log, Detailed File Share off-Standalone), A15 (exits vs the transfer register), PDF section 3b, UI tab "Informații clasificate".
- Fix found on the way: `AuditPolicy.Query` never read "File System", so A10 could never be CONFORM.
- Next: RecentDocs / Office MRU / ShellBags and live Jump Lists are not read; run on a real station with auditing on; owner review of severities.
- Key files: LogAnalyzer.Dfir.Core/Analysis/ClassifiedAccess.cs, Wp14Rules.Classified.cs; LogAnalyzer.Dfir.Windows/Audit/ControlEvaluator.Classified.cs, ClassifiedFacts.cs.

## Partial comparison rows (2026-10-16, branch `claude/loganalyzer-partial-rows`, worktree `.claude/worktrees/la-partial-rows`)
- Owner order: (1) partial rows, (2) deleted $MFT (E8), (3) reading the stick (E6), (4) user activity (ShellBags, RecentDocs, Office MRU, Recycle Bin).
- Done: C8 → new check S06 (Defender signature updates, event 2000 + AntivirusSignatureVersion, vs register antivirus rows); D7 → IMAPI2 writer
  profiles (`Devices/ImapiRecorders.cs`, UNVERIFIED on a real writer: no optical drive here); D12 → container engines, Windows containers, virtual
  networks, bridge drivers in AIRGAP-VM-PRESENT; D13 → WinRAR list key time dates the most recent entry; D14 → thresholds via data pack `channels`
  (test), exact display; G3 → offer on the legacy "Avansat" pages (`NoDataPanel.PageItemCount`, not seen in the running window: licence screen).
- Tests: App 560, Dfir 1887 (+28 skipped), Edition 45, UI 353, all green.
- Open: E10 (admin-share collection) needs an owner decision: the remote path is built on "the application never opens a connection".
- Next: E8 (deleted $MFT entries, timestomping) on a new branch from main after this PR.
