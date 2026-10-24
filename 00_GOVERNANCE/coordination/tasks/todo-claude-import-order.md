# todo-claude-import-order

- **Task:** `20_TESTS/test_council_memory_budget.py` failed when run alone (circular import), on `main` too; it passed in the full suite only through import order.
- **Branch:** `claude/fix-executive-circular-import`
- **Cause:** `memory/controller.py` imported WorkingMemory, GlobalWorkspace/WorkspaceProposal, ReasoningEngine and Executive at the bottom of the file. That breaks the cycle only if the controller is imported first. Measured: 7 `cognitive_core.*` modules (controller, executive, global_workspace, orchestrator, reasoning, recall, working_memory) failed as a first import with "partially initialized module".
- **Done:** those classes are imported inside the builders that use them (`_get_working_memory`, `_get_global_workspace`, `_get_reasoning_engine`, `_get_executive`, and WorkspaceProposal next to its use). `FileStorageEngine`/`PlasticityEngine` late imports unchanged. New `20_TESTS/test_cognitive_core_import_order.py`: every `memory/*.py` imported first in a fresh interpreter (29 cases; 14 fail on the old controller). `test_readme_cognitive_core_claim.py` now requires each of the four wired imports (indent-tolerant) instead of any module-level one.
- **Not a bug:** `memory_controller.executive` etc. were never importable (that namespace's `__path__` has no graph/ or learning/); only `memory_controller.controller` is checked there.
- **Key files:** `03_IMPLEMENTATION/packages/memory/controller.py`, `20_TESTS/test_cognitive_core_import_order.py`, `20_TESTS/test_readme_cognitive_core_claim.py`.
