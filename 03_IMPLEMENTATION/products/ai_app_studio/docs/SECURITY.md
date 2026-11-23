# AI App Studio — security controls, residuals, owner-machine notes

Status: as built at S9c + B1/B2 live checks + final fix slice (2026-11-23; B7 probed, audit lows O6-O8 fixed). Threat
model and probes: `docs/THREAT_MODEL.md`. Design: `docs/DESIGN.md`. Final-gate findings: `docs/FINAL_GATE_ROUND1.md`,
`docs/FINAL_GATE_ROUND2.md`; audit: `docs/SECURITY_AUDIT.md`. This file is cited by `apps/backend/security.py`.

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
  Refused with 409 while a run of the project is live or a build of it holds the build lock in ANY process (OS file
  lock `<data>/build/build.lock` + holder file, S9b review M1; an external MCP server is a separate process). Files go to `<data>/trash` (purged after the retention period);
  the preview site, APK and capture index are removed at once.

### Agent side (built-in runner, `core/aas_runner/claude.py`)
- Runner profile (owner decision 2026-11-23): `full` (default) loads the owner's Claude setup with a reduced tool
  surface and an MCP deny-list (DESIGN 6.1, `core/aas_runner/profile.py`, residuals in section 2); `isolated` is the
  behaviour described in the next bullets. Owner-only route `PUT /api/v1/settings/runner-profile`.
- The Claude Code CLI is resolved to an absolute path once when the backend starts; not installed = runs refused (503).
- Fixed argv: prompt on stdin, `--strict-mcp-config` with only `ai-app-studio`, `--tools ""` + catalogue allow-list +
  built-in deny-list, `--permission-mode dontAsk`, `--setting-sources ""`, studio `--settings`, `--max-turns`,
  `--max-budget-usd`, `--no-session-persistence` (S9). Env: minimal allow-list + `DISABLE_AUTOUPDATER=1` (S9).
- Memory isolation (S9 probe, CLI 2.1.330): with a fixture user `~/.claude/CLAUDE.md` and a `CLAUDE.md` above the cwd,
  the runner argv sends neither marker to the model API and writes no session transcript; a plain `claude -p` sends
  both (positive control). Test: `tests/runner/test_s9_runner.py` (loopback API, dummy key, no cost).
- Owner extensions (B7 probe, CLI 2.1.330): with a fixture user config holding a SessionStart/UserPromptSubmit hook,
  an enabled plugin (local directory marketplace), a user skill and a user agent, plain `claude -p` loads all four and
  the hook fires (positive control); with the runner argv + settings (`disableAllHooks`) + env none is listed in init
  and no hook fires. Only CLI-bundled ("builtin") plugins/skills stay listed; init offers no tool outside the studio
  catalogue, so they cannot be invoked. Test: `tests/runner/test_b7_isolation_probe.py` (real CLI, no login, no cost).
- Agent images are capped at 512 KiB after base64 encoding; one stream-json line is capped at 2 MiB (N1).
- One agent run can create at most 50 approval records (O8, `services.MAX_APPROVALS_PER_AGENT`); any other agent
  label (the external-MCP one, shared by every session) is counted over a rolling 24 h window, so it cannot lock forever.
