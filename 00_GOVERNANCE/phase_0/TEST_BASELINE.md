---
id: fce00714-05aa-44ac-84e3-432c6ca70bd1
type: procedure
lifecycle: REVIEW
category: governance.phase0
tags: ['phase-0', 'reconciliation', 'measured']
created: "2026-09-20"
updated: "2026-09-20"
provenance:
  source_type: 'execution'
  source_ref: 'phase 0 reconciliation measured on origin/main 10224498c, 2026-09-20'
confidence: high
verification: unverified
relations:
  - type: part_of
    target_id: slot-06-procedures
---

# Test baseline — Phase 0

`python -m pytest 20_TESTS -q` on `origin/main` at `10224498c`, clean worktree:

```
3 failed, 2441 passed, 13 skipped, 9 xfailed in 226.94s
```

No collection errors.

## main is red

All three failures are in `20_TESTS/test_vault_runtime_secret.py`:

- `test_cli_without_a_secret_is_a_message_not_a_traceback`
- `test_secret_is_not_in_any_output_log_or_the_repo`
- `test_init_secret_output_names_the_path_but_not_the_value`

Cause, reproduced by hand:

```
python -m cognitive_core.recall_cli --query "ontology gate"
ModuleNotFoundError: No module named 'cognitive_core.working_memory'
  at 03_IMPLEMENTATION/packages/memory/controller.py:1444
```

`468fe9992` added `from cognitive_core.working_memory import WorkingMemory` at
the bottom of `controller.py`. The repository-root `cognitive_core/` package is
a shim holding two files, so the import only resolves when
`03_IMPLEMENTATION/packages` is already on `PYTHONPATH`. With `PYTHONPATH` set
the CLI still returns notes; without it, it dies on import.

That is the exact capability PR #173 delivered and tested: memory recall that
works with no setup. It has been red since `3b2991344` was pushed.

## Why CI did not catch it

The full suite runs as a gate on pull requests to `main` and, since #174, on
push to `main`. `3b2991344` was merged locally and pushed as a merge commit
without a pull request, so no PR gate ran. The push-to-main run was added in the
same PR that landed after it.

The narrow workflows — `Memory V6 Tests` runs only
`20_TESTS/memory_controller` — stayed green, which is why `main` looks healthy
from the outside.

## Fix, for Wave A

One line: export `working_memory`, `global_workspace`, `reasoning` and
`executive` from the `cognitive_core` shim, or import them by their real path
in `controller.py`. Owner decision not required; it is a defect, not a choice.
