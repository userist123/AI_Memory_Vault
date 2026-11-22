# VBA macros for Word and Excel (document-uman)

Distilled on 2026-11-22 from expert tutorials (Excel Macro Mastery, WiseOwl, Leila Gharani, mail-merge and
PDF-export tutorials), the Microsoft Learn VBA reference and the Rubberduck / Excel Macro Mastery style
guides. Then checked by running every template in real Word and Excel.

Files: `vba_ro.py` (static check, build, run, export), `vba_templates/*.bas` (8 tested templates).

## 1. VBA or Python?

| The owner needs... | Use |
|---|---|
| a finished document now (letter, report, workbook, PDF) | Python builders (`docx_ro`, `xlsx_ro`, `pdf_ro`) |
| to re-run the job himself in Office, every month or on new files | VBA in a .xlsm/.docm |
| batch work on his own files: 50 contracts to restyle, find/replace across a folder, one PDF per apartment, consolidating monthly workbooks | VBA |
| a mail merge he edits in Word and re-runs | VBA (Word_CorespondentaPDF) |
| complex logic, web data, many file types | Python, not VBA |

Rule of thumb: VBA when the person who presses the button is the owner, inside Office, without Python.

## 2. Security rules (blocking - `vba_ro.py verifica` FAILs them)

A macro written by this skill never:
- runs programs or scripts: `Shell` (in any form - `Shell x`, `VBA.Shell`, `Interaction.Shell`),
  `WScript.*`, `Shell.Application`, `.Run` on an object or `Application.Run` with a computed name
  (`Application.Run "Literal"` is a WARN), PowerShell, `cmd`, `mshta`, `rundll32`, `schtasks`;
- executes text as code: `Evaluate`, `[ ]` evaluation (`[A1]`, `[CALL(...)]`), `ExecuteExcel4Macro`,
  Excel 4 macro sheets, `CallByName`, `MacScript`;
- schedules or hooks itself: `OnTime`, `OnKey`, `OnEntry`; installs add-ins (`AddIns.Add`,
  `.Installed`); writes to the Startup folders, `XLSTART`, `PERSONAL.XLSB`, `Normal.dotm` /
  `NormalTemplate` or uses `StartupPath`;
- touches the network: `URLDownloadToFile`, `XMLHTTP`, `WinHttp`, URLs in code, UNC paths
  (`\\server\share` - also leaks the Windows password hash), `DDE*`, `FollowHyperlink`, `WEBSERVICE`;
- deletes, renames or moves files (`Kill`, `RmDir`, `DeleteFile`, `Name ... As`, `.Delete` on a
  FileSystemObject File/Folder) - outputs go to a folder the user chose; `.Delete` on a sheet the macro
  created itself is a WARN, as are `FileCopy`/`CopyFile`, `Open ... For Output/Append` and
  `CreateTextFile` (check path and overwrite rule);
- writes the registry (`SaveSetting`, `RegWrite`), uses `SendKeys`;
- declares Windows API calls (`Declare PtrSafe`), unless a documented need is written on the same line
  as `' vba_ro:allow-declare <reason>` (then WARN, and the reviewer decides);
- runs by itself: no `AutoOpen`, `Auto_Open`, `AutoExec`, `AutoNew`, `AutoClose`, `Document_Open`,
  `Document_New`, `Workbook_Open`, `Workbook_BeforeClose`, ActiveX events that fire on open
  (`*_Painted`, `*_GotFocus`, `*_Layout`...). Macros run only when the user starts them;
- lowers `AutomationSecurity` to 1 (that lets the macros of files it opens run);
- hides things: no very hidden sheets (`xlSheetVeryHidden`, `xlVeryHidden`, `Visible = 2`), no code that
  edits VBA projects (`VBProject`, `CodeModule`, `.VBE`), no `StrReverse`, no `ChrB`, no chains of
  three `Chr()` calls and no 8 or more `Chr()` calls in a module (use one helper such as `RO()`), no
  base64 strings, no password on the VBA project, no `GetObject`;
- calls `CreateObject` for anything except a complete literal `"Scripting.Dictionary"`,
  `"Scripting.FileSystemObject"`, `"Word.Application"`, `"Excel.Application"` (a concatenated or
  computed ProgID FAILs).

