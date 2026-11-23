# Security audit - AI App Studio - 2026-11-23

Scope: local-first Windows desktop app. Tauri 2 shell (`apps/desktop/src-tauri`), React UI (`apps/desktop/src`), Python
FastAPI backend on loopback (`apps/backend`, `core/*`), preview server (`core/aas_build/preview_server.py`), Claude Code
runner (`core/aas_runner`), offline Docker / host cargo builds, WSL2 emulator. Branch `claude/ai-app-studio`, HEAD
`53c7a88a7`. Audit only: no code was changed by this audit (another agent edits code in parallel). The audit reuses
`docs/SECURITY.md`, `docs/THREAT_MODEL.md`, `docs/FINAL_GATE_ROUND1.md`, `docs/slices/*.md` and `VENDORED.json`, and
re-verified the rows below against the code and fresh command output.

Single principal model: the owner (bearer token, 256 bit, handed over on stdin) and the agent (MCP calls bound to one
project by a per-run grant). There are no user accounts, passwords or cookies, so checks 7 and 8 are N/A and checks
6, 13 and 15 are applied in their desktop/loopback form.

## Commands run (exit codes)

Temp dir `D:\w\_aas_tmp_audit` (TMP/TEMP), deleted at the end.

| # | Command (from the product dir unless noted) | Exit | Key output |
|---|---|---|---|
| C1 | `gitleaks dir . --redact` (default rules, whole working dir incl. untracked `.venv`, `target`, caches) | 1 (leaks) | 20 hits: 13 in untracked `.venv/` / `target/` / `.pytest_cache` / `__pycache__`, 3 in tracked test fixtures (below) |
| C2 | `gitleaks git . --redact --log-opts="-- ."` (default rules, 44 commits) | 1 | 3 hits, all synthetic fixtures: `tests/wiring/test_s9b_export.py:104` (aws-access-token, commit 28d1648b0), `tests/local_gate/test_devhost_unit.py:386` (generic-api-key, 964b3bc82), `tests/security/test_activity.py:22` (jwt, aec5dbfe8) |
| C3 | `git archive HEAD <product>` then `gitleaks dir --config .gitleaks.toml` (repo root config, as CI) on the tracked tree | 0 | no leaks |
| C4 | same tracked tree, `gitleaks dir --config .gitleaks-tests.toml` (default rules + 3 exact path/rule fixture allowlists) | 0 | no leaks |
| C5 | `gitleaks git --config .gitleaks-tests.toml --log-opts="-- <product>"` (repo root) | 0 | no leaks |
| C6 | `gitleaks git --log-opts="--all -- <product>"` (repo root, repo config) | 0 | no leaks |
| C7 | `git log --all --name-only -- .` grep for `.env`, `.pem`, `.key`, `.pfx`, keystore, `api_token`, `.bak`, `.db`, `.log`, `.map` | 0 | no such file ever committed |
| C8 | `git log --all -p -- .` grep for `sk-ant-api`, `AKIA...`, `ghp_...`, PEM headers | 0 | 9 lines, all in `tests/` (7 test files, redaction / export-scan canaries) |
| C9 | `pnpm audit --prod` and `pnpm audit` in `kit/` | 0 / 0 | No known vulnerabilities found |
| C10 | `pnpm audit --prod` and `pnpm audit` in `apps/desktop/` | 0 / 0 | No known vulnerabilities found |
| C11 | `uv audit --frozen` (uv 0.13.9, experimental command) on `uv.lock` | 0 | no known vulnerabilities in 66 packages |
| C12 | OSV `querybatch` API (api.osv.dev) for every registry crate of `apps/desktop/src-tauri/Cargo.lock` (449) and `images/desktop-wrapper/Cargo.lock` (432); script in the temp dir | 0 | 3 advisories each, all informational / Linux-only (see check 18) |
| C13 | `.venv/Scripts/python.exe -m pytest tests/security tests/build/test_preview_server.py -q -p no:cacheprovider` | 1 | 210 passed, 1 failed: `test_main_binds_loopback_only_and_reads_token_from_stdin` - port 17875 already taken by another studio backend running on this machine (environmental, not a regression) |
| C14 | in-process probe (`fastapi.testclient`, temp data dir) against `create_app()` | 0 | authed 200 has no `nosniff`/`no-store`; no token 401 `{"error":"unauthorized"}`; foreign Origin 403; `openapi.json` without token 401; 422 body echoes the offending input |
| C15 | read-only `git grep` for XSS sinks, raw SQL, `shell=True`, `pickle`, `yaml.load`, `verify=False`, `0.0.0.0` | 0 | no XSS sinks, no raw SQL from input, no `shell=True`; `0.0.0.0` only in `images/proxy/proxy.js:10` (container-internal) |

