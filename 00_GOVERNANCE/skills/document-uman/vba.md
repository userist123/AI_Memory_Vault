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
- runs programs or scripts: `Shell`, `WScript.*`, PowerShell, `cmd`, `mshta`, `rundll32`;
- touches the network: `URLDownloadToFile`, `XMLHTTP`, `WinHttp`, URLs in code;
- deletes files or folders (`Kill`, `RmDir`, `DeleteFile`) - outputs go to a folder the user chose;
  a temporary sheet the macro created itself may be deleted;
- writes the registry (`SaveSetting`, `RegWrite`), uses `SendKeys`;
- declares Windows API calls (`Declare PtrSafe`), unless a documented need is written on the same line
  as `' vba_ro:allow-declare <reason>` (then WARN, and the reviewer decides);
- runs by itself: no `AutoOpen`, `Auto_Open`, `AutoExec`, `AutoNew`, `AutoClose`, `Document_Open`,
  `Document_New`, `Workbook_Open`, `Workbook_BeforeClose`. Macros run only when the user starts them;
- hides things: no very hidden sheets, no code that edits VBA projects (`VBProject`, `CodeModule`),
  no `CallByName`/`ExecuteExcel4Macro`/`StrReverse`, no long `Chr()` chains or base64 strings,
  no password on the VBA project, no `GetObject`;
- calls `CreateObject` for anything except `Scripting.Dictionary`, `Scripting.FileSystemObject`,
  `Word.Application`, `Excel.Application`.

Files the macro opens (sources in a folder) are opened read-only with
`Application.AutomationSecurity = msoAutomationSecurityForceDisable` (3), so their own macros cannot
run, and `UpdateLinks:=0` in Excel. That property is a runtime setting of the current session, restored
in the cleanup block - it is not the macro security level, which a macro never changes.

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
  so `ș ț ă î â` turn into garbage. Write Romanian without diacritics in code, or build the few
  user-facing strings with `ChrW` in one helper.
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
- Fields: update twice (the second pass fixes page numbers moved by a longer TOC); TOCs need
  `TablesOfContents(i).Update`; broken references show "Error! Reference source not found." - count them
  and report.

## 5. Templates (`vba_templates/`, all tested 2026-11-22 in Word/Excel 16)

| Template | Entry (owner) | Worker (tests, other code) | Does |
|---|---|---|---|
| Excel_Consolidare | `Consolidare` | `ConsolideazaFolder(folder, foaie, inlocuieste)` | all workbooks in a folder -> one sheet + "Fisier sursa"; skips files with other columns |
| Excel_RapoartePDF | `RapoartePDF` | `GenereazaRapoarte(camp, campSuma, folder, prefix, suprascrie)` | one PDF per distinct value (apartment, client) with its rows and a total |
| Excel_CurataDate | `CurataDate` | `CurataTabel(coloaneSume, coloaneDate, eliminaDuplicate, inlocuieste)` | trims, nbsp, "1.234,56" -> number, "05.03.2026" -> date, duplicates, "Erori" column |
| Excel_Restante | `CalculRestante` | `CalculeazaPenalitati(inlocuieste)` | days late, penalty per day % with cap %, totals; input sheet "Parametri" with named ranges |
| Word_StiluriLot | `StiluriLot` | `AplicaStiluriFolder(model, sursa, dest, suprascrie)` | styles from a model document onto every document in a folder, ro-RO proofing |
| Word_InlocuireLot | `InlocuireLot` (pairs from a table in the macro document) | `InlocuiesteInFolder(sursa, dest, perechi, majuscule, formatNou, suprascrie)` | find/replace list across a folder, all stories, optional bold/highlight, counts |
| Word_CorespondentaPDF | `CorespondentaPDF` (active main document) | `ImbinaFisiere(principal, registru, foaie, camp, dest, prefix, suprascrie)` | mail merge, one PDF per record, named from a column |
| Word_ActualizeazaCampuri | `ActualizeazaCampuri`, `ActualizeazaFolder` | `ActualizeazaFisiere(sursa, dest, suprascrie)` | update all fields/TOC/TOF in all stories, report field errors |

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
reads `HKCU\Software\Microsoft\Office\16.0\<Word|Excel>\Security\AccessVBOM`, writes a backup to
`%TEMP%\vba_ro_vbom_backup.json`, sets 1 only while Office runs, and restores the original value
(deletes it if it was absent) in `finally` and at exit. After a crash: `vba_ro.py restaureaza-vbom`;
check with `vba_ro.py stare-vbom` or `reg query`. Never change VBAWarnings, the macro security level or
Trusted Locations. `ruleaza` does not need AccessVBOM. Office runs as a private invisible instance; a
watchdog kills only that process if a dialog blocks it.

Testing procedure (every macro, before delivery):
1. `verifica` PASS (fix every FAIL, read every WARN).
2. Synthetic data that includes the hard cases: duplicates, text numbers with comma, invalid dates,
   unsafe characters in names ("14/B"), a file with other columns, an existing output file.
3. Build the .xlsm/.docm, call the worker through COM, check the outputs from outside: values read back,
   files produced, PDFs opened (page count, text of the right record only), sources unchanged, second
   run skips existing files, bad input returns "EROARE: ...".
4. `exporta` the delivered file and run `verifica` on the exported modules (what ships is what passed).
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
