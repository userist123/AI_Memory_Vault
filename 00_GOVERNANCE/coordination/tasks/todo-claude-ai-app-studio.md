# todo-claude-ai-app-studio

Task: AI App Studio — local AI app builder (phone/tablet/desktop); owner sees live on the desktop what the AI does.
Owner rules: build and test everything on the PC (local commits only, NO push) until complete; final gate =
fable-consultant verifies the whole app, its questions go via agent-bridge to Codex (ChatGPT) + Gemini, fix and
repeat until clean, then security-lockdown skill, then push + PR #413 + vault CI + merge. Parallel slices allowed
(owner, 2026-11-23) with per-slice worktrees and file ownership.

Branches (all local, base 6254ee9c6 unless noted):
- claude/ai-app-studio @ 39da19a45 (D:/w/ai-app-studio): S1 core+seam (merged here), S2 app kind/AAS identity/
  backend/activity/MCP — round-2 review VERIFIED, merge-ready. Follow-ups for S6: F1 audit.redact hash exemption,
  F2 sanitise audit keys before secret-key check, F3 DESIGN 5.4 wording (4400 is a 403 handshake under uvicorn).
  Residuals to record in DESIGN 5.3: 64-hex values under hash-named keys kept verbatim; stream.dropped count is
  in-memory per process.
- claude/aas-s3 (D:/w/aas-s3, sparse): S3 build pipeline + preview @ 1ada33c3c; review round 1 PARTIAL; fixes
  written but UNCOMMITTED and UNTESTED (Docker Desktop down after C: filled) — next: new worker finishes tests
  listed in its report (preview %ff/dup Host/idle timeout/conn cap; e2e 300 MB writer, network fail text, P8b text,
  unapproved lockfile, /tmp noexec; proxy isPublic table + DNS-rebinding stub), commit, then review round 2.
- claude/aas-s4 (D:/w/aas-s4, sparse): S4 desktop UI — finishing worker running (WS contract aas.v1, red runs).
- claude/aas-s5 (D:/w/aas-s5, sparse): S5 WSL device host — running; its emulator crash-looped and WSL crash dumps
  (17 x ~2.7 GB in %TEMP%/wsl-crashes) filled C:. Dumps deleted. Worker told to find the cause, max 2 retries,
  abort launch if C: < 5 GB.

Next after the four slices: local merge of s3/s4/s5 into claude/ai-app-studio (main session), S6 built-in runner
(Claude Code stream-json -> activity_events, runner flags per DESIGN 6.1) + wiring backend<->build<->preview<->
device host<->UI, end-to-end demo on the desktop, then the final gate above.
Key files (product dir 03_IMPLEMENTATION/products/ai_app_studio): docs/CHECKPOINT.md, docs/DESIGN.md,
docs/THREAT_MODEL.md, docs/slices/S3.md|S4.md|S5.md, VENDORED.json.
Disk: C: fills easily (Docker VHDX + WSL crash dumps); keep caches on D:.

