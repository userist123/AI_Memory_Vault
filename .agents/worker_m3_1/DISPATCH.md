## 2026-08-14T23:16:31+03:00

You are the Implementation Worker for Milestone 3: Security Invariants & Attestation Gates.
Your working directory is: c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\worker_m3_1

MANDATORY FIRST STEP:
Read c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\ORIGINAL_REQUEST.md and c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\PROJECT.md.

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Scope & Tasks:
1. Verify and enforce all P0-P15 security invariants in `memory_controller/controller.py` and `cognitive_core/`:
   - Invariant P0-001 / P0-005: AI Agent (`Principal.AI_AGENT`) cannot propose or update `verification="verified"`.
   - Invariant P0-002 / P0-003: AI Agent cannot claim privileged `source_type` (`user`, `official`, `experience`, `import`). Permitted: `execution`, `ai`, `inference`, `unknown`.
   - Invariant P0-004: AI Agent can only propose into `{RAW, CLASSIFIED, NORMALIZED, REVIEW}` lifecycles.
   - Invariant P0-006: Provenance `source_type` is immutable after creation.
   - Invariant P0-007 / P0-008: Attestation gate requires `Operation.ATTEST` and is strictly restricted to `Principal.HUMAN` and `Principal.ADMIN`.
   - Invariant P0-009 / P0-015: Tool router capability bounds and principal checks.
2. Verify that any invalid proposal/update is rejected cleanly without partial database writes.
3. Run security tests: `memory_controller/tests/test_security_hardening.py`, `cognitive_core/tests/test_tool_router_security.py`, and the full test suite.
4. Record your findings in `.agents/worker_m3_1/changes.md` and write handoff in `.agents/worker_m3_1/handoff.md`.
5. Send message to parent.
