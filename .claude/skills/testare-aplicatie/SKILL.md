---
name: testare-aplicatie
description: >-
  Black-box / acceptance / smoke test a FINISHED application the way the owner would use it, and
  prove each function with an oracle + evidence before saying "gata". Walks EVERY menu/function,
  decides PASS/FAIL/BLOCKED against a pre-written expected result (not "a window opened"), and keeps
  what was proven here separate from what needs hardware/on-site. Prefers established drivers
  (pywinauto/FlaUI for Windows desktop, Playwright for web) and ships a zero-dependency PowerShell
  Win32+UIA toolkit as a fallback. Not for writing unit/integration tests (that is TDD / test-author)
  and not for building the app (that is aplicatie-securizata). Triggers - "testeaza aplicatia",
  "testare aplicatie", "verifica aplicatia", "testeaza fiecare functie", "mergi prin toate meniurile",
  "test ca utilizatorul", "proba pe aplicatie", "QA pe aplicatie", "test manual", "verifica ca merge
  totul", "test the app", "walk every menu", "smoke test the UI".
---

# testare-aplicatie — acceptance/smoke testing of a finished app, like the owner

Open the real, installed application and confirm **every menu and every function** does what it is
supposed to, each proven by an **oracle** (an expected result written *before* acting) with a
screenshot/log/file as supporting evidence. Never claim "done" because a process launched or a
dialog appeared — that is the classic false PASS. Verify like the owner would
(memory `livrare-verificata-ca-proprietarul`). This is acceptance testing of a built product, NOT
writing automated test code.

## When to use
- The owner asks to test / verify a finished app, walk through all its functions, or prove it works
  after an install or a fix. Pairs with `aplicatie-securizata` (that builds + hardens; this does the
  final user-level acceptance pass and the evidence report).

## Choose the driver first (prefer established tools over hand-rolled)
Inspect what the app exposes before picking a tool: run **Inspect.exe** or **Accessibility Insights
for Windows** and see whether controls expose UI Automation (UIA, rich: AutomationId/Name/patterns),
only MSAA (legacy IAccessible), or nothing (owner-drawn).
- **Windows desktop:** `pywinauto` (Python, `backend='win32'` is built for legacy MFC/VB6 HWNDs,
  `backend='uia'` for modern apps) or `FlaUI` (.NET, UIA2 mature for Win32/WinForms, UIA3 for WPF/UWP).
  **Avoid WinAppDriver** (unmaintained since ~2020) and the deprecated `appium-windows-driver`; if a
  WebDriver stack is required use `appium-wincore-driver`. [UNVERIFIED versions, 2026: pywinauto
  ~0.6.9, FlaUI ~5.0.0 — confirm before relying on a version.]
- **Web:** `Playwright` — user-facing locators (getByRole/getByText/getByTestId), auto-waiting,
  isolated BrowserContexts, trace viewer + HTML report (passed/failed/flaky/skipped). Chromium is
  pre-installed at the pinned path.
- **Fallback (no install):** the shipped PowerShell toolkit `scripts/lib_ui.ps1` — use when you
  cannot add pywinauto/FlaUI. It has the same discipline built in (locate by UIA first, foreground-
  safe input, waits, window-content screenshots, cleanup).

## Method (ISTQB-grounded)

1. **Protect data, then launch.** Back up every database/config the app writes **before** touching
   it (`Backup-File`, keep the sha). Prefer a **data copy / VM / Windows Sandbox** for apps that write
   real data. Confirm the installer won't overwrite user data (`onlyifdoesntexist` = safe). Record how
   it was launched and that it started without crashing.

2. **Inventory functions from the accessibility tree, don't guess.** Dump the UIA tree
   (`Get-UiaTree` gives type, Name, AutomationId, enabled, bounding rect, patterns) or use
   Inspect.exe. Build the checklist from that list — one todo per function — and track **coverage**
   (functions discovered vs exercised).

3. **One test case per function, oracle FIRST.** Before acting, write: objective, **precondition**,
   the action, the **expected result (oracle)**, postcondition. The oracle is a *checkable* state
   change — a UIA text/value, an output file's content, a DB diff — with the screenshot as supporting
   evidence, never the verdict. Design cases with equivalence partitioning / boundary values; add one
   negative case per risky function where cheap. Gate on a **smoke** pass of the critical path first.

4. **Locator ladder — coordinates are the LAST resort.** For each action try, in order:
   (a) UIA pattern (`Invoke-Uia`: Invoke/SelectionItem/Toggle), (b) UIA AutomationId, then Name,
   (c) UIA BoundingRectangle centre, (d) keyboard accelerators, (e) a coordinate map. Coordinates
   break under DPI/resize/layout shifts — label them as a fallback and recalibrate from the current
   window rect, never assume a fixed size. For owner-drawn MFC controls (empty WM_GETTEXT, generic
   Pane in UIA), use pywinauto `win32` HWND traversal + `control.rectangle()`-relative click, or
   keyboard, or image/VLM grounding — not absolute screen coordinates.

