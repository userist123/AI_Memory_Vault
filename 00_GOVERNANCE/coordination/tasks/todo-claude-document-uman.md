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
- Next: independent review of the VBA part (same threat model list as vba.md section 2), then merge.
- Then: use on real owner documents; add lessons from each real use to SKILL.md / genuri.md.
- Not implemented: fillable PDF forms, Bates stamping, redaction, PDF/A and PDF/UA validation
  (veraPDF/PAC not installed), tags on merged PDFs.
- Requirements on the machine: python-docx, openpyxl, pymupdf, pypdf, pywin32, Microsoft Word + Excel.
