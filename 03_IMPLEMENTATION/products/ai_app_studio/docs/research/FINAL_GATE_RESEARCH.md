# Final gate — outside research (2026-11-23). UNTRUSTED until verified.
Codex task b59ec8b4 (PARTIAL), Gemini task 6e56a34e (COMPLETED). Raw sha256: codex 04e07887…, gemini 04893fd1….

## Actionable claims (verify each against the cited primary source before relying on it)
Tauri/WebView2 (Codex)
- GHSA-57fm-592m-34r7: cross-origin iframe IPC fixed in Tauri >= 2.0.0-beta.20; on Windows a SAME-origin iframe can
  still invoke. Window-label checks do not distinguish subframe vs top frame. -> keep preview cross-origin; add a
  command-side origin guard if feasible; real-WebView2 probe (B2).
- Custom additionalBrowserArgs REPLACE wry defaults incl. `--disable-features=msWebOOUI,msPdfOOUI,msSmartScreenProtection`
  -> re-add them.
- Dead proxy is NOT a reliable egress block (Chromium bypasses proxy for loopback/link-local; `<-loopback>` rule
  exists). DisableNonProxiedUdp limits WebRTC only. -> rely on CSP + document residual; consider `<-loopback>`.
Claude Code CLI (Codex)
- Documented: `--no-session-persistence`, env `DISABLE_AUTOUPDATER`; `MCP_TOOL_TIMEOUT` default 100,000,000 ms.
- Empty `--setting-sources` must NOT be assumed to stop `~/.claude/CLAUDE.md` loading (public issue reports).
  -> verify on the installed CLI with a marker; consider a studio-owned config dir if login can be preserved.
Windows named pipe (Codex)
- Python multiprocessing Listener: HMAC-SHA256 on current Python; no ACL control; pipes reachable over network
  unless rejected. -> native pipe with DACL limited to the logon SID + PIPE_REJECT_REMOTE_CLIENTS.
Android emulator (Gemini)
- Console auth via ~/.emulator_console_auth_token; a 0-byte file DISABLES auth (check studio token non-empty).
- gRPC server may start by default on console+3000 (8554); no -no-grpc; use -grpc-use-jwt / -grpc-use-token; keep
  inside netns. -> verify on 31.3.9 and add -grpc-use-jwt or confirm it is off.
- restrict=on blocks 10.0.2.2/10.0.2.3/egress (libslirp); apply to BOTH NICs and add ipv6=off;
  `adb reverse` bypasses slirp via QEMU pipe -> studio adb never issues reverse; refuse it in devhost.
- `-gpu host` exposes host gfxstream/virglrenderer + WSL /dev/dxg -> Windows GPU driver to guest streams
  (CVE-2025-2509, CVE-2022-0135 virglrenderer). -> prefer `-gpu swiftshader_indirect`/`guest` (spike: swiftshader
  showed a white frame — re-test) or record residual.
- Graceful shutdown crash on nested KVM tied to snapshot save -> `-no-snapshot`.
Gradle/Capacitor (Gemini) — VERIFY, ids look unusual
- Capacitor CVE-2026-103922 / GHSA-rvm3-566m-v7fv fixed in 8.3.5/8.4.3/8.5.1 (kit pins 8.6.2 -> OK if true).
- Gradle CVE-2026-22816, CVE-2026-22865 (fixed 8.14.4), GHSA-j5m7-59rp-24f5, GHSA-xwqc-3h47-hg64,
  GHSA-mvvg-497x-hmj8 (fixed 8.14.6) -> image Gradle must be >= 8.14.6.
- TOFU avoidance: PGP verify-signatures + curated keyring + keyservers disabled; aapt2 override.
WebCodecs (Gemini)
- `hardwareAcceleration: "prefer-software"` keeps H.264 parsing in the sandboxed renderer instead of the GPU
  process; 2026 WebCodecs CVEs (CVE-2026-11683, -7933, -5888, -5280, -3544). -> use prefer-software + NAL allowlist
  (1,5,7,8), SPS bounds, AU size cap, require SPS+PPS+IDR first.
WSL2 (Gemini)
- swap at %Temp%\swap.vhdx; crash dumps %Temp%\wsl-crashes, `.wslconfig` [wsl2] maxCrashDumpCount (default 10),
  swap, swapFile, memory. Piped core_pattern: RLIMIT_CORE=1 is the helper recursion guard (aborts) — our
  `ulimit -c 1` relies on that; Gemini recommends `ulimit -c 0` + non-pipe pattern (disagreement, verify).
- ext4.vhdx grows; reclaim with fstrim + wsl --shutdown + Optimize-VHD; sparse mode has corruption warnings.
  -> owner-machine decisions, documented in SECURITY.md (not changed automatically).
