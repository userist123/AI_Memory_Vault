# todo-claude-print-research
STATUS: IN_PROGRESS        UPDATED: 2026-10-08
TASK: Research WP16a print tracking (decision 26) -> 02_PRODUCT/projects/workspaces/loganalyzer-dfir/docs/dfir/PRINT_TRACKING_RESEARCH.md (Romanian), sections A-F + implementare + Neverificat.
BRANCH / PR: none (research only, no commit)    BASE: working copy
SPEC: 02_PRODUCT/projects/workspaces/loganalyzer-dfir/docs/dfir/CONTRACT_AUDIT_STAGE1.md §8 decision 26
DONE:
- checkpoint created
- A: events 307/805 via MS-RPRN spec, wevtutil, ShowJobTitleInEventLogs GPO verified; 308/309/310/372/800/801/812/842 only from vendor docs (PaperCut etc.)
- B: SHD layout from kacos2000 WinHex tpl; KEEPPRINTEDJOBS attr (PRINTER_INFO_2); licences: pclbox Apache-2 (Java PCL5), pcl-parser Apache-2 (Java PCL5), pclparaphernalia Unlicense (C# PCL5+XL analyser), PDFsharp MIT, PdfPig Apache, QuestPDF dual
- C: RFC8011/3805/2707 downloaded to scratchpad print_samples/rfc, quotes verified
- scratch notes in scratchpad print_samples/ (lic/, rfc/, tpl/)
NEXT (in order):
1. A event logs 2. B spool/converters/licences 3. C SNMP/IPP/state model 4. D MFP vendors 5. E samples (scratchpad/print_samples) 6. F regex 7. final doc + report
BLOCKERS: none
KEY FILES: 02_PRODUCT/projects/workspaces/loganalyzer-dfir/docs/dfir/PRINT_TRACKING_RESEARCH.md (output, written incrementally)
VERIFICATION: n/a (research)
