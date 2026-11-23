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

## Live extras: deferred (done later the same day, see "Live extras run" below)
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

## Live extras run (2026-11-23, after the P1c fix, branch at `0375006ce`)
- Preconditions: C: free 20 GB (>= 8 GB); `%TEMP%\wsl-crashes` 7 files; no qemu/emulator/devhost process and no
  listener on :5037/:5600/:5602/:5641 in the distro before the run.
- Command: `AAS_LOCAL_GATE=1 uv run pytest tests/local_gate/test_live_extras.py -q -p no:cacheprovider` (from `P`),
  11:23:00 to 11:24:18 UTC, exit 0: 5 passed in 76.9 s. Emulator launches: 2 (phone, then tablet), both by the test.
- Launch 1 (`aas-phone`): booted; guest network up (10.0.2.15) before the checks.
  - Guest IPv6 off: `ip -6 addr show scope global` lists no `inet6`; `ping6 -c 2 2001:4860:4860::8888` fails;
    positive control `ping6 -c 1 ::1` succeeds (the tool works, the failure is the network).
  - Live `video_keyframe`: after `video_start` and a drain, the op answered `{"ok": true, "msg": "restarted"}` and the
    h264 stream collected in the next 6 s contains an SPS (NAL 7) followed by an IDR (NAL 5).
  - Shutdown exit 0; leftover probe empty.
- Launch 2 (`aas-tablet`): booted with the visible WSLg window "Android Emulator - aas-tablet"; one console tap at
  (640,400) answered ok and the guest input dispatcher read it back at (640,400) after rounding. Shutdown exit 0;
  leftover probe empty; owner state (adb on 5037, `.android`, `debug.keystore`, `android`) unchanged from before
  launch 1.
- After the run: `wsl-crashes` still 7 files (module crash-dump guard passed); C: free 20 GB.
- Evidence limit: the passing assertions are the evidence. The measured values (boot times, NAL list, ping6 return
  codes) were not written to the receipt because the tests had no probe marks in this run (the receipt records only
  marked tests). The marks were added afterwards (R-ipv6, R2, S9-tablet, P10a, P10c) for the next run; a third
  launch to re-record the values was not made (limit of 2 launches).
- Closes: residual "ipv6=off" (guest side) and the live half of R2 (S8 key frame). The tablet form factor boots and
  takes input.
