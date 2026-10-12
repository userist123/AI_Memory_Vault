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
