---
id: "4f8e9b21-7c3d-4a91-b6f2-9d0e1a3c5b78"
title: "Checkpoint: Cost Router Fable 5.2 Opus-Advisor Merge and Multi-Runtime Sync"
type: "project"
lifecycle: "REVIEW"
created: "2026-11-22T13:08:00Z"
updated: "2026-11-22T13:08:00Z"
verification: "unverified"
provenance:
  source_type: "execution"
  source_ref: "00_GOVERNANCE/coordination/tasks/todo-antigravity-cost-router-fable52.md"
  created_by: "antigravity"
tags:
  - "checkpoint"
  - "cost-router"
  - "security"
---

# Checkpoint: Cost Router Unified Fable 5.2 Update

**Branch:** `claude/cost-router-opus-advisor-fable52` (from `origin/main` `aa42387f2`)
**Worktree:** `D:\w\cost-router-fable-update` (on secondary disk; clean base)
**Owner Decisions Applied:**
- **A1:** Merge uncommitted `opus-advisor-mode` (Fable 5.2 / `fable-consultant`) over `origin/main` (PR `#313` phases 6-7, `#317` pinned self-update, `#320` release pin seq 1, `#365` Antigravity) on a clean branch; reinstall `~/.claude/skills/cost-router/` with backup (pin seq `2` stays deferred until merge to `main`).
- **A2:** Preserve `~/.claude/settings.json` and `~/.claude/CLAUDE.md` unchanged (`--no-hook --no-context`).
- **A3:** Align `.agents/skills/cost-router/`, Antigravity (`~/.gemini/config/skills/cost-router/`), Gemini CLI (`~/.gemini/skills/cost-router/`), and Codex CLI (`~/.codex/skills/cost-router/`) with updated `policy.json`, `lib/claude_model_router.py`, `lib/route.py`, and Windows `PermissionError` backoff retry in `lib/route_state.py`.

## Done
- [x] Merged Fable 5.2 consultant allowlist, strict 7-key read-only frontmatter validation, Opus-only session guard, fail-closed `exit 2` / `onFailure: block`, and `historical_models` pricing onto `origin/main` PR `#313`/`#317`/`#320`.
- [x] Synced `.agents/skills/cost-router/` (`policy.json`, `lib/claude_model_router.py`, `lib/route.py`) and ported Windows `os.replace` `PermissionError` retry into `.agents/skills/cost-router/lib/route_state.py`.
- [x] Independent security review completed (`7c02aaed-729e-4768-aee3-f57749de7397`): `VERDICT: APPROVED` with all 7 threat-model probes passed.
- [x] Fixed `20_TESTS/test_hook_antigravity.py` CWD-relative import path for out-of-tree pytest runs.

## Next
- After PR merge to `main`, regenerate `release_pin.json` (`seq: 2`) against the merged `main` commit SHA and refresh the pinned install.

## Key Files
- `.claude/skills/cost-router/hook_agent_model.py`
- `.claude/skills/cost-router/install.py`
- `.claude/agents/fable-consultant.md`
- `.claude/settings.json`
- `04_CONFIG/claude_model_routing.json`
- `03_IMPLEMENTATION/packages/routing/claude_model_router.py`
- `03_IMPLEMENTATION/packages/routing/claude_usage_report.py`
- `.agents/skills/cost-router/policy.json`
- `.agents/skills/cost-router/lib/route_state.py`
- `20_TESTS/test_fable_consultant_routing.py`
