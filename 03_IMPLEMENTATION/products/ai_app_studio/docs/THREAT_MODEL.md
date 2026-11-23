# AI App Studio — threat model v2 (2026-11-23; probes rewritten, M1-M5 added)

Inputs: Fable advisor sketch, docs/RESEARCH_2026-11-22.md, AI Website Studio controls (fork base 25b3eab).
Status: written BEFORE implementation. Every probe below must be red before its control exists.

## Decision: target stack v1
Web-core single codebase (React + TypeScript + Vite static build) wrapped by:
- browser device frames (phone 390x844, tablet 834x1112, desktop 1440x900) on a per-project origin;
- Capacitor Android debug APK, installed on a studio-owned headless emulator in WSL2 (same bundle in WebView);
- Tauri 2 Windows wrapper in v1.1. iOS out of scope (needs macOS or a third-party cloud build).
Why: identical bundle everywhere => evidence-grade screenshots; DOM-based QA reuse; no Metro/dev server
(CVE-2025-11953 class). Expo/RN kept as v2 option if native widgets are required.

## Assets
Owner PC and files; owner token and approval authority; AI provider credentials; all project sources;
the owner's unrelated S9 work in the same WSL distro (AVD s9emu, ~/debug.keystore, Lineage tree);
future signing keys; audit integrity; the owner's attention (activity UI).

## Trust boundaries
B1 agent (MCP / built-in runner) -> studio core
B2 project source -> build container -> host workspace (build output)
B3 package registry -> curated kit image
B4 generated app JS (browser frame) -> Tauri shell, studio API, LAN, other projects
B5 generated APK (emulator guest) -> WSL loopback (adb 5037, emulator 5554/5555, gRPC) -> Windows host
B6 agent/model/log text -> activity stream -> owner GUI
B7 exported artifact -> outside world

## Attack paths, controls, probes (v2, 2026-11-23 — probes rewritten per review round 1 / B5)
Probe rule: every probe has a positive control `[+]` that runs the same check with the control absent or on the
allowed path and must SUCCEED; a probe whose `[+]` fails is invalid (it would pass vacuously). Design references
are sections of docs/DESIGN.md. Where a probe runs: see DESIGN.md sec. 8.

