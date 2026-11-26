# B2 - preview iframe isolation in the real WebView2 (2026-11-23)

Gate item: FINAL_GATE_ROUND1.md B2 ("preview iframe -> owner token in real WebView2; get_session only checks the window
label"). Result: **PASS - no call from the preview iframe reached `get_session`, the IPC host or the API.** No product
code change was needed for B2.

## Setup
- Real Tauri debug exe (`cargo build --locked`, tauri 2.12.7 / wry 0.57.3, WebView2 157.0.4341.60) started by
  `scripts/run_studio.ps1 -Dev -Ui tauri` with the fake CLI fixture (`tests/fixtures/fake_claude.py`) as the runner.
- The probe page was written through the normal agent path: fake CLI -> real MCP server -> `plan_submit` (plan approved
  by clicking "Approve plan" in the studio window via Windows UI Automation) -> `code_write_file src/App.tsx` ->
  `app_build web` (offline Docker build) -> the studio loads it in the phone frame
  (`http://p-<id>.localhost:17880`, sandbox `allow-scripts allow-same-origin`, preview CSP `connect-src 'self'`).
- Results were read three ways: the screenshot `50_ARTIFACTS/ai_app_studio/screenshots/B2-webview2.png` (unmodified debug exe, PrintWindow capture of the studio
  window only), the iframe's accessibility text (UI Automation), and CDP on a second, test-only copy of the exe whose
  only difference is `--remote-debugging-port` in `additionalBrowserArgs` (`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` is
  ignored because the app sets the option; the config change was reverted and the product exe rebuilt).

## Probes from inside the preview iframe

| # | Probe | Inside the iframe | Verdict |
|---|---|---|---|
| P1 | `typeof window.__TAURI_INTERNALS__` | `object` (see residual R1) | info |
| P1c | `__TAURI_INTERNALS__.invoke('get_session')` | no answer, timeout 3 s | PASS |
| P2/P2b | `window.ipc` / `window.ipc.postMessage(get_session)` | object present; call returns, nothing comes back | PASS |
| P3/P3b/P3c | `chrome.webview.postMessage(get_session)` + listen 2.5 s | object present; no message received | PASS |
| P4 | `fetch('http://ipc.localhost/get_session')` | `TypeError: Failed to fetch` | PASS |
| P5 | `fetch` API `/api/v1/health` and `/api/v1/projects` | `TypeError: Failed to fetch` (both) | PASS |
| P6/P6b | read `parent.document.title` / `top.__TAURI_INTERNALS__` | `SecurityError` (cross-origin) | PASS |
| P7 | `top.location.href = ...` | `SecurityError` (sandbox: no top navigation); top URL unchanged | PASS |
| P7b | `window.open('about:blank')` | `null` (sandbox: no popups) | PASS |

## Positive control (top-level studio document, CDP on the test copy)
`__TAURI_INTERNALS__` object; `invoke('get_session')` SUCCEEDED (`apiBase=http://127.0.0.1:17875`, token length 64 -
token not recorded); `window.ipc` and `chrome.webview` objects; `fetch` API `/health` -> 200. The same APIs that exist
in the iframe work only in the top-level document.

## Tauri ACL layer in isolation (CSP taken out of the equation)
`Page.setBypassCSP` does not apply to the header CSP of the out-of-process iframe, so the IPC host was tested from the
top document with CDP `Fetch` rewriting the `Origin` header of a `POST http://ipc.localhost/get_session` that carries
the real invoke key:
- Origin `http://127.0.0.1:1421` (control) -> `Tauri-Response: ok`, body `{apiBase, token:<64-hex>}`;
- Origin `http://p-<id>.localhost:17880` (the iframe) -> `Tauri-Response: error`, `get_session not allowed on window
  "main", webview "main", URL: http://p-<id>.localhost:17880/ ... allowed on: [windows: "main", URL: local]`;
- Origin `http://evil.example` -> the same ACL refusal.

## Why it holds (three independent layers)
1. Preview CSP `connect-src 'self'` blocks `fetch` to `ipc.localhost` and to the API port (P4, P5).
2. wry registers `CoreWebView2.WebMessageReceived` only; postMessage from a subframe is raised on `CoreWebView2Frame`,
   which nothing subscribes to, so `window.ipc` / `chrome.webview.postMessage` from the iframe go nowhere (P2, P3, P1c).
3. Tauri resolves the capability per request origin: `capabilities/default.json` has no `remote` URLs, so a
   non-local origin is refused before the command runs (forged-Origin test above).

## Residuals
- R1: on Windows wry injects every initialization script into subframes ("scripts are always added to subframes"),
  so the iframe holds `__TAURI_INTERNALS__`, `window.ipc` and the invoke key inside the IPC closure. Harmless while the
  three layers above hold; `get_session`'s own `window.label() == "main"` check is not a frame check (the iframe shares
  the label). Do not add `remote` URLs to a capability and do not relax the preview CSP `connect-src`.
- R2: CDP was used only on a separate test copy of the exe; the shipped build has no remote debugging.
