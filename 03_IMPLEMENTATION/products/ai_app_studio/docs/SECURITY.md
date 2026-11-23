# AI App Studio — security controls, residuals, owner-machine notes

Status: as built at S9a (2026-11-23). Threat model and probes: `docs/THREAT_MODEL.md`. Design: `docs/DESIGN.md`.
Final-gate findings: `docs/FINAL_GATE_ROUND1.md`. This file is cited by `apps/backend/security.py`.

## 1. Controls

### Owner boundary (REST + WebSocket, `apps/backend`)
- The API binds `127.0.0.1:17875` only (exclusive bind, no setting changes the address). The preview server binds
  `127.0.0.1:17880` (exclusive bind, S9) and runs inside the backend process (S9, B3).
- Every route except `/api/v1/health` needs the owner token (256-bit, OS CSPRNG, handed to the backend on stdin; never
  argv, environment, file or log). Exact `Host` allow-list, `Origin` allow-list, body limit. WebSockets use the
  subprotocols `aas.v1` + `aas.token.<t>`; the token entry is never echoed.
- Production origins: `tauri://localhost`, `http://tauri.localhost`, `https://tauri.localhost` only (S9, B11). Vite dev
  origins are added only through `AAS_DEV_ORIGINS`, which `scripts/run_studio.ps1` sets only with `-Dev`; the desktop
  shell removes every `AAS_DEV_*` / `PYTHON*` variable before it starts the backend.
- Owner-only settings: external MCP (off by default), device tools (off by default), plan gate (on by default),
  activity retention (`PUT /api/v1/settings/retention`, 1..3650 days, default 30).
- Project delete (S9, B4): `DELETE /api/v1/projects/<id>` creates an approval and answers 428; the owner approves it
  (`POST /api/v1/approvals/<id>/decide`) and repeats the call with `approvalId`. Single use, bound to the project id.
  Refused with 409 while a run of the project is live. Files go to `<data>/trash` (purged after the retention period);
  the preview site, APK and capture index are removed at once.

### Agent side (built-in runner, `core/aas_runner/claude.py`)
- The Claude Code CLI is resolved to an absolute path once when the backend starts; not installed = runs refused (503).
- Fixed argv: prompt on stdin, `--strict-mcp-config` with only `ai-app-studio`, `--tools ""` + catalogue allow-list +
  built-in deny-list, `--permission-mode dontAsk`, `--setting-sources ""`, studio `--settings`, `--max-turns`,
  `--max-budget-usd`, `--no-session-persistence` (S9). Env: minimal allow-list + `DISABLE_AUTOUPDATER=1` (S9).
- Memory isolation (S9 probe, CLI 2.1.330): with a fixture user `~/.claude/CLAUDE.md` and a `CLAUDE.md` above the cwd,
  the runner argv sends neither marker to the model API and writes no session transcript; a plain `claude -p` sends
  both (positive control). Test: `tests/runner/test_s9_runner.py` (loopback API, dummy key, no cost).
- One live run at a time (S9, B6): a second start answers 409 `run_in_progress`.
- The first stream-json line must be a valid `system/init` naming only the studio MCP server and catalogue tools.
- Every MCP call is bound to the run's project by a per-run grant; writes/deletes/builds need an approved plan (P5f).

### Operations (policy + audit)
- Every project operation goes through `Studio.operation` (policy table, `tool_executions`, hash-chained audit row,
  `tool.call` activity event). S9 (B12): builds (`app.build`) and device actions (`device.start`, `device.tap`,
  `device.screenshot`, `device.install`, operation `device.control`) too; refused calls are audited as well.

### Builds and previews
- Offline Docker builds (`--network none`, read-only, non-root, resource limits); studio kit + project `src/`,
  `public/` only. No build starts below 5 GiB free on the data or system drive (S9, B4).
- Preview origin per project (`p-<id16>.localhost:17880`), strict CSP, `frame-ancestors` = Tauri origins (+ dev
  origins only in `-Dev`). Captures: content-addressed PNGs, count/byte quota, served by sha256 to the owner only.

