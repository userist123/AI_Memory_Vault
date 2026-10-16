# Usability 2026-10-16 — click audit and in-place glossary

Owner rules: the fewest possible clicks per task; every term shown in the interface or in a report is explained where it
appears, in plain Romanian (the users are controllers of classified stations without IT training).

Method: the counts below were taken from the XAML views and the view-models (`HomeView`, `StationControlView`,
`MediaCenterView`, `InvestigationView`, `StationControlViewModel*.cs`, `InvestigationViewModel.cs`,
`SignedMediaRegisterViewModel.cs`, `LiveCase.cs`), not from a recording of a real session. A "click" is a mouse click on an
application control; a "dialog" is a window the user has to answer (file/folder dialogs, message boxes, the case-scope
window, the certificate picker). Typing is not counted. Windows' own PIN prompt for the smart card is listed apart: it is
outside the application and cannot be removed. Navigating inside a file dialog is counted as one click (it is at least one,
often several, when the dialog does not start in the right folder).

## Counts

| Flow | Before: clicks | Before: dialogs | After: clicks | After: dialogs | What changed |
|---|---|---|---|---|---|
| (a) Run the station control and get the report PDF | 6 (Home intent, Înainte, Înainte, Pornește controlul, Raport PDF, Save/Cancel in the copy dialog) | 1 every time (save-a-copy dialog) + case scope the first time in a session | 2 (Fă controlul stației, Deschide raportul) or Ctrl+Enter, Ctrl+O | 0 (case scope only when it is missing) | One-click control on Home with the defaults (since the last control, 90 days at the first one); the report is saved in the case and opened without a dialog; a copy elsewhere is the separate "Salvează o copie…" |
| (b) Officer signs the report with the card | 6 from a fresh start (4 to run the control, 1 Semnează, the report had to exist) — 1 when the control page is already open | certificate picker only when the card holds several qualified certificates; Windows PIN | 1 from the Home panel (Semnează ca ofițer) or Ctrl+Shift+S | same (picker only with several certificates; PIN) | The signature is offered in the same Home panel as the result; it is an authority action, so the card and the PIN stay |
| (c) Enter the paper anchor code of the previous control | 5 (Home intent, Înainte, Înainte, click into the field on step 3, Pornește controlul) | case scope the first time | 2 (click into the field on Home, Fă controlul stației) | case scope the first time | The anchor-code field is on Home next to the control button |
| (d) Export for the officer (report, log copy for the CD-R, media history) | about 15 for one medium: report copy (Raport PDF + save dialog with navigation ≈ 3), log copy (Copiază jurnalul pentru CD-R + folder dialog with navigation ≈ 3), media history (go to Medii, select the medium, Copiază datele, then paste by hand ≈ 3 per medium) plus 4 to run the control | 2 (save dialog, folder dialog) every time | 2 (Exportă pentru ofițer or Ctrl+E, then "Da" in the confirmation that shows the folder and the files) after the first time; 4 the first time (button + folder dialog + confirmation) | 1 (the confirmation, on purpose: classified-related data leaves the station) | One folder `Pentru_ofiter_<station>_<time>` with the report (and the signed report), the log copy for the CD-R, `istoric_medii.csv` and a `citeste_ma.txt`; the folder is remembered per Windows user ("Alege alt folder", or "Nu" in the confirmation, changes it); the whole bundle goes through `ExportAudit.RunBundle`: the intent lists every file before anything is written (refused as a whole if it cannot be written), then each written file is recorded with its SHA-256 |
| (e) Investigation from brought evidence, to the forensic report | 10 (Alte acțiuni, Analizează probe, Alege folderul, navigate + Select in the folder dialog, Înainte, Înainte, Pornește analiza, Raport de incident, Save) | 2–3 (folder dialog, case scope the first time, save dialog) | 8 (the folder dialog starts in the evidence folder used last time; choosing it starts the scan at once) | 2–3 (unchanged) | Folder memory for evidence (files, folder, intake); the intake goes to "Ce s-a găsit?" as soon as the folder is chosen; the informational message box "the report cannot be made yet" is now a line on the page |

