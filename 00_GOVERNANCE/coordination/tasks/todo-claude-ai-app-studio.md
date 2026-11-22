# todo-claude-ai-app-studio

Task: AI App Studio — local AI app builder (phone/tablet/desktop) where the owner sees live on the desktop what the AI does.
Branch/PR: claude/ai-app-studio (worktree D:\w\ai-app-studio), draft PR in this vault.
Product dir: 03_IMPLEMENTATION/products/ai_app_studio. CI: .github/workflows/ai-app-studio.yml.
Done: research (Codex + Gemini via agent-bridge), design + threat model (2 review rounds, implementation allowed),
emulator spikes 1-2 (WSLg visible window, netns isolation, restrict=on), CI skeleton,
S1 vendored core from AI Website Studio 25b3eab + ProjectKind seam + VENDORED.json check (N6), CI pytest via uv.
S1 review fixes: restored security tests, generated VENDORED.json (scripts/gen_vendored.py), link-safe drift check,
seam hardening (non-empty Publication.required, audited kind resolution, desktop capture Unavailable), uv sync --locked.
S2 done (local, not pushed): AAS identity (AAS_* env, %LOCALAPPDATA%\AIAppStudio, ports 17875/17880), app kind + migration 0003 + appfiles allow-list,
activity recorder (sanitiser, redaction, seq/prev), FastAPI backend + WS replay, MCP server ai-app-studio (off by default, PX1), backlog L1/L2/L4.
Owner rule: everything is built and tested on the PC until the app is complete; final gate = fable-consultant verifies the whole app,
open questions go via agent-bridge to Codex and Gemini, fix and repeat until clean, only then push + PR + vault CI + merge.
Next: S3 = build pipeline (Docker, offline web build) + preview server + CSP header.
Key files: docs/CHECKPOINT.md, docs/DESIGN.md, docs/THREAT_MODEL.md, docs/RESEARCH_2026-11-22.md, spike/EMULATOR_SPIKE.md.
