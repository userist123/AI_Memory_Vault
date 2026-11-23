# Local gate for release (step c)

Fresh live local gate on the owner PC (WSL2 `Ubuntu-S9AI-D` + KVM), branch `claude/ai-app-studio` at
`7862182bc`, 2026-11-23.

## Preconditions
- `/opt/aas/bin` hashes equal `wsl/opt-aas-bin/MANIFEST.sha256` (all 6 files), so `wsl/install.ps1` was not re-run.
- C: free before: 31.76 GB (29.6 GiB), >= 8 GB.
- `%TEMP%\wsl-crashes` before: 7 files (older `kernel-panic-*.txt`, untouched).

## Run
- Command: `AAS_LOCAL_GATE=1 uv run --project <P> pytest tests/local_gate -q -p no:cacheprovider` (one run, from `P`).
- Run id `d4c76e32c806`, 11:03:23 to 11:06:16 UTC, exit 0: 170 passed, 2 skipped, in 172.8 s. The 2 skips are
  POSIX-only unit tests (`test_console_token_is_created_private_and_non_empty`,
  `test_console_token_symlink_is_refused`) that are skipped on Windows by design.
- Receipt `tests/local_gate/receipt-2026-11-23.json` (sha256 `2a360b7e...cd783`). All probes have status `pass` with
  `full_pass_run = d4c76e32c806`: N4, N7, P9a (incl. the default-slirp positive control), P10a, P10c, PM3a, PM3b,
  PM3c, PM3d (shutdown leftover probe, with its control, and a mid-session `wsl.exe` kill).
- Phone boot took 38.0 s. The console tap at (531,1207) was read back from the guest as (531.0,1206.9). N7 had a
  maximum tap reply of 0.016 s while the video consumer was stalled. Owner adb on 5037 was absent before and after,
  and none of the 38 tracked owner paths changed.
- Emulator launches: 3, all made by the gate (studio phone session, P9a control emulator, PM3d session).
- After the run: no qemu, emulator, devhost or adb process was left in the distro. `wsl-crashes` had 7 files (no new
  dump). C: free was 20.65 GB (19.2 GiB).
- C: lost 11.1 GB during the run, with no new crash file. Another agent was rebuilding Docker images in parallel
  (the Docker vhdx is on C:), which is the likely cause, but this is not proven.

## Live extras: deferred
The gate run used all 3 allowed emulator launches, so these checks were not run. They need a fourth launch:
- guest IPv6 off (no global v6 address, `ping6` fails). Only the WSL distro was checked: it has link-local
  `fe80::` on eth0 and no global v6 address. The guest itself was not checked.
- a live `video_keyframe` op returning an IDR (unit-tested only, in `test_s9b_keyframe.py`).
- one `aas-tablet` boot and tap (only the argv snapshot for `serve-tablet` is tested).

## Threat-model probe coverage
| Probe | Status |
|---|---|
| PM1a (APK to 10.0.2.2:5037 / :5641) | not covered as specified (no app-uid APK, no `000chost:version`). Partly covered: `test_03_p9a_guest_cannot_reach_host` makes TCP connects as the shell uid to 10.0.2.2, including :5037 and :5641, and all of them fail |
| PM5a (DNS exfil via 10.0.2.3) | not covered |
| P7c (APK TCP/HTTPS egress) | not covered |
| P9b (guest to Windows host via WSL gateway) | not covered |
| P4a (listener diff Windows/WSL/Docker) | not covered (WSL default-ns listeners only, snapshot in `test_01_baseline_and_canary`) |
| PM2a (no new wslrelay listener on Windows) | not covered |
