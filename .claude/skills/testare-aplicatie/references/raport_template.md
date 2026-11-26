# Test report – <APP NAME>

## Run manifest
- **Data:** <DD.MM.YYYY HH:MM>
- **Build:** <installer/exe name + version + sha256>
- **Machine / OS:** <OS build>, resolution <WxH>, DPI scaling <100/125/150%>
- **Data snapshot:** <db/config path + sha256 of the backup taken before the run>
- **Driver used:** <pywinauto / FlaUI / Playwright / PowerShell lib_ui>
- **Data safety:** <backed up before run? ran on a copy/VM? restored after?> — state what you actually did, do not pre-claim "not modified".

## Coverage
Functions discovered: <N>  ·  exercised: <M>  ·  PASS <a> / FAIL <b> / BLOCKED <c> / SKIPPED <d> / NOT RUN <e>

## Results

| # | Function | Precondition | Expected (oracle) | Observed | Status | Evidence |
|---|----------|--------------|-------------------|----------|--------|----------|
| T01 | <function> | <state before> | <checkable expected result> | <what happened> | PASS/FAIL/BLOCKED/SKIPPED | <screenshot/file> |

Status legend (ISTQB): **PASS** observed matches the oracle · **FAIL** deviates (defect, logged) ·
**BLOCKED** cannot execute due to missing hardware/peripheral/server/prerequisite or an upstream
crash — NOT an app defect · **SKIPPED** out of scope · **NOT RUN** not reached. Never PASS/FAIL a case
you could not actually execute; a screenshot alone is evidence, not the verdict.

## Fixes confirmed
- <what was broken before> – FIXED, proven by <oracle + evidence>.

## To test on site (BLOCKED here – hardware / state specific)
- <function> – needs <reader on COM / physical printer / server / add-edit mode / camera>. Exact
  precondition to unblock: <...>.

## Findings
- <anything the owner should know; distinguish a data-quality artifact from a real defect>.

## Files
Screenshots: <folder>\*.png · Output samples: <paths> · Backups: <paths+sha>