Update 2026-11-23 ~03:00:
- S4 claude/aas-s4 @ 1b787c0df: round-2 VERIFIED, merge-ready. Integration follow-ups (S6): backend routes
  GET /api/v1/approvals, POST /approvals/<id>/decide, POST /plans/<id>/decide, kind "plan" records; field names
  (step.started title, tool.call/result callId+target, plan.proposed title, build.log line, cost.update vs usage);
  backend DCS regex must stop at newline/end and OSC also at \r; flag U+3164/U+115F/U+180E/U+034F (front+back);
  frontend clean() ESC[ sequence over-swallow; one non-sparse `tauri build` + manual launch (get_session under the
  narrowed capability); S4.md says 6 vitest files, actual 5.
- S5 claude/aas-s5 @ cfcdf2c3b: fixes done (80 tests), live probes DEFERRED until C: >= 8 GB; round-2 review running.
- S3 claude/aas-s3: finishing worker running (Docker back up).

Update 2026-11-23 ~04:00 (S6 done, local commits on claude/ai-app-studio, nothing pushed):
- S6 delivered (docs/slices/S6.md): built-in runner + P5e/N3 probe (real CLI, positive control), run grants, MCP plan_submit/
  app_build/device tools, build->preview wiring, device setting gates, N1, backend approvals/plans/runs routes, activity
  field names + migration 0004, sanitiser/audit F1/F2, DESIGN 5.3 residuals, UI fixes (fillers, CSI, prj_ ids, preview
  reload), Tauri attach mode + debug build launched (get_session works), scripts/run_studio.ps1, end-to-end with the fake CLI
  and a screenshot of the real desktop window (docs/slices/S6-e2e.png). uv run pytest -q: 399 passed, 20 skipped.
- S4 and S2 follow-ups (F1/F2/F3) are closed; DESIGN 5.3 residuals recorded.
- Open: UI prompt box; P5f plan gate on code_write_file; Android build / preview_capture unavailable; S5 live probes deferred
  (C: < 8 GiB); real paid agent run not done; S5 live-gate results may still arrive on claude/aas-s5.
- NEXT = final gate: fable-consultant verifies the complete app; its open questions go via agent-bridge to Codex (ChatGPT) and
  Gemini; fix and repeat until clean; security-lockdown skill; then push + PR #413 + vault CI + merge.

Update 2026-11-23 ~05:00 (S6 review round 1 fixed, local commits only):
- Done: H1 (grant pins all tools to its project), H2 (fail-closed init), token-safe stale flush, P5f enforced (default ON,
  owner toggle), UI prompt box/Start/Stop, Tauri `python -I` launcher + env scrub, device re-check/stop at run end,
  bounded line reader, AAS_DEV_ORIGINS only with run_studio.ps1 -Dev. pytest 489 passed / 20 skipped; vitest 91; Playwright 12.
- Open: review round 2 (delta only); then the final gate.

Update 2026-11-23 ~05:30 (S7 done, local commits only):
- Done: offline Android image built + PB7a/PB7b probes; app_build android -> signed APK -> install + launch on the studio
  emulator; preview_capture (Playwright, PNGs by sha256 over REST); S6 low items (R-A purge, prompt, plan-gate toggle, plan
  content hash bound to the approval). pytest 513 passed / 20 skipped; vitest 92; Playwright 13. Demo: docs/slices/S7-demo.png.
- Open: capture thumbnails in the UI, emulator mirror stream, Nested-Virt modal, CI digest of the android image, paid run.
- NEXT = final gate (fable-consultant, then Codex/Gemini via agent-bridge, fix and repeat, security-lockdown, push + PR #413).

Update 2026-11-23 (S7 review findings M1-M3, L1-L4 fixed, local commits only on claude/ai-app-studio):
- M1 install sha256 vs build.finished + private copy; M2 committed strict Gradle verification metadata (residual TOFU of the
  initial generation); M3 capture quota/disk gate; L1 licence note (hash unverified offline); L2 purge after exclusive bind;
  L3 capture kill+join; L4 keystore pending-marker commit. Details: ai_app_studio/docs/slices/S7.md.
- NEXT = final gate (fable-consultant, Codex/Gemini via agent-bridge, security-lockdown, push + PR #413).

Update 2026-11-23 (S8 done, local commits only on claude/ai-app-studio):
- Done: live emulator mirror (keyed per-run pipe -> backend hub -> owner binary WS -> WebCodecs), capture thumbnails (blob:,
  sha256-checked), Nested-Virt modal via -no-nested-warnings, licence hash verified online. pytest 547 passed / 20 skipped;
  vitest 103; Playwright 15. Details: ai_app_studio/docs/slices/S8.md.
- Open: live check (C: < 8 GB during S8), external MCP without mirror, paid run.
- NEXT = final gate (fable-consultant, Codex/Gemini via agent-bridge, security-lockdown, push + PR #413).