Scanners: `gitleaks` 8.30.1 is on PATH. `osv-scanner` is NOT installed (not on PATH, not in any `D:\w\_aas_*` tool dir);
it was not installed. Substitutes: `pnpm audit` (npm advisory DB), `uv audit` (PyPI/OSV), the OSV REST API for the two
Cargo.lock files. `cargo audit` is not installed either.

## Findings fixed

None by this audit (audit-only scope). Earlier rounds fixed B3, B4, B6, B8, B9, B11, B12 and the S9a/S9b/S9c review
findings (see `docs/FINAL_GATE_ROUND1.md`, `docs/slices/S9*.md`).

Follow-up (final fix slice, 2026-11-23, after this audit): O1 closed by the B2 probe (PASS, residual recorded), O3 by
the B1 real run (PASS after the auto-memory fix), O2 by the B7 probe (PASS, no fix needed), O6-O8 fixed, O9 accepted.
Status per row in the "Status" note under the table and in `docs/FINAL_GATE_ROUND2.md`.

## Open findings

| # | Severity | Finding | Why open | Owner action needed |
|---|---|---|---|---|
| O1 | high | B2: `get_session` (returns the owner token) checks only the window label `main` (`apps/desktop/src-tauri/src/lib.rs:441-444`), not the calling frame origin. Previews are cross-origin (`p-<id16>.localhost:17880`) and sandboxed (`DeviceFrames.tsx:62`), which should keep agent-generated pages away from IPC, but the real-WebView2 iframe probe (`__TAURI_INTERNALS__.invoke`, `window.ipc`, `chrome.webview.postMessage`, IPC host fetch) has never run | needs the real packaged shell | run the B2 probe before release; if any channel reaches IPC, add a frame URL/origin check in `get_session` |
| O2 | high | B7 / runner profile: the runner keeps the owner's real `USERPROFILE`/`HOME`/`APPDATA`; only CLAUDE.md memory and session transcripts were probed. Owner hooks, plugins, skills and agents under `%USERPROFILE%\.claude` were not probed; one the installed CLI still loads would run with the owner's rights inside an agent run | recorded residual (`docs/SECURITY.md` §2 "Runner profile") | extend the marker probe (fixture hook/plugin/skill/agent + positive control) or run the CLI with a studio-owned profile holding only credentials |
| O3 | high | B1: no real (paid) agent run yet. The isolation claims (no tool_use outside the catalogue, no owner memory text, plan wait survives) are tested only against a fake CLI / loopback API | needs a budget-capped live run | one budget-capped run with an adversarial prompt |
| O4 | medium | Build images stale: `aas-android-build:dev` still runs Gradle 8.14.3 (sources pin 8.14.8), `aas-web-build:dev` predates the `tsc --noEmit` step (DIGESTS.json `_must_do_s9`, `_must_do_s9b_web_build`) | C: had < 10 GB free | rebuild both images, re-check Gradle verification metadata, record image ids |
| O5 | medium | Release gate evidence incomplete: CI workflow `.github/workflows/ai-app-studio.yml` never ran on GitHub; installed-mode end-to-end (install, first launch, capture) not run; local-gate live receipt not refreshed; final gate round 2 pending | ordering of the release plan | run them at the release SHA |
| O6 | low | API success responses carry no `X-Content-Type-Options: nosniff` / `Cache-Control: no-store` (only rejects and captures do; C14). JSON over loopback behind a bearer token, so exposure is minimal | hygiene | optional: add both headers in `BoundaryMiddleware` for every response |
| O7 | low | FastAPI 422 bodies echo the rejected input (C14). Only the token holder can reach them | hygiene | optional: custom `RequestValidationError` handler without `input` |
| O8 | low | No cap on the number of approval / plan records an agent run can create (bounded indirectly by `--max-turns` / `--max-budget-usd`, `core/aas_runner/claude.py:92`) | hygiene | optional: per-run cap on pending approvals |
| O9 | low | Rust advisories, informational only: `chrono 0.4.45` RUSTSEC-2026-0543 (unmaintained, via `serde_with`), `proc-macro-error 1.0.4` RUSTSEC-2024-0370 (unmaintained, via gtk macros), `glib 0.18.5` RUSTSEC-2024-0429 / GHSA-wrw7-89jp-8q8g (unsound iterator, fixed in 0.20). `glib`/`proc-macro-error` are pulled only by the GTK/webkit2gtk stack (Linux targets); none is compiled into the Windows build | upstream tauri/wry pins | none for v1; re-check when tauri moves to gtk-rs 0.20 |