5. **Wait on conditions; never fixed sleeps.** Use `Wait-Until` / `Wait-WindowTitle` /
   `Wait-FileStable`. After every step run a **liveness check** (`Test-ProcessAlive`, and watch for an
   unexpected modal). **Assert the foreground window belongs to the target process before any click or
   keystroke** (`Assert-Foreground`) — if focus was refused or stolen, synthetic input lands in the
   wrong window; abort that step. Bound retries to ≤2, then mark FAIL or BLOCKED.

6. **Follow the real chained workflow.** Many functions are gated: print needs a validated record,
   search needs input, edit needs add/edit mode, a button may be disabled until a precondition. A
   disabled button or a validation message is correct behavior → record the precondition, reach the
   state, continue; do NOT auto-assume "disabled = fine" or "dead click = coordinate miss" without
   checking (both mask real defects). Prove end-to-end flows to completion and open the output file to
   confirm its content. **Before committing a print, verify the selected printer by NAME**
   (`Confirm-PrinterByName`) so a test never hits a physical printer by accident.

7. **Status vocabulary (ISTQB).** PASS (actual matches oracle), FAIL (deviates — defect), **BLOCKED**
   (cannot execute: hardware/peripheral/server/missing prerequisite/upstream crash — NOT an app
   defect), SKIPPED (out of scope), NOT RUN. Hardware- and on-site-dependent checks (real reader on
   COM, physical printer, network server, camera capture) are **BLOCKED with the exact precondition**,
   never PASS/FAIL.

8. **Clean up and report.** `Restore-Desktop` (clear topmost, restore minimized windows), restore the
   default printer, apply the owner's `.curatenie.json`. Note that screenshots may hold personal data.
   Write the report (`references/raport_template.md`): PASS/FAIL/BLOCKED table with the evidence file
   per row, coverage count, the fixes confirmed, the on-site list, findings, and a **run manifest**
   (build hash, resolution, DPI, data snapshot) so a fix can be re-tested. Report to the owner in
   Romanian; keep file contents/screenshot names in English.

## Windows toolkit (`scripts/lib_ui.ps1`, generic) + `scripts/profile_example.ps1` (per-app)
Keep the engine generic and put all app-specific locators/steps in a copied `profile_<app>.ps1`
(`Invoke-AppTests` drives the run). Key helpers: `Get-AppWindows`/`Find-Window` (requires a real pid;
`-AnyProcess` to opt out), `Wait-Until`/`Wait-WindowTitle`/`Wait-FileStable`, `Assert-Foreground`,
`Click-PointSafe` (verifies the point is over the target process), `Send-KeysSafe`/`ConvertTo-SendKeys`
(escapes `+ ^ % ~ ( ) { } [ ]`), `Save-WindowShot` (PrintWindow — captures the window content without
stealing focus or capturing overlaps), `Get-UiaTree`/`Find-Uia`/`Invoke-Uia`/`Set-UiaValue` (locator
ladder), `Backup-File`/`Save-OutputPath`/`Confirm-PrinterByName` (data/hardware safety),
`Hide-Window`/`Set-Topmost`/`Restore-Desktop` (cleanup — everything touched is restored),
`Save-DefaultPrinter`/`Restore-DefaultPrinter` (keep the machine's default printer across a print test),
`Invoke-WithRetry` (bounded retry: PASS when the check passes, else FAIL — never loops forever).

## Autonomous self-verification
Combine structured checks (UIA text/value, log lines, output-file/DB diff) with semantic reading of
the screenshot (does the expected text/button/content appear, is anything clipped/blank) — but the
screenshot confirms, the structured oracle decides. Avoid pixel-to-pixel diffs (they fail on benign
font/GPU rendering). On failure, attach the full-screen capture + the element-under-point + process
state as diagnostics.

## Web apps
Drive with `playwright` (installed, pinned Chromium). Same discipline: inventory the nav/DOM, one
oracle per function (web-first assertions auto-retry), fresh BrowserContext per case for isolation,
screenshot + trace on failure, same PASS/FAIL/BLOCKED report.

## Gotchas (learned)
UIA `InvokePattern` fails on custom MFC "Pane" buttons → coordinate click as labelled fallback.
Native Save-As/Print common dialogs are the flakiest part: the "File name" combo is often not UIA-
exposed → click the field + type the **bare filename** when already in the target folder (a full path
with `\` `:` `~` can land in the file list as a rename and error). `$using:` does not work inside
Win32 callback delegates (use `$script:`/`$global:`). A stuck "Spooling"/"Deleting" print job needs
the Print Spooler service restarted (affects ALL jobs, needs admin). If the app runs elevated and the
test host does not, synthetic input is silently dropped (looks like a coordinate miss) — match
integrity levels. No DPI-awareness → UIA rects and pixels disagree at non-100% scaling (the toolkit
calls `SetProcessDPIAware` at load).
