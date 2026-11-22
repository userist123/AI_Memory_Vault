# todo-claude-document-uman

- Task: skill `document-uman` - human-looking professional Romanian documents (Word, Excel, PDF), from
  short letters to complex reports, workbooks and merged PDF dossiers.
- Branch/PR: claude/document-uman-skill (this PR).
- Done (2026-11-22): rules distilled from expert tutorials, official writing guides, Romanian legal
  texts and three rounds of blind review; builders docx_ro, docx_complex, xlsx_ro, pdf_ro, cifre_ro;
  checker check_doc.py; renderer render_doc.py (Word/Excel COM -> PDF -> PNG).
- Install: the local Claude Code skill folder is a directory junction to
  `00_GOVERNANCE/skills/document-uman` in a vault checkout that tracks main.
- Done (2026-11-22, VBA part): `vba.md` (when VBA vs Python, security and professional rules, gotchas,
  recipes, build/test/delivery), `vba_ro.py` (static check, build with AccessVBOM guard, run, export),
  8 templates in `vba_templates/` tested end-to-end in Word/Excel 16 on synthetic data; static check PASS.
- Done (2026-11-23, VBA fix round after the independent Opus review, 8 findings): Office starts with
  AutomationSecurity 3 before any file opens; `construieste` re-checks the exported built project
  (incl. --baza code) before saving; `ruleaza` checks then runs; AccessVBOM guard rewritten (O_EXCL
  lock with PID + start time, one backup per app, exact type/absence/key restore, no exception
  masking, dead-run recovery); templates skip ThisDocument/ThisWorkbook and open files, lock external
  fields (INCLUDEPICTURE/LINK/DDE...), half-up R2(), "1.500" flagged ambiguous, PDF name dedupe and
  Chr(0-31) strip, RO() helper for diacritics in cells (dialogs stay ASCII - documented decision);
  checker: continuations joined, ~30 new rules. Tests: `20_TESTS/test_document_uman_vba.py`
  (52 FAIL probes, 7 WARN probes, 8 templates PASS, 16 guard tests on a scratch HKCU key); live
  end-to-end 64/64 checks in Word/Excel (harness kept outside the repo).
- Done (2026-11-23, final fix round, no further review): lock/backup moved from `%TEMP%` to the fixed
  `%LOCALAPPDATA%\vba_ro` (Known Folder API; old TEMP location read once by `restaureaza-vbom`, never over a
  live lock); recovery race: the loser of the lock rename waits (bounded) for the other recoverer, backup
  deleted only if byte-identical; checker: AutomationSecurity literal 3 only, Office CreateObject WARN, XLM,
  startup persistence, network/UNC after concat normalisation, event handlers, OLE (46 new probes);
  `Word_CorespondentaPDF` carries the `vba_ro:allow-mailmerge` marker. 150 tests pass, hygiene FAILURES=0,
  live Excel + Word template rebuild/run OK, AccessVBOM unchanged (absent before and after).
- Next: main session verifies and merges (no further review round).
- Residuals: the checker is a heuristic pattern filter (names built across variables/statements can pass;
  backstop = AutomationSecurity 3 at runtime, see vba.md section 2);
  Word may fetch an attached template or linked picture from a UNC path when a hostile document opens.
- Then: use on real owner documents; add lessons from each real use to SKILL.md / genuri.md.
- Not implemented: fillable PDF forms, Bates stamping, redaction, PDF/A and PDF/UA validation
  (veraPDF/PAC not installed), tags on merged PDFs.
- Requirements on the machine: python-docx, openpyxl, pymupdf, pypdf, pywin32, Microsoft Word + Excel.