### Device (emulator in WSL2)
- Device tools are off by default; on only with the owner setting, >= 8 GiB free, a passing preflight (pinned
  `/opt/aas/bin` scripts, NAT mode, only `aas-*` AVDs).
- Emulator in a private user+net namespace; both NICs `restrict=on,ipv6=off` (S9); gRPC only with a JWT
  (`-grpc-use-jwt`, S9); `-no-snapshot -wipe-data`; software GPU `-gpu swiftshader_indirect` (S9, see residuals);
  console auth token created by the studio (random, 0600) and the session refuses to start when the token file is
  empty or malformed (an empty file disables console auth; S9); the device host never runs `adb reverse`/`forward`.
- Device screenshots: one PNG <= 8 MiB, the directory keeps <= 200 files / 200 MiB, oldest pruned (S9, B4).
- Live mirror: per-run native named pipe with a DACL for the current logon SID only, `PIPE_REJECT_REMOTE_CLIENTS`,
  `FILE_FLAG_FIRST_PIPE_INSTANCE`, HMAC handshake both ways with a 2 s timeout (S9, B8). The decoder runs with
  `hardwareAcceleration: "prefer-software"`, takes only NAL types 1/5/7/8, refuses SPS outside 8-bit 4:2:0 /
  profile 66/77/100 / 4096 px, drops access units above 1 MiB and decodes nothing before SPS + PPS + IDR (S9, B9).

### Desktop shell (`apps/desktop/src-tauri`)
- One IPC command (`get_session`) for the `main` window; preview frames are remote origins without IPC.
- WebView2 args: wry defaults re-added (`--disable-features=msWebOOUI,msPdfOOUI,msSmartScreenProtection`, wry 0.57.3
  `src/webview2/mod.rs`) + dead proxy + `disable_non_proxied_udp` (S9, B9).
- Product root resolved at run time (installed runtime bundle or the exe's ancestors; S9, B3); backend killed with
  the app (job object); backend log `<data>/logs/backend.log` (2 MiB, one rotation) + `backend-boot.log`.

### Activity data
- Server-shaped events (enum kinds, field allow-list, sanitiser, value redaction). Retention: rows older than the
  owner setting (default 30 days) are deleted at startup and every 6 h; the newest row is always kept (no seq reuse).

## 2. Accepted residuals (FINAL_GATE_ROUND1 E7, updated at S9a)
| Id | Residual | Why accepted / next step |
|---|---|---|
| R-EXT | The owner's WSL user has `NOPASSWD: ALL` + docker => root in the distro; `/opt/aas` hashes are drift detection, not a boundary | owner machine decision; documented in THREAT_MODEL |
| Manifest trust | CI trusts the committed upstream hashes in VENDORED.json (no access to the AWS repo) | owner PC re-derives with `--upstream-repo` before merge |
| Gradle TOFU | The initial `gradle/verification-metadata.xml` was generated from the network once | reviewed diff; PGP + curated keyring is a later hardening |
| Gradle image | Sources pin Gradle 8.14.8 (S9) but the local android image was not rebuilt (C: < 10 GB): it still runs 8.14.3 | MUST-DO before release: rebuild, re-check metadata, record image id (DIGESTS.json `_must_do_s9`). Exposure is low: builds are offline (`--network none`), `--no-daemon`, read-only |
| SDK licence | Android SDK licence hash recorded, licence text verified online once (S8 L1) | — |
| 64-hex exemption | Typed sha256/git fields are stored verbatim (exempt from redaction) | only lowercase digests under hash-named keys |
| Grant / pipe key | The run grant secret and the mirror pipe key sit in the run's `mcp.json` under the data root for the run's lifetime | same-user exposure; deleted at run end and on startup purge |
| P9a uid caveat | The app-uid guest probe is documented, not automated | local gate |
| Debug-only APK | Generated apps are debug-signed with the studio key | no store release in v1 |
| No network in generated apps | Kit CSP `connect-src 'self'` | feature, not a bug, for v1 |
| Emulator in WSL | Nested virtualisation in WSL2 is not an officially supported setup | local gate covers it |
| Unsigned installer | The NSIS installer is not code-signed | v1 is local only |
| Runtime bundle | The installed-mode runtime (`runtime.zip`) is supported by the shell but not produced yet | S9b packaging |
| GPU | `swiftshader_indirect` renders the guest correctly (screencap, S9 live check); the visible WSLg window was not inspected by eye in that check | confirm in the next live session; fall back to `-gpu host` = residual (gfxstream / virglrenderer guest-to-host GL path, WSL `/dev/dxg`) |
| ipv6=off | The emulator accepts `restrict=on,ipv6=off` on both NICs (qemu argv checked live); the guest-side IPv6 check did not run | next local-gate run |
| Loopback listeners | Inside the device namespace the emulator listens on a few random loopback ports besides console/adb | namespace-private, not reachable from Windows |
| Runner profile (B7) | The runner keeps the owner's real `USERPROFILE` / `HOME` / `APPDATA` (the CLI needs the owner's login). The S9 marker probe covers CLAUDE.md memory and session transcripts only; user hooks, plugins, skills and agents under the owner's `~/.claude` were NOT probed. `--setting-sources ""` + the studio `--settings` should keep user settings (and so user hooks) out, but an owner hook/plugin that the installed CLI still loads would run with the owner's rights | S9a review: recorded residual. Next: extend the marker probe with a fixture `~/.claude` holding a hook, a plugin, a skill and an agent (positive control: plain `claude -p` loads them), or run the CLI with a studio-owned profile copy holding only the credentials |
| Second account | The pipe DACL test cannot simulate a second Windows account | manual check (docs/slices/S9.md) |
| WebView2 iframe IPC | Same-origin iframe IPC (GHSA-57fm-592m-34r7 class) is avoided by keeping previews cross-origin | real-WebView2 probe B2 still to run |

