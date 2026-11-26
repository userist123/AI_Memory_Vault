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
  checksum in the committed `Cargo.lock` (`.cargo-checksum.json`): this detects damage only, not tampering by an
  account that can write the dir (see "S9c review fixes": owner-only root + studio-held hash manifest).
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
Blank template (index.html only), `AAS_DESKTOP_ROOT=D:\w\_aas_desktop` (moved to `D:\aas-desktop` in review 2), toolchain cargo 1.94.0, MSVC: first build
**220 s** (all ~430 vendored crates, LTO, `opt-level s`), exe **3,143,168 bytes** (3.0 MiB, PE, unsigned);
second build with the shared target dir **58 s** (only the app crate + LTO). Succeeded with `--offline`, the
vendored source and the dead proxy, i.e. no network was needed. The exe sha256 differs between builds (build-dir path
and timestamps are not normalised; not bit-reproducible). Export scan of the real exe: no false positive. The exe was
not launched (owner action). Target dir after both builds: ~1.1 GB on D:.

## Open
- Launch button (owner only); NSIS bundle once an offline NSIS is pinned; code signing (residual "Unsigned exe").

## S9b review fixes (own commit, sent by the main session)
TDD: `tests/wiring/test_s9b_review_fixes.py` (6 failed: no `build_lock`, hard link accepted, link chain checked after
the approval, markers not escaped, console in the JSON body), `tests/unit/test_s9b_review_runtime.py` (4 failed),
`tests/ci/test_s9b_review_ci.py` (2 failed) -> all passed.
- **M1** build lock across processes: `service.build_lock` = in-process lock + OS lock on `<data>/build/build.lock`
  (`msvcrt.locking` / `flock`; released by the OS when the holder dies) + `build.holder` (project id). Web, android
  and desktop builds use it; `DELETE /projects/<id>` asks `building_project(settings)` (any process) -> 409 for the
  project (or an unknown holder). Test: a second Python process holds the lock -> delete 409, a build in this process
  refused, released after; a killed holder leaves no stale lock (Windows releases it shortly after the kill).
  S9b.md and SECURITY.md wording corrected (the S9b lock was per process).
- **L1** export reader: `os.stat(path, follow_symlinks=False).st_nlink` (DirEntry.stat() reports 0 on Windows); a
  hard-linked file in the site is refused.
- **L2** export write: the folder chain is checked for links/junctions before the approval is consumed and before any
  mkdir (and again after); a junction in the parent path -> refused, nothing created through it.
- **L3** `scripts/build_runtime.ps1`: app sources = `git ls-files -z` of the listed dirs (no untracked/ignored file);
  `uv export` keeps hashes and `uv pip install --require-hashes`; reparse-point check over the whole stage (python/,
  ms-playwright/, app/); `runtime.version` = full runtime.zip sha256; the shell (lib.rs) checks it is a sha256,
  hashes runtime.zip with `%SystemRoot%\System32\certutil.exe -hashfile` before extracting and refuses a mismatch
  (extraction dir = first 16 hex). Integrity check against damage/partial copies, not authenticity (both files sit
  in the same install dir; signing = residual "Unsigned installer"). cargo test --lib 6 passed (new
  certutil-parse test). Not re-run live: build_runtime.ps1 (needs ~250 MB on C: staging; C: 6.9 GB).
- **L4** CI: `.gitleaks-tests.toml` (default rules; allowlists = exact path + one rule: the JWT canary, the devhost
  fake token, the planted AWS key of the export test; `__pycache__`) and a second gitleaks step over
  `$PRODUCT_DIR/tests`; positive control: a new AWS-shaped key in a test file is found. actionlint = release binary
  1.7.12 sha256-verified (actionlint-py is sdist-only, its build downloads the binary unverified); zizmor wheel
  installed with `--require-hashes --only-binary :all:`. A pre-existing YAML error (unquoted `:` in the vendored job
  name) fixed; actionlint 0, zizmor 0 locally.
- **L5** sight: `<<`/`>>` inside untrusted lines are broken up (`< <`), so data cannot fake the end/start marker;
  `preview_capture` keeps console lines only in the UNTRUSTED block (`consoleLines` count in the JSON body).

## S9c review fixes (own commit, sent by the main session)
TDD: `tests/wiring/test_s9c_review_fixes.py` written first (fixture error: no `desktop.secure_dir`; then 14 failed
against the first implementation attempt until each fix landed); `test_s9c_desktop.py` adapted to the new layout.
- **M1 pair binding.** A build writes `app-desktop.exe` + `app-desktop.dist.zip` into a fresh staging dir
  `<preview>/<project>/.desktop-<hex>/`, hashes both, and puts them in place with ONE directory rename to
  `<preview>/<project>/desktop/<buildId>/`. `build.finished` records `artifactSha256` (exe), `distSha256`, `buildId`
  (activity schema gained `distSha256`, `buildId`); older pairs are pruned only after the record exists. Export reads
  the pair named by the latest successful record and checks exe sha256 AND zip sha256 against that same record before
  scanning. Tests: the second build's rename fails -> BuildFailed, export still ships the whole first pair; no
  previous build -> export refuses; a different valid zip beside the unchanged exe -> refused.
