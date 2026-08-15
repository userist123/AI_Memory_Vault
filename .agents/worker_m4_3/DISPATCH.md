# DISPATCH: Worker 3 for Milestone 4 Remediation (worker_m4_3)

## Mission
Remediate the two defects and test signature fix identified by `reviewer_m4_4`:

1. **Fix `VerifierAgent.process_task` (`cognitive_core/agents/verifier_agent.py:25-31`)**:
   - Safely validate provenance dictionary:
     ```python
     prov = node.get("provenance")
     if not isinstance(prov, dict):
         violations.append(f"Node {node_id} has invalid provenance: {prov!r}")
         source_type = "unknown"
     else:
         source_type = prov.get("source_type", "unknown")
     ```
2. **Fix `RecallEngine.recall` (`cognitive_core/recall.py:154-184`)**:
   - Store unpenalized match score `pre_lifecycle_score = final_score` before applying `lifecycle_factor` (0.3 for SUPERSEDED).
   - Use `pre_lifecycle_score` when computing active successor inherited score:
     ```python
     inherited_score = min(1.0, pre_lifecycle_score * 1.1)
     ```
3. **Fix test fixture signature (`cognitive_core/tests/test_milestone4_adversarial_challenger_m4_4.py:186`)**:
   - Change `def flaky_search(principal, **kwargs):` to `def flaky_search(principal, *args, **kwargs):`.

## Mandatory Integrity Warning
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A forensic auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

## Mandatory Reference Documents
- `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\ORIGINAL_REQUEST.md`
- `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\PROJECT.md`
- `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\reviewer_m4_4\handoff.md`

## Working Directory
`.agents/worker_m4_3`

## Verification Requirements
1. Implement fixes in `cognitive_core/agents/verifier_agent.py`, `cognitive_core/recall.py`, and `cognitive_core/tests/test_milestone4_adversarial_challenger_m4_4.py`.
2. Run `python -m pytest cognitive_core/tests/test_milestone4_adversarial_challenger_m4_4.py cognitive_core/tests/test_recall.py cognitive_core/tests/test_specialized_agents.py -v`.
3. Run full test suite: `python -m pytest`. Ensure 100% pass across all test modules (0 failures).
4. Write your handoff report to `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\worker_m4_3\handoff.md`.
