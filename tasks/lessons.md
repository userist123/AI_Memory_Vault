# Lessons

## 2026-10-08 — sequential merges need the combined test roots, not a subset
- What happened: #209 added `security/tests/test_audit_remediation.py::test_u01` asserting the
  MCP server has exactly three tools; #214 added six read-only `vault_*` tools. Each PR was
  green alone. I merged #214 after #209 having re-run only `20_TESTS` subsets on the
  combination, so `security/tests` went red on main (the security-boundary workflow is
  path-filtered and did not run on the merge).
- Rule: before merging PR B on top of a main that just received PR A, run **every** test root
  CI uses (`20_TESTS` and `security/tests`, plus any root the two PRs add) on `main + B`,
  not only the tests near the files B touches.
- Rule: when two PRs touch the same module (here `memory_mcp_server.py`), grep the other PR's
  tests for assertions on that module's public surface before merging.
