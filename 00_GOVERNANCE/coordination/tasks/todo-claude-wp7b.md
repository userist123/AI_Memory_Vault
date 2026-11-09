# todo-claude-wp7b

- Task: WP-07 PR B, one module identity for memory_controller and cognitive_core.
- Branch: modernize/f2-wp07b-alias (from modernize/f2-wp07a-facades). PR not opened (owner/main session).
- Done: `_alias.py` meta-path finder + install from the shim; `20_TESTS/test_memory_core_contract.py` (P1-P7, subprocess probes);
  baseline_dynamic_imports packages/memory_controller=2; F0 inventory regenerated; VAULT_STATE.md and import-order comment updated.
- Verified: focused set 465 passed; full suite 5256 passed / 55 skipped / 9 xfailed; architecture_boundaries exit 0.
- Next: Opus vault-reviewer (WP-08) re-runs probes; main session opens PR and checks CI once.
- Blockers: none. Note: 01_ARCHITECTURE/memory/Lessons/Verify_Consumption_Not_Existence.md still says "19-line" (canonical memory, not edited).
- Key files: 03_IMPLEMENTATION/packages/memory_controller/{__init__,_alias}.py, 20_TESTS/test_memory_core_contract.py.