- **M2 cargo config planting / root ACL.** Build refused (`Unavailable`) when any parent of the build dir has
  `.cargo/config(.toml)` (or a `.cargo` link/file). cargo runs with `--config build.rustc-wrapper=""`,
  `build.rustc-workspace-wrapper=""`, `build.rustflags=[remap...]` (highest precedence), no linker override; CARGO_HOME
  (`<root>/cargo-home`) gets a studio-written `config.toml` each build. `assert_root_trusted` (ctypes
  GetNamedSecurityInfo, no new dependency) refuses a root whose owner is not the user/SYSTEM/Administrators, that has
  any non-trusted allow ACE with write rights (inherit-only ones included: they reach the children), or whose parents
  (up to, not including, the drive root) give others delete-child / DELETE / write-dac / write-owner / full control /
  add-file / add-subdirectory (review 2 F1, N3). Fresh
  roots are created owner-only with `python -m aas_build.desktop secure`. Residual: a `[target.*]` linker or
  `[env]` set in a config that cannot be reached by `--config` is covered only by the refusal above.
- **M3 vendored crates.** vendor/ and target/ live under the owner-only root. `record_vendor` hashes every file of the
  vendor tree (sha256, no links) into `<data>/desktop-vendor.manifest.json` (outside the vendor dir) right after
  `cargo vendor` (`python -m aas_build.desktop record-vendor`); every build re-hashes the tree and refuses a changed,
  added or removed file or a missing manifest. Wording corrected: Cargo's `.cargo-checksum.json` check detects
  *damage* only (it sits in the same writable dir as the crate), tampering by a writer is detected by the
  studio-held manifest, and is prevented by the owner-only ACL. Dev dir `D:\w\_aas_desktop`: ACL reset to
  `Administrators`, `SYSTEM`, `<owner>` full control with inheritance removed (`icacls /reset` +
  `/inheritance:r` + `/grant:r`, children reset to inherit); manifest recorded after that.
- **L1 path remap.** `--remap-path-prefix` for home -> `~`, root -> `/aas-desktop`, target -> `/target`, vendor ->
  `/vendor`, build dir -> `/src` (both slash forms). After the build the exe is scanned for the vendor/build/root/home
  prefixes (ascii + utf-16, case-insensitive); a hit fails the build closed (unit tests with fake exes).
- **L2 build probe.** `building_project` no longer takes the OS lock (a probe that locked, even briefly, made a build
  starting at that moment fail with "already running"): on Windows it reads the lock byte through a second handle (a
  read of a byte locked by another process fails), on POSIX it checks the pid in `build.holder` (`<pid>:<project>`).
  The holder file replace/unlink retries while a probe has it open. Tests: the probe never calls the lock; a
  hammering probe thread never makes 150 consecutive `build_lock` acquisitions fail.
- **Real check (2026-11-23).** `AAS_DESKTOP_ROOT=D:\w\_aas_desktop`, blank template, studio `--config` overrides
  (flags changed -> full rebuild): 100 s, exe 3,140,608 bytes; the exe has no `_aas_desktop`, `C:\Users`, or the
  account name (ascii and utf-16), `host_path_leaks` = []. `rustc-wrapper=""` accepted by cargo 1.94. Manifest
  hashing of the 23k-file vendor tree: 6.4 min cold once, ~21 s warm per build.

## Review 2 fixes (2026-11-23)
- **F1 parent DELETE.** `_REPLACE` includes DELETE (0x10000): a parent where another account may delete (e.g. `D:\w`,
  Authenticated Users modify) lets it rename the root away and plant its own, so it is refused. Dev root moved from
  `D:\w\_aas_desktop` (now refused) to **`D:\aas-desktop`** (created with `python -m aas_build.desktop secure`:
  inheritance removed, only the owner, SYSTEM, Administrators); vendor/ (23,188 files) and target/ moved with
  `robocopy /MOVE`, manifest re-recorded: same tree sha256 `21b324fb...3b1e` as before the move (verified at the old
  path first). Use `AAS_DESKTOP_ROOT=D:\aas-desktop`.
- **N3.** Add-file / add-subdirectory for another principal on any parent is untrusted too (a planted `.cargo` config);
  the parent `.cargo` refusal and the root ACL check run again right before the cargo spawn (after the slow vendor
  hashing).
- **N4.** `AAS_DESKTOP_ROOT` unset and < 10 GB free on the data drive: the desktop build is refused with a message to set
  `AAS_DESKTOP_ROOT` to an owner-only folder on another drive.
- **F2.** No per-build `build_dir -> /src` remap: the build dir sits under root (`/aas-desktop/builds/<id>`), so the
  rustflags (part of cargo's fingerprint) are identical across builds; a build dir outside root is refused; the exe
  leak scan still checks the build dir.
- **F3.** `DELETE /projects/<id>` holds the build lock from the probe through `capture.forget_project`; a build that
  started after the probe (any process) -> 409 `RunInProgress`.