## 3. Owner machine recommendations (not changed automatically)
- `%UserProfile%\.wslconfig`, section `[wsl2]` (keys documented at learn.microsoft.com/windows/wsl/wsl-config):
  `maxCrashDumpCount=0` (default 10 crash dumps in `%Temp%\wsl-crashes`), `swapFile=D:\\wsl\\swap.vhdx` (default
  `%Temp%\swap.vhdx` on C:), `swap=4GB`, `memory=8GB` (or what the PC allows). Then `wsl --shutdown`.
- Docker Desktop: move the disk image location to D: (Settings > Resources > Advanced).
- Reclaim WSL disk space: `sudo fstrim -av` in the distro, `wsl --shutdown`, then `Optimize-VHD -Path <ext4.vhdx>
  -Mode Full` (Hyper-V module) or `diskpart` `compact vdisk`. `sparseVhd=true` exists but has data-loss warnings.
- Keep C: >= 10 GB free before an image rebuild and >= 8 GB before an emulator launch (the studio refuses device tools
  below 8 GiB and builds below 5 GiB).

## 4. Running the local gate
From the product root on the owner PC (Docker Desktop running, WSL distro `Ubuntu-S9AI-D` installed with
`wsl/install.ps1`):
```
uv run pytest -q                                   # everything, local-gate probes skipped
uv run pytest tests/local_gate -q                  # local gate, non-live part
$env:AAS_LOCAL_GATE = '1'; uv run pytest tests/local_gate -q   # live: launches the emulator several times
uv run python scripts/check_vendored.py --upstream-repo <AI Website Studio clone>
uv run python scripts/gen_vendored.py --upstream-repo <AI Website Studio clone> --check
```
The live run writes a receipt under `tests/local_gate/` (commit it with its sha256). Before a live run: C: >= 8 GB,
count `%TEMP%\wsl-crashes` before and after (delete new dumps), never touch the owner's AVD `s9emu`, `~/.android`,
`~/debug.keystore`, `~/android` or adb port 5037.
