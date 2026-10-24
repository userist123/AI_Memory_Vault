# todo-claude-loganalyzer-devmon

- **Task:** device monitor rework (only storage media and externally connected devices), D05 and report section 3c,
  then the users register, then E7 (ShellBags, RecentDocs, Office MRU, Recycle Bin $I).
- **Branch:** `claude/loganalyzer-device-monitor` (worktree `.claude/worktrees/la-devmon`, built on E6). Pushed; no PR yet.
- **Head:** 96dc39395. Dfir.Tests 1981 passed / 0 failed / 30 skipped; App.Tests 629/629; App.Classified builds.
- **PR order:** open after #303 (E6) is merged by the owner; #302 (E8) and #303 conflict on different rows of the
  comparison table; resolve after the first merge.
- **Updated:** 2026-10-21.

## Done (device monitor)
- One pure scope classifier (`DeviceScope`) for the page, D05 and section 3c; one row per physical device; UAS and
  USB/Thunderbolt NVMe disks are external disks; cards keyed by medium (volume serial, disk signature, else per insertion).
- No driver columns; "Nu funcționează (fără driver)" only as a status word.
- Page columns: Stare, Nume, Tip, Producător, Model, VID, PID, Serie, Capacitate, Literă, Etichetă, Utilizator,
  Prima apariție, Ultima conectare, Ultima deconectare, Nr. de înregistrare, Registru, Alertă; per-device Istoric from the
  sealed audit only; Export CSV (BOM, ';', formula guard, sealed note with SHA-256).
- Trust model: the ProgramData journal is untrusted; the sealed OperationAudit is the anchor. Sealed: runs and
  heartbeats, first sight, connect/disconnect, presence at run start, state changes, alerts (rate-limited popups only),
  gaps and lost events, journal resets, registration numbers, CSV exports. Two-way journal/audit comparison.
- D05: no sealed run in the period -> NEDETERMINAT; partial coverage, pending seals, unreadable records, resets ->
  DE VERIFICAT; tampering, forbidden class sealed, unregistered medium -> NECONFORM. Bluetooth/Wi-Fi off is CONFORM
  if never switched on and nothing connected (owner rule).
- Three review rounds (scope/tamper, delta, UI) and their fixes; flaky dispatcher test fixed in production code.

## Next
- Owner checks the Standalone build on the real station (as administrator and as a standard user: red banner).
- Owner decisions 2026-10-21, applied: capacity in both units, Explorer first then the label („14,9 GB (16 GB pe etichetă)”,
  `DeviceText.CapacityRo`); the app always runs elevated (`LogAnalyzer.App/app.manifest` requireAdministrator, shared by the three
  editions; test `The_application_always_asks_for_administrator_rights`). The red banner stays as a defence when the run is not elevated.
- Open PR after #303; then task 2 (users register), task 3 (E7).

## Known residuals
- A standard user cannot create the temp file in ProgramData, so the journal is saved in place (sealed note once per run).
- Without administrator rights nothing can be sealed (audit key readable only by Administrators/SYSTEM).
- Rollback of the whole audit folder by a user with write access to it is caught only by the audit anchor (not re-verified).
- Not verified on real hardware: container ids, card readers, phones, two clone sticks at once, second Windows account.

## Key files (under 02_PRODUCT/projects/workspaces/loganalyzer-dfir)
- LogAnalyzer.Dfir.Core/Devices/: DeviceScope.cs, DeviceMonitor.cs, DeviceJournalModel.cs, DeviceMonitorJournal.cs,
  DeviceMonitorAudit.cs, DeviceMonitorCoverage.cs, DeviceHistory.cs, DeviceCsv.cs, AlertRateLimiter.cs
- LogAnalyzer.Dfir.Windows/Audit/ControlEvaluator.DeviceMonitor.cs; LogAnalyzer.App/ViewModels/DeviceMonitorViewModel.cs;
  LogAnalyzer.App/Views/MediaCenterView.xaml, DeviceHistoryDialog.xaml

## 2026-10-22 (claude-sonnet) - real journal, two fixes
- Fold: a disconnected row with the SAME instance id as a connected one (any identity scheme, incl. DUP) folds into it (FoldSameInstance; guards: registration numbers, known VID/PID, kind equal or legacy v0/v1 vs present v2+). Display only.
- Kind: a keyboard/mouse under Bluetooth (HID\{...-0000-1000-8000-00805F9B34FB}, BTHENUM/BTHLE/BTHLEDEVICE, BTH* parent) is Bluetooth (forbidden class); ClassifierVersion 3; whitelist (1,3) and (2,3) Keyboard/Mouse->Bluetooth, only if KindAtVersion(old) matches the live snapshot; journal field `detail` ("tastatura prin Bluetooth").
- Not covered: the old BLE COL06 leftover (different instance id from any present row) stays visible until the mouse reconnects over Bluetooth. Tests: DeviceMonitorBluetoothHidTests.cs. Suites: Dfir 2370/31/0, App 669, UI 354/1, Edition 45.

- 2026-10-22 (rând vechi Bluetooth): un rând stocat Keyboard/Mouse cu id de instanță Bluetooth (DeviceScope.IsBluetoothInstanceId, sursa unică) este afișat ca „Dispozitiv Bluetooth” cu nota „tip vechi înregistrat”, cu alerta clasei interzise (pagină + rânduri 3c/D05, ForbiddenSeen); tipul stocat NU se rescrie, nicio reclasificare sigilată. Teste: Dfir 2372/31/0, App 670, UI 354/1, Edition 45.
