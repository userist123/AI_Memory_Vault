# S9c — generated app as a Windows desktop app (2026-11-23)

Branch `claude/ai-app-studio`, local commits only. Owner goal: apps for laptop/desktop, not only phone/web.
TDD: `tests/wiring/test_s9c_desktop.py` (collection error: no `aas_build.desktop`) and
`tests/ui/unit/s9c_export_desktop.test.ts` (no `export-desktop` button) were written first.

## Design (decided)
- **Studio-owned wrapper template** `images/desktop-wrapper/`: `Cargo.toml` (exact pins `tauri =2.12.7`,
  `tauri-build =2.7.6`, the same versions as the studio shell; feature `custom-protocol` on by default so the exe
  embeds the assets and never uses a dev server), committed `Cargo.lock` (433 packages, derived offline from the
  studio shell lock), `build.rs` (`AppManifest::commands(&[])`: no app commands), `src/main.rs`
  (`Builder::default().run(generate_context!())`: no invoke handler, no plugins), icons. No `capabilities/` dir and
  no `tauri.conf.json` in the template: the studio writes the config.
- **Studio-written config** (`core/aas_build/desktop.py`): `tauri.conf.json` with `identifier`
  `dev.aiappstudio.p<id16>`, product name = sanitised project name (`[A-Za-z0-9 _-]`, <= 40), `frontendDist
  ../dist`, `withGlobalTauri false`, `security.capabilities []` (no IPC permission at all), CSP = the kit CSP
  (`connect-src 'self'`, `object-src 'none'`, `frame-src 'none'`, `form-action 'none'`, `base-uri 'none'`;
  without `frame-ancestors`: the app is the top-level document), `dangerousDisableAssetCspModification false`,
  `bundle.active false`, WebView2 args = the studio window's (wry defaults + dead proxy `127.0.0.1:9` +
  `disable_non_proxied_udp`). `.cargo/config.toml`: crates-io replaced by the vendored directory, `net.offline`.
- **Agent input = the dist only.** The fresh build dir is `<desktop root>/builds/d<hex>/{wrapper,dist}`; the
  template files are copied byte-for-byte (links refused), the dist is the current preview site re-validated with
  the web output reader (`runner._scan`: name and extension allow-list, no link/reparse point/hard link, caps) and
  read with `O_NOFOLLOW` + inode check. A `build.rs` or `Cargo.toml` in the site fails closed; dist files cannot
  reach `wrapper/` (sibling dir).
- **Vendored crates.** `cargo vendor --locked --versioned-dirs <desktop root>/vendor` once (network only for that
  step, from `images/desktop-wrapper`; 432 crates, 564 MB). Cargo verifies every vendored crate against the
  checksum in the committed `Cargo.lock` (`.cargo-checksum.json`) - no other trust in the directory.
  Desktop root = `AAS_DESKTOP_ROOT` (absolute) or `<data root>/desktop` with `vendor/`, `target/` (shared: the
  ~430 dependencies compile once), `cargo-home/` (empty, studio-owned: the owner's `~/.cargo` config and
  credentials are not read), `tmp/`. A missing vendor dir -> `Unavailable` with the one-time step.
- **Host build** (`runner.run_desktop_build`, the only process starter, P1c): argv `[<toolchain>/cargo.exe, build,
  --release, --offline, --locked, --quiet, --color, never]`, cwd = wrapper, `shell=False`, stdin null. The real
  `cargo.exe` of the toolchain (`AAS_CARGO` or `rustup which cargo`), not the rustup proxy. Env allow-list:
  OS/MSVC lookup variables (SystemRoot, ProgramFiles*, ProgramData, PROCESSOR_*, NUMBER_OF_PROCESSORS, ComSpec),
  PATH = toolchain bin + System32, RUSTC, CARGO_HOME, CARGO_TARGET_DIR, TEMP/TMP (studio dirs),
  `CARGO_NET_OFFLINE=true`, HTTP(S)/ALL proxy = `http://127.0.0.1:9` (a build-script fetch hits a dead port). No
  USERPROFILE, tokens, RUSTFLAGS, RUSTC_WRAPPER, registry tokens. Timeout 1800 s, process tree killed
  (`taskkill /T /F`); log tail capped (1 MiB ring); the exe is read only after cargo exited: `target/release`
  and its parent not links, the exe a regular PE (`MZ`), not a link, nlink <= 2 (cargo uplifts by hard link),
  <= 200 MiB, inode unchanged while read; copied atomically.
- **Service** `service.build_desktop` (MCP `app_build target desktop`, plan-gated like web/android, inside
  `Studio.operation` policy `app.build` + audit): web build required first; 5 GiB disk gate (data + system drive
  + desktop root drive); same process-wide build lock as web/android; events build.started/log/error/finished
  `{target desktop, artifactSha256, sizeBytes}` with the sha in the typed artifact column. Outputs:
  `<preview>/<project>/app-desktop.exe` and `app-desktop.dist.zip` (deterministic zip of the exact embedded dist).
- **Export** `app_export target desktop` (MCP), REST `target: desktop`, UI button "Export desktop (exe)": the exe
  must match its recorded `build.finished` sha256; the secret scan covers the raw exe AND every file of the
  embedded-dist zip (Tauri stores the assets brotli-compressed inside the exe, so the raw bytes alone would hide a
  planted key; test). Approval bound to {projectId, target, sha256} as for web/android; written as
  `<name>-rev<N>-<sha12>.exe`.
- **NSIS bundle: not built.** `tauri bundle` needs tauri-cli and downloads NSIS at bundle time (not offline-capable
  with the pinned toolchain); v1 artifact = the unsigned exe. Requires WebView2 runtime on the target PC (Windows
  10/11 ship it).
- **Launch for the owner: not built** (optional item). Open: owner-only REST + UI button, never an agent tool.

## Tests
`tests/wiring/test_s9c_desktop.py`: argv fixed offline/locked; env minimal (planted secrets/RUSTFLAGS/RUSTC_WRAPPER
/USERPROFILE absent, proxy sinkholed, toolchain first on PATH); cargo config vendored + offline; run_desktop_build
(fake Popen: cwd, env, exe read after wait); collect_exe refuses non-PE, oversize, symlink; template pins and no IPC
surface; conf identifier/CSP/capabilities/dist; web build required; agent files cannot override the template (byte
compare) and a `build.rs` in the dist fails closed; artifact sha + events + build dir removed; failed build keeps no
exe; missing vendor dir; disk gate; export approval bound to sha (swapped exe refused); embedded-dist scan blocks a
planted key; MCP app_build desktop plan-gated then called; real build (skipped unless `AAS_DESKTOP_REAL_ROOT`).
`tests/security/test_mcp_app.py` catalogue: target enums gain `desktop`. vitest `s9c_export_desktop.test.ts`.

## Real build
Blank template (index.html only), `AAS_DESKTOP_ROOT=D:w_aas_desktop`, toolchain cargo 1.94.0, MSVC: first build
**220 s** (all ~430 vendored crates, LTO, `opt-level s`), exe **3,143,168 bytes** (3.0 MiB, PE, unsigned);
second build with the shared target dir **58 s** (only the app crate + LTO). Succeeded with `--offline`, the
vendored source and the dead proxy, i.e. no network was needed. The exe sha256 differs between builds (build-dir path
and timestamps are not normalised; not bit-reproducible). Export scan of the real exe: no false positive. The exe was
not launched (owner action). Target dir after both builds: ~1.1 GB on D:.

## Open
- Launch button (owner only); NSIS bundle once an offline NSIS is pinned; code signing (residual "Unsigned exe").