Status after the final fix slice (2026-11-23):

| # | Status | Evidence |
|---|---|---|
| O1 | closed (PASS) | B2 real-WebView2 probe: every IPC channel from the preview iframe blocked; residual "wry injects the IPC scripts into subframes on Windows; blocked by CSP + ACL" accepted (`docs/slices/B2-webview2.md`) |
| O2 | closed (PASS) | `tests/runner/test_b7_isolation_probe.py`, real CLI 2.1.330: plain `claude -p` loads the fixture hook, plugin, skill and agent (control); the runner flags load none and no hook fires. No new flag needed (`disableAllHooks` + `--setting-sources ""` already cover them) |
| O3 | closed (PASS) | B1 budget-capped real run ($0.12, 8 turns) after the auto-memory fix (`docs/slices/B1-real-run.md`) |
| O4 | open | image rebuilds need C: >= 10 GB free |
| O5 | open | CI first run, installed-mode e2e (or v1 = repo mode decision), fresh live local gate |
| O6 | fixed | `apps/backend/headers.py` (outermost middleware): `nosniff`, `Cache-Control: no-store`, `Referrer-Policy: no-referrer`, `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'` on every API response incl. boundary rejects, 404 and 422; `tests/security/test_audit_lows.py` |
| O7 | fixed | custom `RequestValidationError` handler: `{"error":"validation_failed","message":"request validation failed","details":{"fields":[...]}}`, field names only, no input; same test file |
| O8 | fixed | `services.request_approval` refuses (403 `policy_denied`) an agent identity (one run) that already created 50 approval records; owner not capped; `tests/unit/test_o8_approval_cap.py` |
| O9 | accepted | the three Rust advisories are informational (two "unmaintained", one unsound iterator in `glib` 0.18); `glib` and `proc-macro-error` are compiled only for the Linux GTK/webkit2gtk stack, not into the Windows build; `chrono` is pulled by tauri via `serde_with` and no advisory affects the API we use. No fixed version is reachable without a tauri/gtk-rs upgrade. Re-check at each tauri bump |

Accepted residuals already recorded with the owner (unsigned installer and exe, host-build trust of ~430 vendored
crates, R-EXT WSL root, Gradle TOFU, manifest trust, export scan pattern-based, grant/pipe key in `mcp.json`, debug-only
APK, emulator in WSL) are listed in `docs/SECURITY.md` §2 and are not repeated as open findings.

## The 19 checks

