# todo-claude-context-economy
STATUS: DONE                UPDATED: 2026-10-10
TASK: stop agents exploring whole repos (owner: 300-400k tokens/session) — in every project, not just this one
BRANCH / PR: ccr-233cc182-iik794 / none yet
DONE:
- .claude/skills/cost-router/lib/repo_map.py: ~1k-token map from git ls-files (dirs, sizes, types, README line, tests, ⚠ heavy), cached by HEAD
- hook_repo_map.py: SessionStart (startup|clear|compact), shadowed by the user install; project settings register it
- install.py: user-scope SessionStart hook + ~/.claude/CLAUDE.md marked rules block + Read deny rules; --no-context; uninstall exact
- 20_TESTS/test_cost_router_repo_map.py (11) + cost-router + CLAUDE.md contract tests: 90 passed
- docs: SKILL.md §3.7, protocol §3B, CLAUDE.md pointer
NEXT:
1. owner: re-run `python3 .claude/skills/cost-router/install.py` on the PC (new hook + rules land in ~/.claude)
2. cloud: the bootstrap line in the environment Setup script already pulls the new files
BLOCKERS: live injection into a fresh Claude Code session not observed (hook output format matches the prompt hook's)
- SKILL.md §0: main session = entrepreneur, subagents = employees (owner rule); guarded by test_skill_md_contract
KEY FILES: .claude/skills/cost-router/{lib/repo_map.py,hook_repo_map.py,install.py}
REVIEW: vault-reviewer (Opus) 7 findings -> fixed: cp1252 crash (ASCII JSON), uninstall removed user's own deny rules (sidecar record),
  deny too broad (caches only now), CLAUDE.md block byte-exact + damaged markers left alone, cache keyed by HEAD+listing + atomic write,
  rules wording without hook; also found+fixed: repo with no commits crashed. Not done: stat() bound for huge repos (0.06-0.35s on 17.5k files).