- One live run at a time (S9, B6): a second start answers 409 `run_in_progress`.
- The first stream-json line must be a valid `system/init` naming only the studio MCP server and catalogue tools.
  Profile `full` only: up to 64 `system/hook_started|hook_progress|hook_response` lines with `hook_event`
  `SessionStart` (the owner's own hooks) may precede it; they are skipped, never recorded (owner smoke 2026-11-23).
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

- Desktop exe (S9c): studio-owned Tauri wrapper (`images/desktop-wrapper`, exact pins, committed Cargo.lock, no app
  commands, no plugins, `capabilities: []`, kit CSP); the agent supplies only the validated web dist. Host build
  `cargo build --release --offline --locked` against the vendored crate dir (checksums from Cargo.lock), minimal
  env, dead proxy, studio CARGO_HOME, 1800 s timeout + tree kill, exe read after exit (no links, PE magic, cap). The
  exe and the zip of its embedded dist are one pair (one build dir, both sha256 in the build record, checked at export);
  host paths are remapped (`--remap-path-prefix`) and a leak fails the build.

### Device (emulator in WSL2)
- Device tools are off by default; on only with the owner setting, >= 8 GiB free, a passing preflight (pinned
  `/opt/aas/bin` scripts, NAT mode, only `aas-*` AVDs).
- Emulator in a private user+net namespace; both NICs `restrict=on,ipv6=off` (S9); gRPC only with a JWT
  (`-grpc-use-jwt`, S9); `-no-snapshot -wipe-data`; software GPU `-gpu swiftshader_indirect` (S9, see residuals);
  console auth token created by the studio (random, 0600) and the session refuses to start when the token file is
  empty or malformed (an empty file disables console auth; S9); the device host never runs `adb reverse`/`forward`.
- Device screenshots: one PNG <= 8 MiB, the directory keeps <= 200 files / 200 MiB, oldest pruned (S9, B4).
- Live mirror: per-run native named pipe with a DACL for the current logon SID only (TokenUser SID when the token
  has none, e.g. CI service logons), `PIPE_REJECT_REMOTE_CLIENTS`,
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

**v1 launch mode (decision 2026-11-23, owner rule "decide safe, record"):** v1 is launched from the repo/runtime with `scripts/run_studio.ps1` (Tauri window + backend). The NSIS installer is **experimental, unsigned** until an installed-mode e2e passes.

### W2-W6 surfaces (flows, inspector, baselines, brand kit; added 2026-11-23, Opus review round 1)
- **Flow tests (W3).** A flow test is data, not code: `tests/flows/<name>.spec.ts` is parsed by a closed-subset parser
  (a fixed statement list, no imports, no free expressions, no `eval`/`Function`/template evaluation); anything outside the
  subset is refused at write time and at run time. It is executed by the studio's own interpreter in the HOST Chromium
  (never `node` / the spec file itself), launched with a dead proxy for every non-loopback request, a same-origin route
  filter (every other request aborted) and a CSP on the preview origin. Results are server-shaped `qa.result` events (suite
  `flows`), screenshots go to the content-addressed capture store.
- **Inspector (W4).** Owner-only REST (`/inspector`, `/inspector/edit`, `/inspector/capture`), behind the boundary
  middleware. The preview posts a descriptor `{v:1, kind:"select", ...}`; the UI accepts it only from the exact preview
  origin of the current project and only from that iframe's window (source check), and the backend validates it again with a
  strict schema (`extra=forbid`, length caps, control/bidi characters refused). A class swap is limited to the W1 design
  system allow-list (one family per swap). Text edits replace only JSX text children (the `>` that closes a JSX tag up to the
  next tag start; a `a > x <` in a code expression is not a match) or a whole string literal equal to the visible text;
  0 or more than 1 location -> refused (404 / 409 `inspector_ambiguous`). Two steps: without `confirm` the server answers a
  dry run `{dryRun, path, line, planToken}` and writes nothing; the panel shows "Va edita <file>:<line>" and only the
  owner's Confirm sends the same body with `confirm: true` and that `planToken` (sha256 of path | line | baseRevision |
  op | old | new, recomputed by the server; missing or different -> 409 `inspector_plan_mismatch`, nothing written), then
  a revisioned `write_file`, audited, rebuild. A string literal that is a call argument (`navigate("X")`,
  `db.exec("a", "X")`) or sits inside a `//` / `/* */` comment is never a text target.