| # | Attack | Control (DESIGN.md) | Probes — must fail today; `[+]` = positive control |
|---|---|---|---|
| T1 | Build-time code exec on host | Docker-only build, fixed argv, studio-owned kit, positive write allow-list (3.1-3.4) | P1a code_write_file on package.json, PACKAGE.JSON, vite.config.ts, capacitor.config.json, index.html, .npmrc, .pnpmfile.cjs, .yarnrc.yml, android/app/build.gradle, src/../package.json, src/a.ts::$DATA, SRC~1/a.ts, "src/a.ts.", src/A.ts when src/a.ts exists -> refused, revision unchanged [+] src/App.tsx and public/logo.png accepted. P1b test-only kit whose vite.config.ts writes a canary to /src, /out/.., $HOME and a host-path env var, built through builds/web.py -> no canary on host, ctx unchanged (hash) [+] same config run by node outside Docker writes the canary. P1c AST: subprocess/os.system/ctypes CreateProcess only in builds/docker.py, device_host/launcher.py, runner/claude.py; sys.addaudithook during the whole suite logs every subprocess.Popen, argv[0] in {docker.exe, wsl.exe, claude} absolute [+] fixture module with an extra Popen is flagged by both checks |
| T2 | Malicious / hallucinated dependency | curated kit, approval bound to lockfile hash, minimumReleaseAge, ignore-scripts, offline build (3.3-3.4) | P2a src import of a non-kit package -> build fails closed naming it [+] import of `react` builds. P2b approval for lockSha A, lockfile edited to B -> install and build refused [+] unchanged A builds. P2c in the build container: `getent hosts registry.npmjs.org`, `getent hosts host.docker.internal`, TCP to a canary listener on WSL 127.0.0.1 and on 192.168.65.254 -> all fail [+] same probe container on `--network bridge` reaches the canary. P2d fixture package with postinstall writing a marker, installed through the install stage -> marker absent [+] same fixture with ignore-scripts=false in a throwaway container -> marker present. P2e local test registry: version published now -> refused by minimumReleaseAge [+] version dated 8 days ago accepted |
| T3 | Preview JS attacks studio | per-project origin p-<id>.localhost:17880, CSP HTTP header, sandboxed iframe, narrowed Tauri frame-src (4) | P3a page fetch + WebSocket to 127.0.0.1:17875 -> securitypolicyviolation and backend request counter unchanged [+] same page served by a fixture server without the CSP header reaches the backend (gets 401). P3b frame calls fetch(http://ipc.localhost/...), window.__TAURI_INTERNALS__.invoke('get_session'), forged parent.postMessage -> no token, no IPC [+] main window invoke('get_session') returns a session. P3c top.location= and window.open -> blocked [+] same script in an unsandboxed test iframe navigates. P3d page of A fetches http://p-B.localhost:17880/ and reads B storage -> blocked (CSP + Host check) [+] A fetches its own file. P3e every preview response (html, js, css, 404) carries the exact CSP and Permissions-Policy headers; page with inline `<script>` does not execute [+] same page from a header-less fixture executes it |
| T4 | Network exposure | listeners only on Windows 127.0.0.1:17875/17880, Host/Origin allow-list, NAT check (7) | P4a listener diff before/during a full session (backend, preview, build, device): Windows Get-NetTCPConnection, WSL default-ns `ss -ltn` via preflight, `docker ps` published ports -> only the two studio ports added [+] a test canary on WSL 127.0.0.1 appears in the Windows diff (proves relayed listeners are seen). P4b Host evil.com, 127.0.0.1.nip.io, p-<id>.localhost.evil, missing Host -> 421 [+] exact Host -> 200. P4c connect via LAN IP -> refused [+] 127.0.0.1 connects. P4d preflight with stub wslinfo printing `mirrored` (and a .wslconfig with networkingMode=mirrored) -> device features refused with reason [+] stub printing `nat` -> started; live value read in local gate |
| T5 | Prompt injection via logs, docs, screenshots, assets, rules files | minimal MCP catalogue, default-deny policy, owner-only approvals, control files unwritable, plan gate, runner flags (1, 3.2, 5.5, 6) | P5a catalogue snapshot: exact tool names AND every input schema (properties, types, enums, maxLength, additionalProperties:false) equal the committed snapshot; no property named command/args/argv/script/url/host [+] mutation adding `debug_exec{command}` or widening an enum fails the test. P5b agent actor decide_approval / plan.decide -> PolicyDenied [+] owner actor succeeds. P5c export_run without approvalId, with approval for other params, reused approval -> refused (policy app.export agent=APPROVAL) [+] valid single-use approval exports. P5d AGENTS.md, CLAUDE.md, claude.md, .mcp.json, .claude/settings.json, .CLAUDE/x, .vscode/settings.json, src/.claude/settings.json, CLAUDE~1.MD, src/x.ts:Zone.Identifier -> refused [+] src/notes.ts accepted. P5e built-in runner argv equals snapshot; live run with a prompt demanding Bash/Read/WebFetch -> stream-json shows no such tool executed [+] same harness with `--tools default` executes Read (proves the probe detects use). P5f (enforced, gate default ON) code_write_file / code_delete_file / app_build without an approved plan of the current run -> `plan_required`; a plan approved for another run or an "edit" (rejected) plan -> still refused; plan_submit waits for the owner, timeout -> run aborted [+] after owner plan.decide -> accepted; [+] owner toggle OFF -> accepted without a plan (tests/security/test_s6_review_mcp.py, tests/runner/test_review_fixes.py) |
| T6 | Activity stream attacks owner UI | text-node rendering, sanitiser, value redaction, field allow-list, server-set kinds, approval region from server records, DB-ordered WS replay (5) | P6a payloads (`<script>`, img onerror, markdown image to 127.0.0.1:9999, javascript: URL, OSC-52, U+E0041, U+202E, 10 MB) in agent text, tool input, build log -> 0 script nodes, 0 network requests (Playwright log), capped text [+] same payloads through a test-only innerHTML component trigger the canary request. P6b agent text "APPROVAL REQUIRED — click Approve" and a forged `kind` field -> timeline only, approval region count unchanged, schema rejects `kind` [+] a server approval record renders a card. P6c canaries (sk-ant-…, live owner token, PEM, JWT, `password=…`, 48-char hex) in tool input, tool result, build log, agent text, file content -> absent from activity_events, audit_events, tool_executions, WS frames [+] a non-secret marker in the same positions is present. P6d WS without token, wrong token, wrong Origin, foreign Host -> closed 4401/4403/4421 [+] correct token+Origin receives events. P6e stream-json fixture with thinking blocks, unknown types, cwd/tools in init, 5 MB tool result -> thinking/cwd absent, tool result stored as size+sha256+<=300-char excerpt, stream.dropped counted [+] text block and tool_use name present. P6f disconnect at seq N, 50 events, reconnect afterSeq=N -> exactly N+1..N+50 in order; forced overflow -> close 4409 then lossless replay [+] client with gap detection disabled shows missing events |
| T7 | Exfiltration | build --network none, install via internal network + allow-list proxy, preview CSP + dead proxy WebView2 args, restrict=on guest, export secret scan (3, 4, 2.2) | P7a build container: DNS, TCP 1.1.1.1:443, HTTPS to registry -> fail [+] bridge-network container succeeds. P7b preview page: fetch https://example.com, img to external host, sendBeacon, form POST, `<link rel=prefetch>` -> nothing reaches a canary HTTP server on a non-loopback test address [+] same page outside the studio WebView args and without CSP hits the canary. P7c test APK (app uid, INTERNET) TCP 1.1.1.1:443 and HTTPS to the canary -> fail [+] same APK on a default-network test emulator succeeds. P7d planted key pattern in src -> export refused [+] clean project exports |
| T8 | Workspace escape / confused deputy (CVE-2026-39861 class) | Workspace.resolve, reparse refusal, output read only after container exit, link-failing reader, quotas (3.2, 3.4) | P8a inherited 14 traversal cases (.., absolute, UNC, ADS, device names, .git/hooks) -> refused [+] normal path accepted. P8b build creates symlink, junction and hardlink in /out pointing to a host canary file -> build fails, nothing copied/served/exported, canary unchanged [+] a naive rglob+read_bytes reader (AWS execution.py:79-80 style) on the same out dir reads the canary. P8c 301st file, >4 MB src, >200 MB output -> refused [+] at-limit accepted. P8d output reader called while the container still runs -> refuses [+] after exit it reads |
| T9 | Generated APK reaches host tooling via 10.0.2.2 | emulator + studio adb in private user/net ns, restrict=on, wipe per session (2.2) | P9a test APK (uid u0_a*) sweeps 10.0.2.2:{every port listening in the WSL default ns, 5641, 5600, 5601, 8554, a random-port canary} -> all fail [+] the canary answers `curl` from the WSL default ns AND the same APK on a default-slirp, non-namespaced test emulator reaches it. P9b APK to Windows host via WSL gateway IP :17875/:17880/:135 -> unreachable [+] same targets reachable from the WSL default ns |
| T10 | Interference with owner's S9 work | aas-* AVDs only, fake HOME ~/.aas/home, own ANDROID_USER_HOME/AVD_HOME, adb 5641 in ns, read-only SDK use (2.2, 2.5) | P10a static scan of core/, wsl/, kit/, images/ for s9emu, debug.keystore, /.android, ~/android, 5037 -> none [+] fixture file containing `s9emu` is flagged. P10b devhost refuses unknown verb, AVD .ini with path= outside ~/.aas/avd, any symlink under ~/.aas, config.ini differing from manifest [+] valid aas-phone starts. P10c full device session: owner adb PID on 5037 (if running), mtimes of ~/.android/*, ~/debug.keystore, s9emu.avd/*, ~/android unchanged [+] the same checker detects a deliberate touch of a test file |
| M1 | Guest -> 10.0.2.2:5037 = owner's adb server -> control of all devices (critical) | adb and emulator in private ns; guest restrict=on; studio adb on 5641 inside ns (2.2) | PM1a APK sends `000chost:version` to 10.0.2.2:5037 (owner adb if running, else a canary bound on WSL 127.0.0.1:5037 for the test) and to 10.0.2.2:5641 -> no OKAY [+] `adb -P 5037 version` / canary answers from the WSL default ns, and the default-slirp test emulator gets OKAY (spike 1 result) |
| M2 | WSL loopback relayed to Windows loopback; containers reach it via host.docker.internal (critical) | nothing of ours listens in the WSL default ns; builds --network none; installs on --internal network (2.2, 3.3, 7) | PM2a during a device session the Windows listener set gains nothing from wslrelay [+] a test canary on WSL 127.0.0.1 appears on Windows 127.0.0.1 (relay works). PM2b build and install containers -> host.docker.internal:<canary> fails [+] bridge container reaches it (spike 2d) |
| M3 | wsl.exe / docker group = root in owner's distro; wrapper abuse (high) | single launcher, fixed argv allow-list, absolute wsl.exe, hash-pinned /opt/aas/bin, pid-ns lifetime (2.1-2.2) | PM3a AST: exactly one wsl.exe call site; `argv_for` rejects any verb outside {preflight, serve-phone, serve-tablet}, extra args, `-u`, `--exec`, `--cd` before spawn (Popen spy count 0) [+] mutation adding `-u root` to the template fails the snapshot. PM3b one byte changed in a temp copy of aas_devhost.py referenced by test config -> launcher refuses, no spawn [+] matching hash spawns. PM3c every argv token matches `^[A-Za-z0-9/._-]+$` and `bash -c 'printf %s\\n "$@"'` echoes it unchanged [+] a token with `;` fails. PM3d kill wsl.exe mid-session -> within 30 s no qemu/adb/aas_devhost process remains [+] a `setsid nohup` child started the spike way survives (proves the check sees orphans) |
| M4 | Build output / build-time code plants .claude/settings.json hooks, .mcp.json, CLAUDE.md for the next runner (high) | runner cwd = fresh empty dir outside workspaces; --strict-mcp-config, --setting-sources, fixed --settings; output reader fails on control-file names (3.4, 6.1) | PM4a build whose output contains .claude/settings.json, .mcp.json, CLAUDE.md -> build fails; runner start asserts cwd empty [+] `claude -p` in a fixture cwd holding that .mcp.json WITHOUT the studio flags lists the fixture server in system/init; with the flags it does not |
| M5 | DNS exfil via 10.0.2.3; WebRTC/form/prefetch bypass connect-src (high) | restrict=on guest (no 10.0.2.3); WebView2 dead proxy + WebRTC policy; form-action 'none' (2.2, 4) | PM5a APK resolves `<nonce>.canary.invalid` -> a canary DNS server set as the WSL resolver in the local gate logs no query [+] the default-slirp test emulator's query is logged. PM5b preview page: RTCPeerConnection with stun:<canary udp>, form POST, `<link rel=dns-prefetch/prefetch>` to the canary -> canary receives nothing [+] same page in a plain Chromium without the studio args and CSP reaches it |

B-item coverage: B1 -> P10c, PM1a; B2 -> P9a; B3 -> PM3a-PM3d; B4 -> P5e, PM4a; B5 -> this table; B6 -> P3e, P1a,
P5d; B7 -> PB7a (`gradlew --offline --dependency-verification strict assembleDebug` succeeds under --network none
[+] deleting one cache entry fails with "No cached version" — proves it is really offline), PB7b (APK signed by the
studio keystore cert sha256, never ~/debug.keystore [+] verification flags an APK signed by a test key); B8 -> P6c,
P6e; B9 -> PM2b.

## Residuals inherited from AI Website Studio (accepted until fixed there)
Docker Desktop VM is the isolation boundary; best-effort secret redaction vs agent-written code; unsigned
installer; no data volume quota. Emulator-in-WSL2 is not a vendor-supported configuration.
New (v2): R-EXT — an external Claude Code session's own shell runs as the owner: it can edit the data root, read the
backend's memory (owner token), and via wsl.exe (<owner-user> = docker group + passwordless `sudo -n runuser`) act as root
in the distro, including the owner's adb 5037 and s9emu. Mitigations in DESIGN.md sec. 6.2; accepted until the owner
chooses the kvm-group option (DESIGN.md sec. 9 item 3).
Manifest trust (S1): CI runs `scripts/check_vendored.py` without the AWS repository, so it trusts the
upstream_sha256 values recorded in VENDORED.json; only `check_vendored.py --upstream-repo <AWS repo>` on the owner PC
re-derives them from 25b3eab (also recorded as `residuals` in VENDORED.json; regenerate with scripts/gen_vendored.py).

## Review round 1 (Opus vault-reviewer, 2026-11-22) — verdict PARTIAL, implementation blocked
Verified environment facts: WSL networking `nat` (wslinfo), WSL 2.6.3. Owner's adb server listens on WSL
127.0.0.1:5037 and is relayed by wslrelay.exe to Windows 127.0.0.1:5037 (answers host:devices). Other WSL
listeners: 0.0.0.0:8080 (open-webui, root), 127.0.0.1:11434 (ollama), 1080, 9001, *:8123 (Docker-published).
emulator has no flag to block host loopback/egress. User <owner-user> in docker group; sudo needs password.

New attack paths:
- M1 critical: guest -> 10.0.2.2:5037 = owner's adb server -> shell/install/pull on every attached device.
- M2 critical: WSL loopback relayed to Windows loopback; bridge containers may reach it via host.docker.internal.
- M3 high: wsl.exe (and docker group) = root in owner's distro; wrapper must be fixed-argv allow-list + hashed script.
- M4 high: build output / build-time code planting .claude/settings.json hooks or .mcp.json for the next runner.
- M5 high: DNS exfil via 10.0.2.3 -> WSL resolver; WebRTC/form/prefetch bypass connect-src in preview.

Corrections to T1-T10 (probes must have positive controls; vacuous ones rewritten):
- T1: positive allow-list (src/**, public/** by extension), case-insensitive; index.html studio-owned; block
  .pnpmfile.cjs/.yarnrc.yml; P1b canary in a vector that executes; P1c via AST + sys.addaudithook.
- T2: P2c tests DNS + host.docker.internal; P2d paired with scripts-enabled positive control.
- T3: P3b must fetch ipc.localhost and forge postMessage; CSP must be an HTTP header from preview server
  (inherited meta-only CSP, renderer.py:28 / preview_server.py:98-107); frame-src narrowed.
- T4: P4d reads `wslinfo --networking-mode`; P4a enumerates WSL + Docker-published listeners.
- T5: P5a asserts input schemas, not names; new operation for export (policy.py:48 allows project.export);
  P5d covers case/ADS/8.3 variants.
- T6: value-pattern redaction (audit.redact is key-name only, audit.py:22-24); stream-json field allow-list per
  event type, drop thinking, tool results as size+sha256+capped redacted excerpt; WS gap detection on replay.
- T7/T9: isolation = emulator + studio-only adb server (-L localfilesystem:) in own network namespace
  (root helper or `unshare -rn` if allowed); gRPC off or -grpc-use-jwt; screen over wsl.exe stdio; guest wiped
  per run. P9a: app-uid APK, positive control, canary listener on random port, sweep every listener.
- T8: never read build output while container runs; reads must not follow links (execution.py:291-292).
- T10: separate HOME / ANDROID_USER_HOME / adb server; dynamic test with fake HOME and owner adb PID unchanged;
  .ini path= confined to studio AVD home.
- Gradle offline image: JDK 21, SDK platform/build-tools for Capacitor 8 compileSdk (host SDK only has
  android-33), licences, wrapper dist, pre-resolved cache (--offline, GRADLE_RO_DEP_CACHE, verification-metadata),
  studio debug keystore mounted read-only, memory > 2g.

Blocking items before implementation (in order):
B1 stop using owner's adb server and ~/.android (spike included); B2 network-namespace isolation prototype;
B3 wsl.exe wrapper spec; B4 runner flags for D5 + document what external agent shell reaches;
B5 rewrite vacuous probes; B6 CSP header + positive file allow-list; B7 offline Gradle image design;
B8 stream-json allow-list + value redaction; B9 host.docker.internal -> wslrelay probe.

## Review round 2 (Opus vault-reviewer, 2026-11-23) — verdict PARTIAL -> implementation may start; decisions by main session
Closed: B1, B2 (live PM3d pending), B3, B5 (PM1a [+] must re-run live; PM3b uses in-process monkeypatch, never a
production path override), B6, B7, B8, B9 (live run: --internal network and --network none cannot reach WSL loopback;
default bridge can), M1 (live [+] for 10.0.2.2:5641 in local gate), M2, M5 (PM5a uses emulator -dns-server <canary>).
Decisions (supersede earlier text where they conflict):
- R-EXT corrected (N1): <owner-user> has `(ALL) NOPASSWD: ALL` (/etc/sudoers.d/<owner-user>, 2026-10-24) and is in group docker,
  so any process running as <owner-user> in the distro is root. Script hashes in /opt/aas are DRIFT DETECTION ONLY, not a
  boundary (M3 hash part = ACCEPTED-RESIDUAL). Adding <owner-user> to group kvm does NOT reduce R-EXT; it only removes the
  sudo call from the launcher. Preflight records `sudo -n -l` and the residual text must match it.
- External MCP path (N2/Q3): OFF by default in v1. The ai-app-studio MCP server refuses every tool until the owner
  enables it in the studio UI; the toggle shows the R-EXT text. The built-in runner is the default and only
  unattended path. Probe PX1: tools refused when disabled; [+] enabled works.
- N3 added to PM4a/P5e: read the raw stream-json system/init; assert mcp_servers == ["ai-app-studio"] and tools are a
  subset of the catalogue; [+] fixture CLAUDE_CONFIG_DIR with a user-scope MCP server and a CLAUDE.md marker shows
  both appear without the runner flags.
Slice prerequisites (must exist before the named slice, not before first commit):
- N4 (launcher slice): Popen(wsl.exe) with a minimal env (no BASH_ENV/WSLENV) and fixed cwd; probe BASH_ENV canary
  with WSLENV=BASH_ENV/u must not run; [+] runs without the scrub.
- N5 (install slice): proxy refuses CONNECT example.com:443, registry.npmjs.org:22, IP literals, host.docker.internal
  (403); [+] registry.npmjs.org:443 = 200.
- N6 (CI slice): VENDORED.json carries a per-file patch list with reasons; unlisted change fails CI, listed passes;
  track AWS upstream security fixes.
- N7 (device-stream slice): stdout always drained, bounded queue that drops video first, h264 decoded in a worker;
  probe: video client stalled 10 s, tap replies < 1 s; [+] unbounded queue stalls.
- N8 accepted residual: -gpu host and shared X11/Wayland only matter after a VM escape (already <owner-user>/root).