Totals for the five flows: **before ≈ 42 clicks and 6–7 routine dialogs; after ≈ 15 clicks and 3–4 routine dialogs**
(the first-time folder and case-scope dialogs excluded on both sides).

## Review of PR #298

- The one-click control says its period before it runs ("Perioada controlului: …", with "Schimbă perioada" to the control page). „De la ultimul
  control” is no longer taken from the case folder (a planted folder could shorten it): with the paper anchor code it starts at the anchored entry of
  the application's log; otherwise at the last `control.finished` of the sealed audit (plain chain not broken); otherwise 90 days (`ControlPeriodStart`).
- Ctrl+O, Ctrl+Shift+S and Ctrl+E pass the same role gate as the Home panel.
- Every write to a folder taken from a folder picker or from the folder memory goes through `ExportAudit.Run`/`RunBundle` (officer export, CD-R copy of
  the log, signed media register); `ExportAuditTests` checks it for the whole App project, with an exact allowlist of pickers that only read or only choose.

## Dialogs kept on purpose

Kept because they are authority or irreversible actions, or the only place to choose a destination:

- the case scope window (why, period, systems, approver, authorising act): it is the authorisation of the control, asked once;
- the certificate picker and the Windows PIN prompt for the officer's qualified signature;
- the officer registration questions ("Ofițeri de securitate…": card or file, first officer fingerprint, signed request);
- "import the archive as unverified?" (integrity decision);
- "export refused" (the audit could not be written: a failure must be visible);
- file dialogs that pick a destination outside the case (save a copy, signed media register export, masked exports); they
  now start in the folder the same Windows user chose last time (`FolderMemory`, under `%LOCALAPPDATA%\LogAnalyzer\preferences\`).

Removed: the forced "save a copy" dialog after every report on the control page, and the informational message box on the
investigation report button.

## Keyboard shortcuts (anywhere in the main window)

| Keys | Action |
|---|---|
| Ctrl+Enter | Fă controlul stației (control-station role only) |
| Ctrl+O | Deschide raportul |
| Ctrl+Shift+S | Semnează ca ofițer |
| Ctrl+E | Exportă pentru ofițer |
| F1 | Ce înseamnă? (the glossary) |
| Alt+F / Alt+D / Alt+S / Alt+E | the same Home buttons through their underlined letter |

## Terms explained where they appear

- One catalogue, `TermCatalog` (Dfir.Windows/Audit): the existing glossary (`Glossary.Terms`, the jargon), a new list of
  words that are shown as they are but explained (`Glossary.Explained`: the results CONFORM, NECONFORM, DE VERIFICAT,
  NEDETERMINAT, NU SE APLICĂ, NECONFIRMAT; the severity words CRITICĂ, RIDICATĂ, MEDIE, SCĂZUTĂ, INFORMATIVĂ; proces-verbal, USB,
  PIN, SID, amprentă SHA-256) and one entry per control check id taken from `CheckGuides` (what is checked and why).
- New technical words: SACL, BitLocker To Go, BitLocker, PAdES, UAC, WMI, LDAP, UTC, DNS, MITRE ATT&CK, SAM, Event ID, VSS;
  aliases for cod de ancorare, semnătura calificată, CD-R, the enum names the grids bind (DeVerificat, NuSeAplica).
- Interface: `local:TermTip.Auto="True"` on the result and check-id cells of the control grids and on the Home result chips
  (the tooltip follows the text shown), `local:TermTip.Term="…"` for fixed labels, and `local:HelpButton` ("?") next to the
  anchor-code, officer-signature and certificate-fingerprint headings, which opens the glossary at that entry.
- Reports: the control report and the investigation report end with "Ce înseamnă termenii din acest raport", listing only
  the terms that appear in that report (whole-word match; capitalised spellings must match exactly).
- Guards: `UsabilityGlossaryTests` scans every XAML attribute text for check ids and for a list of known technical words and
  fails when one has no glossary entry; `TermCatalogTests` checks that every `CheckGuides` id, result word and severity word has
  an entry and that the report appendix lists only what the report prints.
