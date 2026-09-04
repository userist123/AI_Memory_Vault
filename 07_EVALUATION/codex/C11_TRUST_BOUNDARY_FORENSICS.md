# R001 C11 — REVIEW memory trust boundary

Evidence level: `CODE_VERIFIED` + `TEST_VERIFIED`.

Baseline lineage: CODEX branch commit `387a7fb0d6bd79826b69aae8ce8f83b992476942`. At the time of this lane, `origin/main` had advanced separately to `9e883c45fcacd18b8f7e5b52e04edaa847952008`; that main commit was not merged into this branch.

The adversarial test stores four REVIEW notes containing fake SYSTEM/developer authority, tool-bypass language, and admin claims. `RecallEngine` returns a detached candidate marked `_cognitive_unverified=True`; it does not change the stored lifecycle. A privileged `delete_canonical` request is rejected by `ToolRouter` as high risk before execution.

Observed test output:

```text
python -m pytest -q tests/test_codex_review_memory_injection.py cognitive_core/tests/test_tool_router_security.py
.......                                                                  [100%]
7 passed, 1 warning in 0.56s
```

The warning is the existing invalid `\\s` escape in `cognitive_core/activation.py`.

This proves the tested REVIEW-to-retrieval and high-risk authorization boundary, not that every possible model-mediated instruction path is safe. Model authority escalation and real tool execution require separate end-to-end runtime tests.
