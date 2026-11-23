# Final gate - round 2 (2026-11-23, branch claude/ai-app-studio, local commits only)

Round 1: `docs/FINAL_GATE_ROUND1.md`. Audit: `docs/SECURITY_AUDIT.md`. Residuals: `docs/SECURITY.md` section 2.

Verdict: release blockers from round 1 B1, B2 and B7 are closed; NOT release-clean until the remaining items below
are done.

## Round-2 verdict table

| Item | Verdict | Evidence |
|---|---|---|
| B1 real agent run | PASS (with the auto-memory fix) | budget-capped real CLI 2.1.330 run from the UI ($0.12, 8 turns): init `mcp_servers == ["ai-app-studio"]`, catalogue-only tool_use, plan gate held, adversarial line refused, web build + capture ok. Found and fixed F1: CLI auto-memory loaded the owner's MEMORY.md (`autoMemoryEnabled: false` + `CLAUDE_CODE_DISABLE_AUTO_MEMORY=1`, real-CLI test). `docs/slices/B1-real-run.md` |
| B2 WebView2 iframe IPC | PASS, residual | probe page in the preview iframe of the real Tauri/WebView2 window: `invoke(get_session)`, `window.ipc`, `chrome.webview.postMessage`, ipc.localhost / API fetch, parent document, top navigation, `window.open` all blocked; positive control in the top document works. Residual: IPC scripts injected in iframe on Windows; blocked by CSP + ACL. `docs/slices/B2-webview2.md` |
| B7 runner isolation (hooks, plugins, skills, agents) | PASS | `tests/runner/test_b7_isolation_probe.py` (real CLI): plain `claude -p` loads the fixture hook, plugin, skill and agent (control); the runner flags load none, no hook fires. No new flag needed |
| F2 project title from prompt | FIXED | UI `projectTitle()` keeps `[A-Za-z0-9 _.-]`, fallback "App"; real server error shown (sanitised); vitest + backend test |
| F3 app revisions in git | FIXED | `gitrepo` commits the kind's file roots (`src`, `public`); commit + diff + restore test |
| N1 image vs line cap | FIXED | agent image <= 512 KiB after base64, runner line cap 2 MiB; max-size image line through the fake CLI |
| Audit lows O6-O8 | FIXED | API security headers, generic 422, 50 approvals per agent run |
| Audit O9 (3 Rust advisories) | ACCEPTED | informational; Linux GTK stack only or unmaintained-only; re-check at each tauri bump |

## Remaining before release

1. Image rebuilds (`aas-web-build:dev` with `tsc --noEmit`, `aas-android-build:dev` with Gradle 8.14.8): need
   C: >= 10 GB free.
2. A fresh live local gate run (`tests/local_gate/run.ps1`, receipt with sha256).
3. Installed-mode end-to-end (install, first launch, capture), or an owner decision that v1 ships in repo mode only.
4. First CI run of `.github/workflows/ai-app-studio.yml` on GitHub (needs a push; not done in this slice).