| # | Check | Status | Evidence |
|---|---|---|---|
| 1 | No hardcoded secrets in source/config/bundles | PASS | C3-C6 clean with repo and tests configs; the 3 default-rule hits (C2) are synthetic canaries, each allowlisted by exact path + rule in `.gitleaks-tests.toml`. Provider keys are read from env only (`core/studio_core/ai/providers.py:144,194,227`), never stored |
| 2 | Secrets in env / OS store, validated at startup, refuse to boot | PASS (adapted) | Owner token from stdin, `--token-stdin` (`apps/backend/main.py:548-551`, exits below 32 chars); `create_app` and `BoundaryMiddleware` raise below 32 chars (`main.py:135-136`, `security.py:35-36`); token never in argv/env/file/log in production (`lib.rs:6,301`). Dev-only token file prints a warning (`main.py:553-554`). Low note: `AAS_API_PORT`/`AAS_PREVIEW_PORT` are `int()`-parsed (`config.py:73-74`) without a range check (fails closed on garbage) |
| 3 | `.env*` ignored and not tracked | PASS | `.gitignore` has `.env*`, `data/`, `*.keystore`, `*.jks`, `*.log`; `git ls-files` has no env/key/keystore file; no `.env.example` needed (no env secrets) |
| 4 | No secrets in git history | PASS | C2, C5, C6, C7, C8: only test canaries under `tests/`; no key/env/db file ever committed. CI also scans history (`ai-app-studio.yml:57-75`) |
| 5 | Every private route behind auth, default deny | PASS | `BoundaryMiddleware` runs outermost before routing (`security.py:68-107`, `main.py:414-420`): exact Host allow-list, Origin allow-list, constant-time token compare (`security.py:66`); only `/api/v1/health` public (`security.py:24`); `docs_url=None`, `openapi.json` needs the token (C14). WebSockets use the same boundary + fixed subprotocol, token never echoed (`main.py:329-333`). Bind fixed to 127.0.0.1 (`main.py:51`). Tests: `tests/security/test_backend.py` (C13) |
| 6 | Ownership checks on resource access by id (anti-IDOR) | PASS (adapted) | Single owner; the second principal is the agent, bound to one project by a per-run grant. Test `tests/security/test_s6_review_mcp.py:89` `test_h1_grant_refuses_other_project_on_every_project_tool`. Ids are regex-validated on routes (`main.py:211,230,254,311,377`); captures served only if content hash matches (`main.py:259-261`) |
| 7 | Passwords hashed with Argon2id / bcrypt | N/A | no passwords: one 256-bit random bearer token (`config.py:62`, `secrets.token_urlsafe(32)`) |
| 8 | Session cookies HttpOnly/Secure/SameSite, CSRF | N/A | no cookies: bearer header / WS subprotocol only. CSRF is covered by the Origin allow-list (`security.py:77-79`) and the non-ambient token; CORS `allow_credentials=False` (`main.py:415`) |
| 9 | Server-side strict schema, unknown fields rejected | PASS | every request model extends `Strict` with `extra="forbid"` (`main.py:58-107`); `StrictBool`/`StrictInt`, length caps and id patterns; query `path` capped at 200 (`main.py:203`) |
| 10 | No XSS sinks | PASS | C15: no `dangerouslySetInnerHTML`/`innerHTML`/`insertAdjacentHTML`/`eval`/`new Function` in `apps/desktop/src`, `kit/`, `images/desktop-wrapper/src`; React escapes; only `react`/`react-dom`/`@tauri-apps/api` at runtime (`apps/desktop/package.json:16-20`); agent pages run in a sandboxed cross-origin iframe (`DeviceFrames.tsx:62`) |
| 11 | CSP without `unsafe-inline` scripts, `frame-ancestors 'none'` | PASS | Studio UI: `script-src 'self'`, `style-src 'self'`, `frame-ancestors 'none'`, `object-src 'none'`, `base-uri 'none'` (`tauri.conf.json:26`), `withGlobalTauri: false` (`:13`), `dangerousDisableAssetCspModification: false` (`:27`). Preview: `script-src 'self'`, `frame-ancestors` = Tauri origins only (it must be framed by the studio; dev origins added only with `-Dev` and regex-checked, `preview_server.py:26-31,113-118`). `style-src 'unsafe-inline'` exists only in the generated-app CSP (`kit/index.html:6`, `preview_server.py:27`) - styles, not scripts |
| 12 | Parameterised queries / safe ORM only | PASS | SQLAlchemy Core/ORM `select`/`delete` everywhere (`core/studio_core/activity.py:336,396,461`, `audit.py:54,73`); the only raw strings are constant PRAGMAs (`db.py:232-234`); C15 found no SQL built from input |
| 13 | Rate limiting on submit endpoints | PASS (adapted) | No login/register/checkout. Loopback-only + token; one live run at a time, second start 409 (`docs/SECURITY.md` §1, B6); cross-process build lock; builds refused below 5 GiB, device tools below 8 GiB; capture and screenshot quotas; `--max-turns` / `--max-budget-usd` and a run time cap (`claude.py:92,145`); pipe handshakes capped at 4 (`video_relay.py:65`); preview server max 32 connections + 10 s idle timeout (`preview_server.py:22-24`). Gap: O8 |
| 14 | CORS explicit allow-list, no `*` with credentials | PASS | `CORSMiddleware(allow_origins=settings.allowed_origins, allow_credentials=False)` (`main.py:415-417`); production origins only `tauri://localhost`, `http(s)://tauri.localhost` (`config.py:23-27`, B11); dev origins only via `AAS_DEV_ORIGINS`, which the shell strips (`lib.rs:286,306`; tests `tests/e2e/test_run_studio_script.py:120-128`). C14: foreign Origin 403 |
| 15 | Security headers (HSTS, XFO, nosniff, Referrer, Permissions) | PASS (adapted) | Preview server sends CSP, Permissions-Policy, COOP, CORP, XFO, nosniff, `Referrer-Policy: no-referrer`, no-store on every response incl. errors (`preview_server.py:33-43,154-164`). Studio UI is the Tauri custom protocol with CSP `frame-ancestors 'none'`. HSTS N/A: plain HTTP on loopback by design, no TLS endpoint. Gap: O6 (API 200 responses) |
| 16 | Debug off, no sensitive logging | PASS | uvicorn `log_level="warning"`, `server_header=False` (`main.py` serve, line 519); `docs_url=None`, `redoc_url=None` (`main.py:175`); preview `log_message` is a no-op (`preview_server.py:151-152`); activity/audit values sanitised + redacted, token registered for redaction (`main.py:137`, `activity.py:167-256`, `audit.py:29-41`); Tauri built with `features = []` (no devtools), release `strip = true` (`Cargo.toml:17,33-38`) |
| 17 | Generic error responses | PASS | boundary errors are fixed codes (`security.py:44-52`); `StudioError` mapped to `to_dict()` (`main.py:181-183`); unhandled exceptions fall to Starlette's plain 500 (debug off); preview never reflects the request line (`preview_server.py:166-168`). Gap: O7 |
| 18 | Dependency audit clean of high/critical, lockfiles committed | PASS | lockfiles tracked: `uv.lock`, `kit/pnpm-lock.yaml`, `apps/desktop/pnpm-lock.yaml`, `apps/desktop/src-tauri/Cargo.lock`, `images/desktop-wrapper/Cargo.lock`. C9-C11: 0 vulnerabilities. C12: 0 high/critical; 3 informational advisories (O9). osv-scanner not available (substitutes listed above). Exact pins in Cargo.toml (`=2.12.7` etc.) and package.json |
| 19 | Nothing sensitive served / shipped | PASS | `sourcemap: false` in `kit/vite.config.ts:10` and `apps/desktop/vite.config.ts:39`; release binaries stripped; preview serves GET/HEAD only, no listing, no dotfiles, no `..`/ADS/links (`preview_server.py:6,186-202`), per-project Host routing (`:45`, test `test_p3d_project_comes_from_host_not_path`); export runs a secret scan of raw + UTF-16 bytes and needs owner approval of the sha256 (`core/aas_build/export.py`, `tests/wiring/test_s9b_export.py:103-107`); runtime bundle staged from `git ls-files` (S9b L3) |

Desktop-specific items (skill section "non-web applications"): no `shell=True` / user input in process args (C15;
runner argv fixed, prompt on stdin, `claude.py:83-92`); safe deserialisation (JSON only, no pickle/yaml.load); file
paths validated against traversal (preview + export link/junction checks); signed builds and signed auto-update: NOT
met - installer and generated exe are unsigned and there is no auto-update (accepted residual, owner-PC-only v1).

## Verdict

Update after the final fix slice: O1, O2, O3 closed (PASS), O6-O8 fixed, O9 accepted; production ready still NO until
O4 and O5 are done (see `docs/FINAL_GATE_ROUND2.md`). Original verdict:

Production ready: NO - all 19 checks are PASS or N/A, but three high items specific to this desktop app are still open:
O1 (the real-WebView2 iframe IPC probe B2 has not run), O2 (owner hooks/plugins under the runner profile not probed,
B7) and O3 (no real agent run, B1). The image rebuilds (O4) and the release-SHA evidence (O5: CI on GitHub,
installed-mode e2e, live local gate, final gate round 2) must also be done before release.