- **Baselines (W6).** Only the owner approves or resets baselines. Volatile regions (`data-aas-volatile`) are measured in a
  CDP isolated world (the page cannot patch `querySelectorAll` / `getBoundingClientRect` for the measurement), recorded
  ONLY at owner approval and capped at 15 % of the frame (more -> approval refused). At compare time only the approved masks
  are used (the current page's masks are ignored, so an agent cannot add masks after approval); a stored mask share above
  15 % counts as changed. `maskedPct` is reported in the status result.
- **Brand kit (W5).** Glyphs come from a manifest-checked bundled set (name pattern + manifest membership + hash); the
  generated resources are mounted `:ro` into the Android build container; `brand_kit` is plan-gated for agents. Store
  screenshots and the caption text are bound to the export approval (their sha256 values are part of the approved
  parameters; the export re-verifies them and answers 409 / re-approval if they differ).
- **Residual (baselines, masks).** Up to 15 % of a frame may be masked as volatile by design: a regression that lies
  entirely inside an approved mask is not detected by the pixel compare (the owner sees the mask share as `maskedPct` and
  approves the masks; above 15 % the approval is refused and a stored share above it counts as changed).
- **Residual (export, store screenshots).** The APK export copies the approved APK first and then the store screenshots,
  re-checking each against the approved fingerprint; if a screenshot changes mid-copy the export stops with 409 and the
  already copied approved APK (and any screenshots copied before the change) stay in the export folder. Nothing unapproved
  is ever written; the owner re-approves and exports again.
- **Residual (flows).** Flow tests run in the host Chromium, not in the offline container: the dead proxy, same-origin
  route filter and CSP are the controls, and a Chromium escape would run as the owner (same class as R-EXT for the
  capture path).

## 2. Accepted residuals (FINAL_GATE_ROUND1 E7, updated at S9a and at release, 2026-11-23)
| Id | Residual | Why accepted / next step |
|---|---|---|
| R-EXT | The owner's WSL user has `NOPASSWD: ALL` + docker => root in the distro; `/opt/aas` hashes are drift detection, not a boundary | owner machine decision; documented in THREAT_MODEL |
| Manifest trust | CI trusts the committed upstream hashes in VENDORED.json (no access to the AWS repo) | owner PC re-derives with `--upstream-repo` before merge |
| Gradle TOFU | The initial `gradle/verification-metadata.xml` was generated from the network once | reviewed diff; PGP + curated keyring is a later hardening |
| Gradle image | Sources pin Gradle 8.14.8 (S9) | Closed 2026-11-23 (release step b): image rebuilt (sha256:ad389dfcab6b...), Gradle 8.14.8, strict dependency verification passed, PB7a/PB7b pass |
| SDK licence | Android SDK licence hash recorded, licence text verified online once (S8 L1) | — |
| 64-hex exemption | Typed sha256/git fields are stored verbatim (exempt from redaction) | only lowercase digests under hash-named keys |
| Grant / pipe key | The run grant secret and the mirror pipe key sit in the run's `mcp.json` under the data root for the run's lifetime | same-user exposure; deleted at run end and on startup purge |
| P9a uid caveat | The app-uid guest probe is documented, not automated | local gate |
| Debug-only APK | Generated apps are debug-signed with the studio key | no store release in v1 |
| No network in generated apps | Kit CSP `connect-src 'self'` | feature, not a bug, for v1 |
| Emulator in WSL | Nested virtualisation in WSL2 is not an officially supported setup | local gate covers it |
| Unsigned installer | Experimental in v1 (v1 runs via `scripts/run_studio.ps1`). The NSIS installer (`pnpm tauri build --config src-tauri/tauri.release.conf.json` after `scripts/build_runtime.ps1`) is not code-signed: SmartScreen warns, and a tampered copy is not detected by Windows | v1 is built and installed on the owner PC only, never downloaded; the runtime.zip sha256 is printed by the build and its first 16 hex are the runtime version the shell checks; signing = later (certificate cost) |
| Runtime integrity (S9b review L3) | The shell verifies runtime.zip against the sha256 in runtime.version before extracting: this catches damage and partial copies, not a deliberate swap of both files in the install dir | code signing of the installer (residual above) |
| Runtime bundle | `scripts/build_runtime.ps1` (S9b) stages python + locked deps + Chromium headless shell + app sources; Docker images, the Android SDK/emulator and the Claude CLI are not inside (owner PC prerequisites) | installed-mode end-to-end test (install, first launch, capture) still to run |
| Export scan | The export secret scan is pattern-based (key shapes + the registered owner token); a secret without a known shape is not detected | owner approval of the exact sha256 is the second control |
| Web-build image | S9b `tsc --noEmit` step | Closed 2026-11-23 (release step b): image rebuilt (sha256:5b3a6cae2153...), a planted type error exits 5 in the real container |
| GPU | `swiftshader_indirect` renders the guest correctly (screencap, S9 live check); the visible WSLg window was not inspected by eye in that check | confirm in the next live session; fall back to `-gpu host` = residual (gfxstream / virglrenderer guest-to-host GL path, WSL `/dev/dxg`) |
| ipv6=off | The emulator accepts `restrict=on,ipv6=off` on both NICs (qemu argv checked live) | Closed 2026-11-23 (release live extras): in the guest no global v6 address, `ping6` to a global address fails, `ping6 ::1` works (docs/slices/LOCAL_GATE_RELEASE.md) |
| Loopback listeners | Inside the device namespace the emulator listens on a few random loopback ports besides console/adb | namespace-private, not reachable from Windows |
| Runner profile (B7) — owner decision 2026-11-23 | The owner chose that the builder loads his Claude Code setup: profile `full` is the DEFAULT (user CLAUDE.md, auto-memory of the home dir, hooks, plugins, skills, agents, user MCP servers; built-ins only Read/Grep/Glob/Skill/Task; MCP deny-list; claude.ai connectors off). The B7 isolation (`isolated` profile, the old behaviour, still probed by `tests/runner/test_b7_isolation_probe.py`) is one toggle away. Residuals accepted by the owner: (1) the owner's memory and settings sit in the same context as untrusted generated content (tool results, console text, screenshots), so a prompt injection could echo memory into the timeline or into the generated app; (2) Read/Grep/Glob can read any file the owner can except the denied credential paths (exact list in DESIGN 6.1; absolute `//` / `~/` rules, live-probed on Windows CLI 2.1.330: fixture .env, .credentials.json, api_token outside the cwd denied, notes.txt read); a secret in a file with another name is readable; subagents inherit only the parent's tools (live probe: no Bash / Write / `gh api` from a Bash+Write agent or general-purpose); (3) the owner's hooks run with the owner's rights on every run; (4) the deny-list is defence in depth (primary control: `dontAsk` with no allow entry for owner MCP tools; matching NFKC + case-insensitive, non-ASCII names denied; denied servers are never launched: `--strict-mcp-config` with only the studio server plus the owner's user servers not fully denied, plugin MCP servers and claude.ai connectors never) and name-based: a new server or a new plugin tool is allowed until the owner adds it (the committed default denies every server of the owner's configuration as of 2026-11-23, i.e. all claude.ai connectors, all plugin MCP servers, agent-bridge, ai-website-studio, chrome-devtools, chrome-user, clips-kitty, fetch, playwright); a tool added after init (list_changed) is checked only by the CLI deny rules, not by the init check; substrings that would also hit a studio tool (`delete`, `write_file`) are enforced at init only; (5) a future CLI version may change how user extensions load; (6) owner decision 2026-11-23 (chat): in the `full` profile a chat turn runs without `--no-session-persistence` and continues the previous turn with `--resume <session id>` (id format-checked, chat runs only), so the CLI writes the session transcript into the owner's `~/.claude/projects` like any normal Claude Code session of the owner (the transcript holds the prompts, the AI replies and tool results, i.e. also untrusted generated content); if the CLI refuses the resume the turn continues from a summary of the earlier turns (`chat.fallback`); the `isolated` profile and every non-chat run keep `--no-session-persistence` (no transcript, chat continuity only from the summary) | mitigations: no shell, no file-write and no web built-in, egress/exec MCP tools denied and the init check fails closed, project writes only via studio MCP tools (plan gate, allow-list, audit), export secret scan + approval bound to the sha256, activity redaction (memory content can still appear in AI messages in the local timeline); review the deny-list after every plugin/MCP change; probes: tests/runner/test_profile_real_cli.py (+ `AAS_OWNER_CONFIG_PROBE=1` against the real config) |
| Unsigned exe (S9c) | The generated desktop exe is not code-signed: SmartScreen warns on another PC and tampering is not detected by Windows | export is owner-approved and bound to the exe sha256 (printed in the result); signing = later, with the studio installer |
| Host-build trust (S9c) | The desktop build runs on the Windows host, not in a container: the build scripts and proc-macros of the ~430 vendored crates (tauri, wry, windows, ...) run with the owner's rights. Integrity: cargo verifies every vendored crate against the Cargo.lock checksum (detects damage, not tampering by a writer: `.cargo-checksum.json` sits in the same dir); tampering is detected by a studio-held per-file sha256 manifest (outside the vendor dir) checked before every build and prevented by the owner-only root (ACL checked: other accounts cannot write it, nor delete / rename / add files or folders in any parent; checked again right before cargo runs; dev root `D:\aas-desktop`; with `AAS_DESKTOP_ROOT` unset and < 10 GB free on the data drive the build is refused with a pointer to set it to an owner-only folder on another drive); no `.cargo` config in a parent of the build dir, cargo `--config` pins no rustc wrapper, studio CARGO_HOME config; the vendor step itself used the network once (TOFU on crates.io at that moment). The agent's only input is the dist (data embedded by tauri-codegen, which parses index.html for CSP hashes); the target dir is shared across projects (studio-owned) | accepted for v1 (same crates as the studio shell); later: build in a Windows container / sandbox user, `cargo vet` / audit of the lock |
| No NSIS for apps (S9c) | Generated desktop apps ship as a bare exe (no installer, no uninstall entry); they need the WebView2 runtime | NSIS needs tauri-cli + a download at bundle time (not offline): later |
| Second account | The pipe DACL test cannot simulate a second Windows account | manual check (docs/slices/S9.md) |
| PM1a not automated (release) | The app-uid APK probe (`000chost:version` to 10.0.2.2:5037 / :5641 from an installed test APK) has no test: building and installing a probe APK per gate run was not done for v1 | partly covered: `test_03_p9a_guest_cannot_reach_host` connects as the shell uid to 10.0.2.2 on every WSL listener plus :5037, :5641, :5600, :5601, :8554 and a canary, all fail (restrict=on drops the 10.0.2.2 gateway for every uid); controls: emulator + studio adb in a private user/net/pid namespace, studio adb on 5641 only, owner adb 5037 never started (checked before/after each live run) |
| PM5a not automated (release) | DNS exfil via 10.0.2.3 from an app uid (canary resolver) is not probed | controls: `restrict=on` on both NICs removes the slirp DNS at 10.0.2.3 for every uid; `test_03` shows the guest reaches nothing behind the gateway; next step: probe APK + canary resolver |
| P7c not automated (release) | App-uid TCP/HTTPS egress (1.1.1.1:443, canary) is not probed | controls: `restrict=on,ipv6=off` (no slirp egress, no v6; guest IPv6 checked live, LOCAL_GATE_RELEASE.md "Live extras"), generated apps ship with kit CSP `connect-src 'self'` |
| P9b not automated (release) | Guest to the Windows host through the WSL gateway IP (:17875/:17880/:135) is not probed | controls: `restrict=on` (slirp forwards nothing outside the guest), the emulator lives in a private net namespace with only a dummy interface (no route to the WSL gateway); backend/preview bind 127.0.0.1 on Windows with Host/Origin allow-lists and the token |
| P4a not automated (release) | The full listener diff (Windows `Get-NetTCPConnection` + WSL default ns + Docker published ports, before/during a session) is not automated | partly covered: WSL default-ns listener snapshot in `test_01_baseline_and_canary` and the leftover probe (no :5600/:5601/:5641 in the default ns after a session); builds run `--network none` without `-p` (argv tests); backend/preview bind 127.0.0.1 |
| PM2a not automated (release) | "No new wslrelay listener on Windows during a device session" is not probed | controls: nothing of the device session listens in the WSL default namespace (all device ports are inside the private namespace; leftover probe), so wslrelay has nothing to relay |
| External-MCP approval cap label | The O8 cap for the external MCP server counts approvals per agent label over a rolling 24 h window; the label is `AAS_AGENT_ID` from the environment of the owner's MCP client (default `claude-code`), so a client configured with changing labels gets a fresh cap per label | accepted for v1: external MCP is OFF by default and enabled only by the owner (owner-only REST route); the label comes from the owner's client configuration, not from the agent; every approval still needs the owner's decision; runner runs are capped per run (`runner:<run_id>`) |
| WebView2 iframe IPC | Same-origin iframe IPC (GHSA-57fm-592m-34r7 class) is avoided by keeping previews cross-origin. B2 probe (real WebView2) PASS: every IPC channel from the preview iframe is blocked; wry still injects the IPC scripts into subframes on Windows | accepted: blocked by the CSP and the Tauri ACL (iframe origin refused); `docs/slices/B2-webview2.md` |

### 2a. Dependency advisories accepted for v1 (OSV-Scanner, review by 2027-02-23)

Decision recorded 2026-11-23 (owner). The product OSV job reads one `osv-scanner.toml` next to each affected file; each ignores exactly the ids below, for that file only, with `reason` and `ignoreUntil = 2027-02-23`. A new advisory on any other package, or in any other file, still fails the job.

**Gradle build-tool internals** (`images/android-build/template/gradle/verification-metadata.xml`, config `images/android-build/template/gradle/osv-scanner.toml`; 95 findings, 54 unique ids). Reason: AGP/Gradle build-tool internals; used only inside the offline, read-only, non-root, --network none Android build container on studio-controlled inputs; Capacitor 8 pins AGP 8.13 so a bump is out of scope for v1. The artifacts are pinned and checksum-verified by the dependency verification file; they are not part of any generated app.

| Package | Advisory ids |
|---|---|
| `com.google.guava:guava` | GHSA-xxph-c9ww-hj94 |
| `com.google.protobuf:protobuf-java` | GHSA-735f-pc8j-v9w8 |
| `com.google.protobuf:protobuf-kotlin` | GHSA-735f-pc8j-v9w8 |
| `io.netty:netty-codec` | GHSA-3p8m-j85q-pgmj, GHSA-558v-64gr-wgg4, GHSA-mj4r-2hfc-f8p6 |
| `io.netty:netty-codec-http` | GHSA-38f8-5428-x5cv, GHSA-4mp9-239f-g9hg, GHSA-57rv-r2g8-2cj3, GHSA-5jpm-x58v-624v, GHSA-6cqp-g7gg-8hr5, GHSA-6jqx-86gh-f27w, GHSA-84h7-rjj3-6jx4, GHSA-8c42-7qj2-3j46, GHSA-f6hv-jmp6-3vwv, GHSA-fghv-69vj-qj49, GHSA-gcjf-9mgh-3p7g, GHSA-hvcg-qmg6-jm4c, GHSA-jppx-w49h-x2qq, GHSA-m4cv-j2px-7723, GHSA-mvh2-crg5-v77c, GHSA-pwqr-wmgm-9rr8, GHSA-q4f6-jm68-57ww, GHSA-v8h7-rr48-vmmv, GHSA-xxqh-mfjm-7mv9 |
| `io.netty:netty-codec-http2` | GHSA-563q-j3cm-6jxm, GHSA-5x3r-wrvg-rp6q, GHSA-93wv-jw9v-4972, GHSA-c2gf-v879-257j, GHSA-c69g-56f8-xwqj, GHSA-f6hv-jmp6-3vwv, GHSA-prj3-ccx8-p6x4, GHSA-w9fj-cfpg-grvv, GHSA-xpw8-rcwv-8f8p |
| `io.netty:netty-common` | GHSA-389x-839f-4rhx, GHSA-xq3w-v528-46rv |
| `io.netty:netty-handler` | GHSA-3qp7-7mw8-wx86, GHSA-4g8c-wm8x-jfhw, GHSA-6mjq-h674-j845, GHSA-c4c3-7fpv-j4q5, GHSA-c653-97m9-rcg9, GHSA-fccg-mwvh-qqg4, GHSA-x4gw-5cx5-pgmh |
| `io.netty:netty-handler-proxy` | GHSA-45q3-82m4-75jr |
| `org.apache.commons:commons-compress` | GHSA-4265-ccf5-phj5, GHSA-4g9r-vxhx-9pgx |
| `org.apache.httpcomponents:httpclient` | GHSA-7r82-7xv7-xcpj |
| `org.bitbucket.b_c:jose4j` | GHSA-3677-xxcr-wjqv |
| `org.bouncycastle:bcpkix-jdk18on` | GHSA-wg6q-6289-32hp |
| `org.bouncycastle:bcprov-jdk18on` | GHSA-574f-3g2m-x479, GHSA-9pwp-9qqc-pr26, GHSA-c3fc-8qff-9hwx, GHSA-p93r-85wp-75v3, GHSA-qp49-qgx5-5m26 |
| `org.jdom:jdom2` | GHSA-2363-cqg2-863c |
| `org.json:json` | GHSA-ghc9-756g-vrcv |

**Rust crates** (`apps/desktop/src-tauri/Cargo.lock` and `images/desktop-wrapper/Cargo.lock`, one `osv-scanner.toml` beside each):

| Crate | Advisory ids | Reason |
|---|---|---|
| `chrono` | RUSTSEC-2026-0543 | chrono 0.4.45: no fixed release available; locked only for non-Windows targets, not in the Windows target graph (verified 2026-11-23: `cargo tree -i chrono --target x86_64-pc-windows-msvc` prints nothing for both lockfiles) |
| `glib` | GHSA-wrw7-89jp-8q8g (alias RUSTSEC-2024-0429) | glib 0.18.5: Linux-only gtk path of wry/tao, not in the Windows target graph; the app ships on Windows only; the fix needs the gtk-rs 0.20 generation (Tauri upgrade, later); verified with `cargo tree -i glib --target x86_64-pc-windows-msvc` |
| `proc-macro-error` | RUSTSEC-2024-0370 | proc-macro-error 1.0.4: unmaintained, no fixed release available; locked only for non-Windows targets, not in the Windows target graph (verified 2026-11-23: `cargo tree -i proc-macro-error --target x86_64-pc-windows-msvc` prints nothing for both lockfiles) |

Next step at review: bump AGP with the next Capacitor major; bump Tauri/wry when the gtk-rs 0.20 generation lands; drop each ignore as soon as a fixed release exists (the ignores expire on the date, so the job fails again if nobody reviews them).

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
