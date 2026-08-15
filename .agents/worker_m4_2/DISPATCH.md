# DISPATCH: Worker 2 for Milestone 4 Remediation (worker_m4_2)

## Mission
Remediate the two defects identified by `challenger_m4_1` in `cognitive_core/reflection.py`:

1. **Fix `ReflectionPipeline.propose_synapse` (`cognitive_core/reflection.py:124-153`)**:
   - Align relations format with `_CANONICAL_SCHEMA`: Use `{"relation": relation_type, "target": target_node.get("type", "knowledge") if isinstance(target_node, dict) else "knowledge", "target_id": target_id}` (requires `relation` and `target` strings, optional `target_id` UUID string).
   - In `controller.update`, pass only the updated fields: `{"relations": relations}` instead of the entire `source_node` dictionary (which contains `verification="verified"` and causes `controller.update` to reject the update).
2. **Fix `SelfRefine.refine_memory` (`cognitive_core/reflection.py:39`)**:
   - Safely extract content: `content = (candidate.get("content") or "")` and verify `isinstance(content, str)` before calling `.strip()`, ensuring `{"content": None}` or non-string content is safely handled without raising `AttributeError`.

## Mandatory Integrity Warning
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A forensic auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

## Mandatory Reference Documents
- `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\ORIGINAL_REQUEST.md`
- `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\PROJECT.md`
- `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\rules\vault_cognitive_rules.md`
- `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\challenger_m4_1\handoff.md`

## Working Directory
`.agents/worker_m4_2`

## Verification Requirements
1. Implement the fixes in `cognitive_core/reflection.py`.
2. Run `python -m pytest cognitive_core/tests/test_milestone4_adversarial_challenger.py cognitive_core/tests/test_milestone4_adversarial_challenger_m4_2.py cognitive_core/tests/test_reflection.py cognitive_core/tests/test_dynamic_synapses.py -v`.
3. Run the full test suite: `python -m pytest`. Ensure 100% pass across all test modules.
4. Write your handoff report to `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\worker_m4_2\handoff.md`.