The checker removes comments, joins `" _"` line continuations before matching (so a keyword split over
two lines is still seen) and scans string literals for names and paths. It is a pattern filter, not a
VBA parser: a determined author can still hide a call (for example by building a ProgID from pieces
the rules do not list). The reviewer reads every module; the checker only catches the known shapes.
`20_TESTS/test_document_uman_vba.py` holds one probe per rule (each must FAIL or WARN) and checks
that the 8 templates PASS.

Files the macro opens (sources in a folder) are opened read-only with
`Application.AutomationSecurity = msoAutomationSecurityForceDisable` (3), so their own macros cannot
run, and `UpdateLinks:=0` in Excel. That property is a runtime setting of the current session, restored
in the cleanup block - it is not the macro security level, which a macro never changes.
Word templates also set `Options.UpdateLinksAtOpen = False` while they open other documents (restored
in cleanup), so LINK fields do not refresh on open.

Files already open in Office - the document/workbook that holds the macro (`ThisDocument`,
`ThisWorkbook`) and anything the user has open - are never reopened or closed by a batch macro: they
are skipped and listed in the result ("Sarite (deschise acum ...)"). Excel also skips a file whose name
equals an open workbook (Excel cannot open two workbooks with the same name).

Fields that pull content from outside the document (`INCLUDEPICTURE`, `INCLUDETEXT`, `INCLUDE`,
`IMPORT`, `LINK`, `DDE`, `DDEAUTO`, `DATABASE`, also when nested in an `IF`) are never updated in
documents the macro did not write: `Word_ActualizeazaCampuri` locks them before `Fields.Update`,
unlocks afterwards only those it locked, and reports how many it left alone. Updating them would read
`\\server\...` paths (sending the user's NTLM hash) or start other programs. Residual: Word itself may
fetch an attached template or a linked picture from a UNC path when a hostile document opens; keep
batch folders to documents the owner trusts.

Checker rules added in the last round (all FAIL unless noted): any `AutomationSecurity` assignment other
than the literal 3 (the only exception is restoring a variable the same procedure read from
`.AutomationSecurity` while it also sets 3); `CreateObject("Excel.Application"/"Word.Application")` is a
WARN unless `AutomationSecurity = 3` follows in the same procedure (a new instance defaults to Low); XLM
(`EXEC(`/`CALL(`/`REGISTER(` in strings, `Sheets.Add` Type 3/4, `Names.Add "Auto_..."`); startup persistence
(`XLSTART`, `Startup`, `Start Menu`, `wdStartupPath`, `Options.DefaultFilePath(`), also when the path is
split with `" & "` joins; UNC paths also via `Chr(92) & Chr(92)`; `.Follow`, `QueryTables.Add`,
`Connections.Add`, `Fields.Add` with INCLUDE/LINK/DDE, `MailMerge.OpenDataSource` (allowed only with
`' vba_ro:allow-mailmerge <need>` on the same line, then WARN), `Documents.Add Template:=` non-literal
(WARN); any `Workbook_*`/`Worksheet_*`/`Document_*`/`App_*` Sub or `WithEvents` (macros start only from
an explicit entry Sub); `AddOLEObject`.

Residual risk, stated plainly: the checker is a heuristic text filter. A string or name built from pieces
across statements or variables, or from data read at run time, can pass it. The backstop is the runtime:
Office starts with `AutomationSecurity = 3`, so macros of files opened by the build/export steps cannot
run, `ruleaza` runs only a file that passed the check, and the owner reads what the macro does before
using it on documents he does not trust.

## 3. Professional rules (FAIL/WARN in the checker where testable)

- `Option Explicit` in every module. Explicit types (`As Long`, `As String`), never `Dim x As New`.
- Header comment (Romanian, for the owner): `Scop:`, `Rulare:`, `Modifica:` (what it changes and what
  it never changes), `Versiune:`.
- Two layers. A public entry Sub without arguments (what the owner runs from Alt+F8 or a button) does
  only the dialogs: folder picker, InputBox, confirmation, final `MsgBox`. It calls a public worker
  Function that takes everything as arguments, shows no dialog and returns `"OK: ..."` or
  `"EROARE: ..."`. The worker is what tests call through COM (a MsgBox would block an automated run).
- Every entry Sub and worker has `On Error GoTo Eroare`, an `Exit Sub/Function` before the label, and a
  `Curatare:` block that restores every Application setting it changed (`ScreenUpdating`,
  `EnableEvents`, `Calculation`, `DisplayAlerts`, `AutomationSecurity`, `PrintCommunication`). The
  error path does `Resume Curatare`, so the restore runs on both paths. Save the original value first
  (`calc = Application.Calculation`) and restore that, not a hard-coded default.
- Romanian user messages, clear and specific: what failed and on which file/row. Error text from
  `Err.Description` is added, never shown alone.
- Validate inputs before changing anything: folder exists, sheet/column exists, numbers are numbers,
  dates are dates, output folder differs from source folder.
- Never overwrite without consent: batch macros write copies to an output folder; an existing output
  file is skipped unless the user answered Yes to "Suprascriu?"; a result sheet that already exists is
  replaced only with confirmation (`inlocuieste` argument).
- No `Select`, `Activate`, `Selection` - work with object variables (`Set ws = ...`, `Set doc = ...`).
- Names: constants for sheet names, header names and settings (`Private Const FOAIE_DATE As String =
  "Date"`), named ranges for user inputs (`DataCalcul`), columns found by header text, not by letter.
  Literal cell addresses (`Range("B4")`) get a WARN.
- `On Error Resume Next` only for one statement (closing a file in cleanup), followed by
  `On Error GoTo 0`.
- Source text in ASCII: the VBA editor imports .bas files in the ANSI code page (1252 on most machines),
  so `ș ț ă î â` typed into the source turn into garbage. Text written to cells, documents or UserForms
  goes through the `RO()` helper with escape tokens: `RO("Fi{s,}ier surs{a~}")` gives "Fișier sursă"
  (`{a~} {A~}` ă Ă, `{a^} {A^}` â Â, `{i^} {I^}` î Î, `{s,} {S,}` ș Ș, `{t,} {T,}` ț Ț; one `ChrW` call
  over a code table, so the Chr rule stays quiet). Cells, document text and UserForm captions are
  Unicode and show the letters correctly (tested: headers read back as "Zile întârziere",
  "Fișier sursă"; the PDF title as "Situație pentru: 12").
- Dialogs (`MsgBox`, `InputBox`) stay without diacritics. Decision (2026-11-23): VBA passes their text
  through the ANSI code page; tested in Excel 16 on code page 1252, `StrConv(..., vbFromUnicode)` - the
  conversion MsgBox applies - turns "șțăîâ ȘȚĂÎÂ" into "??aîâ ??AÎÂ". Even on a Romanian (1250) PC only
  the cedilla forms survive. A Unicode `MessageBoxW` via `Declare PtrSafe` would work but adds an API
  declaration to every delivered file and cannot be tested without a person clicking the dialog; plain
  letters in dialogs are the safe default. A UserForm is the option when a dialog must show diacritics.
- Money: VBA `Round` rounds half to even (`Round(0.125, 2)` = 0.12). Amounts to pay use a half-up helper
  `R2(x) = Application.WorksheetFunction.Round(x, 2)` (Excel's ROUND: 0.13), as in `Excel_Restante`.
- English identifiers are fine; comments and messages in Romanian.

Performance (measured in the tutorials: 10x-1000x):
- Read a block once into an array (`d = ws.Cells(1, 1).CurrentRegion.Value`), work in memory, write the
  result once with `.Resize(UBound(a, 1), UBound(a, 2)).Value = a`. Never loop over cells.
- `Scripting.Dictionary` for "distinct keys", "sum per key", "seen before" (test `.Exists` first -
  reading a missing key adds it).
- ScreenUpdating/EnableEvents off, Calculation manual during the work; `PrintCommunication = False`
  around PageSetup.
- Value assignment (`dest.Value = src.Value`) instead of Copy/Paste; built-in bulk features
  (AdvancedFilter, `RemoveDuplicates`, Find/Replace) when they do the job.
- Late binding (`CreateObject("Scripting.Dictionary")`, `As Object`) in delivered files: no reference to
  tick, works on any Office version.

## 4. VBA gotchas found while testing the templates

- `Dir()` has one global enumeration. A `Dir(dest & fis)` call (the "file exists?" check) inside a
  `Do While` over `Dir()` resets it: the next `Dir()` raises "Invalid procedure call". Read the file list
  into a Collection first (`ListaFisiere` in the Word templates), then process it.
- VBA `Or`/`And` do not short-circuit: `If UBound(a, 2) <> n Or Not Same(a) Then` still calls `Same`
  and can crash. Split the test or check inside the helper.
- `CStr(0.2)` uses the Windows decimal separator ("0,2" on a Romanian PC) and `Val` reads only a point,
  so `Val(CStr(x))` can give 0. Read numbers with `CDbl(cell.Value2)` after `IsNumeric`.
- Dates can carry a time fraction (23:00 the day before). Use `Int(CDate(x))` before subtracting days.
- `CurrentRegion.Value` of a single cell is a scalar, not an array: test `IsArray`.
- After `MailMerge.Execute` the new document is the `ActiveDocument` (Execute returns nothing); capture
  it immediately in a variable, export, close without saving.
- Word: the StoryRanges loop needs `NextStoryRange` to reach every header/footer and linked text box:
  `For Each s In doc.StoryRanges: Do ... Set s = s.NextStoryRange: Loop Until s Is Nothing: Next`.
- Counted replace: `Do While r.Find.Execute(Replace:=wdReplaceOne): n = n + 1: r.Collapse wdCollapseEnd:
  Loop` with `Wrap = wdFindStop`; works even when the new text contains the old one ("ab" -> "abab").
- Excel converts text written through `.Value` as if it were typed: "1.500" becomes 1.5 and
  "05.03.2026" may become a date. A value the macro could not (or must not) convert is written with a
  leading apostrophe (`"'" & v`) so it stays text, next to its error note.
- Word `Application.Run "Modul.Macro"` looks in the active document's project. When a test opens other
  documents, open them before the macro document (or call the worker from the macro document).
- Word `Fields.Add` updates the new field immediately; to test "not refreshed" change the source after
  inserting the field.
- Fields: update twice (the second pass fixes page numbers moved by a longer TOC); TOCs need
  `TablesOfContents(i).Update`; broken references show "Error! Reference source not found." - count them
  and report.

## 5. Templates (`vba_templates/`, tested in Word/Excel 16 on 2026-11-22, re-tested 2026-11-23 after the security review)

| Template | Entry (owner) | Worker (tests, other code) | Does |
|---|---|---|---|
| Excel_Consolidare | `Consolidare` | `ConsolideazaFolder(folder, foaie, inlocuieste)` | all workbooks in a folder -> one sheet + "Fișier sursă"; skips files with other columns and workbooks open in Excel |
| Excel_RapoartePDF | `RapoartePDF` | `GenereazaRapoarte(camp, campSuma, folder, prefix, suprascrie)` | one PDF per distinct value (apartment, client) with its rows and a half-up total; colliding names ("14/B", "14_B") get _2 |
| Excel_CurataDate | `CurataDate` | `CurataTabel(coloaneSume, coloaneDate, eliminaDuplicate, inlocuieste)` | trims, nbsp, "1.234,56" -> number, "05.03.2026" -> date, duplicates, "Erori" column; "1.500" is ambiguous: left as text and flagged |
| Excel_Restante | `CalculRestante` | `CalculeazaPenalitati(inlocuieste)` | days late, penalty per day % with cap %, totals rounded half-up; input sheet "Parametri" with named ranges |
| Word_StiluriLot | `StiluriLot` | `AplicaStiluriFolder(model, sursa, dest, suprascrie)` | styles from a model document onto every document in a folder, ro-RO proofing |
| Word_InlocuireLot | `InlocuireLot` (pairs from a table in the macro document) | `InlocuiesteInFolder(sursa, dest, perechi, majuscule, formatNou, suprascrie)` | find/replace list across a folder, all stories, optional bold/highlight, counts |
| Word_CorespondentaPDF | `CorespondentaPDF` (active main document) | `ImbinaFisiere(principal, registru, foaie, camp, dest, prefix, suprascrie)` | mail merge, one PDF per record, named from a column |
| Word_ActualizeazaCampuri | `ActualizeazaCampuri`, `ActualizeazaFolder` | `ActualizeazaFisiere(sursa, dest, suprascrie)` | update all fields/TOC/TOF in all stories, report field errors; external fields (INCLUDEPICTURE, LINK, DDE...) locked, not updated |

Adapting a template: change the constants and header names first; keep the two layers, the
validation, the cleanup block and the overwrite rule. Run `verifica` after every change.

## 6. Recipes (snippets - adapt, then test like a template)

Refresh pivots and charts (Excel):
```vba
Dim pc As PivotCache, ws As Worksheet, co As ChartObject
For Each pc In ThisWorkbook.PivotCaches
    pc.Refresh
Next pc
For Each ws In ThisWorkbook.Worksheets
    For Each co In ws.ChartObjects
        co.Chart.Refresh
    Next co
Next ws
```
Prefer `ThisWorkbook.RefreshAll` when queries also need refreshing; never hook a refresh to
`SelectionChange` (runs on every click).

Tables (ListObjects): read `lo.DataBodyRange.Value` into an array (about 12x faster than ListRows);
add rows by resizing the table (`lo.Resize`) and writing the block once.

Excel range -> Word table (late binding, from Excel):
```vba
Dim wd As Object, doc As Object, d As Variant, t As Object, i As Long, j As Long
d = ThisWorkbook.Worksheets(FOAIE_DATE).ListObjects(1).Range.Value
Set wd = CreateObject("Word.Application")
Set doc = wd.Documents.Add(Template:=caleModel)        ' a model with the styles
Set t = doc.Tables.Add(doc.Content.Paragraphs.Last.Range, UBound(d, 1), UBound(d, 2))
For i = 1 To UBound(d, 1)
    For j = 1 To UBound(d, 2)
        t.Cell(i, j).Range.Text = Format$(d(i, j))
    Next j
Next i
t.Rows(1).HeadingFormat = True                           ' header repeats on each page
doc.SaveAs2 caleIesire                                   ' after the Dir() check / confirmation
doc.Close False: wd.Quit                                 ' in the cleanup block too
```
For many rows build the text with tabs and use `Range.ConvertToTable` (one call instead of cells).

Content-control form (Word): give each control a Tag, read/write by tag, never by position:
```vba
Dim cc As ContentControl
For Each cc In doc.SelectContentControlsByTag("NumeClient")
    cc.Range.Text = numeClient
Next cc
```
Lock the template with `doc.Protect wdAllowOnlyFormFields` only without a password, or not at all.

Batch styles in one document: map direct formatting to styles by searching with
`Find.Font.Bold = True / .Size = 16` and setting `.Replacement.Style = doc.Styles(wdStyleHeading1)`.

Mail merge to individual Word files: same loop as Word_CorespondentaPDF with
`rezultat.SaveAs2 fis, wdFormatXMLDocument` instead of `ExportAsFixedFormat`.

PDF export parameters: Excel `ExportAsFixedFormat Type:=xlTypePDF, Quality:=xlQualityStandard,
IncludeDocProperties:=False, IgnorePrintAreas:=False, OpenAfterPublish:=False`; Word
`ExportFormat:=wdExportFormatPDF, OptimizeFor:=wdExportOptimizeForPrint, IncludeDocProps:=False,
CreateBookmarks:=wdExportCreateHeadingBookmarks, DocStructureTags:=True`.

## 7. Building, testing, delivering

Build (the static check runs first and blocks a failing module):
```
python -P -E <skill dir>\vba_ro.py verifica <module.bas> [...]
python -P -E <skill dir>\vba_ro.py construieste out.xlsm Modul.bas --baza date.xlsx
python -P -E <skill dir>\vba_ro.py ruleaza out.xlsm Modul.Worker arg1 arg2
python -P -E <skill dir>\vba_ro.py exporta out.xlsm <folder>       (review what is really inside)
```
`construieste` and `exporta` need "Trust access to the VBA project object model". `vba_ro.acces_vbom`
guards `HKCU\Software\Microsoft\Office\16.0\<Word|Excel>\Security\AccessVBOM`:
- one run per app at a time: an exclusive lock file `%LOCALAPPDATA%\vba_ro\<App>.lock` (a fixed per-user
  folder from the Windows Known Folder API, not `%TEMP%`, so runs with different TEMP values exclude
  each other; fallbacks in order: the `LOCALAPPDATA` variable, `<home>\AppData\Local`, the temp folder
  only if none exists; created with
  O_EXCL, holds the owner PID and its process start time). A second run while the owner is alive is
  refused with "in use by another vba_ro run (PID n)"; a lock whose owner is dead (crash, kill) is
  moved aside atomically and its backup restored before the new run starts. A run that lost the
  rename race waits (at most 10 s) for the other recoverer, finishes the work itself if that recoverer
  died, and otherwise refuses; a backup is deleted only if it is byte-identical to the one restored;
- the backup `%LOCALAPPDATA%\vba_ro\<App>.backup.json` records the exact original state: which keys
  existed, whether the value existed, its registry type (DWORD or REG_SZ...) and data. The guard sets
  DWORD 1 only during the build and restores that state - same type, value deleted if it was absent,
  Security key deleted again if the guard created it - then checks the result;
- an exception inside the build is never replaced by a restore error: the restore problem is printed
  and the backup kept for `vba_ro.py restaureaza-vbom`, which leaves a live run alone and also restores,
  once, backups that versions 1-2 left under `%TEMP%\vba_ro_vbom` (never while a live lock exists).
Check with `vba_ro.py stare-vbom` or `reg query`. Never change VBAWarnings, the macro security level or
Trusted Locations.

Office runs as a private invisible instance with `AutomationSecurity = 3` set right after it starts,
before any file opens: `Workbook_Open`/`Document_Open`/`Auto_Open` of a --baza file cannot run while
AccessVBOM is 1 (tested: a --baza workbook whose `Workbook_Open` writes a cell did not run; the same
file opened with macros enabled did). `construieste` exports the whole built project (imported modules
and any code that came with --baza; empty document modules are skipped) and runs the static check
before saving: a FAIL aborts the build and nothing is written. `ruleaza` first exports and checks the
file with macros disabled, refuses it on FAIL, then reopens it with macros enabled (Excel with
`EnableEvents = False` while opening) to run the macro; `verifica` also accepts a .xlsm/.docm and checks
its exported modules. A watchdog kills only the Office process it started if a dialog blocks it.

Testing procedure (every macro, before delivery):
1. `verifica` PASS (fix every FAIL, read every WARN).
2. Synthetic data that includes the hard cases: duplicates, text numbers with comma, invalid dates,
   unsafe characters in names ("14/B"), a file with other columns, an existing output file.
3. Build the .xlsm/.docm, call the worker through COM, check the outputs from outside: values read back,
   files produced, PDFs opened (page count, text of the right record only), sources unchanged, second
   run skips existing files, bad input returns "EROARE: ...".
4. `verifica out.xlsm` (exports and checks what is really inside; `construieste` already did this).
   After changing `vba_ro.py` or a template: `python -m pytest 20_TESTS/test_document_uman_vba.py`.
5. Open the delivered file once in visible Office and run the entry Sub by hand when dialogs changed.

Delivery to the owner:
- The .xlsm/.docm (macros inside) and the .bas modules for import (VBA editor: Alt+F11 > File >
  Import File, then save as .xlsm/.docm). For Word macros used on many documents, a .dotm in the
  Startup folder or the Normal template is an option the owner chooses.
- A short Romanian note: what it does, how to run it (Alt+F8 > name, or a button), what it changes and
  what it never changes, where the results go.
- Macro security note: a downloaded or e-mailed file is blocked (Mark of the Web) - Properties >
  Unblock, or keep it in a folder the owner trusts. "Enable Content" only for files he made or received
  from this skill. Signing with a self-signed certificate (SelfCert.exe) is optional; it does not make
  the file trusted on other PCs.
- Run `check_doc.py` on any document or workbook the macro produces, as for any delivered document.
