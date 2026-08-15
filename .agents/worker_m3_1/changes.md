# Milestone 3 Security Invariants & Attestation Gates — Changes & Verification Report

## Overview
This milestone verified and enforced the complete trust boundary invariants (P0-001 through P0-015) in `memory_controller` and `cognitive_core`.

## Verification of Security Invariants (P0-001 to P0-015)

### 1. Invariant P0-001 / P0-005 (AI Agent Self-Verification Blocked)
- **Mechanism**:
  - In `MemoryController.propose()`, caller-provided `verification == 'verified'` is explicitly rejected with `ValueError("Verification status 'verified' cannot be set via propose. Use attest() instead.")`.
  - In `MemoryController.update()`, `updates.get('verification') == 'verified'` is rejected for all principals (`"Verification status 'verified' cannot be escalated via update. Use attest() instead."`).
- **Tests**:
  - `test_p0_001_ai_cannot_propose_verified` in `memory_controller/tests/test_security_hardening.py`
  - `test_p0_005_ai_cannot_update_verification_to_verified` in `memory_controller/tests/test_security_hardening.py`
  - `test_ai_cannot_self_verify` in `memory_controller/tests/test_security_hardening.py`

### 2. Invariant P0-002 / P0-003 (Privileged Provenance Gating)
- **Mechanism**:
  - `_ALLOWED_PROVENANCE_SOURCE_TYPES` dictionary restricts `Principal.AI_AGENT` to `{"execution", "ai", "inference", "unknown"}`.
  - Privileged source types (`user`, `official`, `experience`, `import`) cannot be claimed by `Principal.AI_AGENT`.
- **Tests**:
  - `test_p0_002_ai_cannot_claim_official_provenance` (official blocked)
  - `test_p0_003_ai_cannot_claim_user_provenance` (user blocked)
  - `test_p0_additional_ai_prohibited_provenance_types` (experience & import blocked)
  - `test_p0_ai_permitted_provenance_types` (execution, ai, inference, unknown permitted)

### 3. Invariant P0-004 (Creation Lifecycle Gating)
- **Mechanism**:
  - `_PERMITTED_CREATION_LIFECYCLES = {RAW, CLASSIFIED, NORMALIZED, REVIEW}`.
  - Proposing directly into `ACTIVE`, `VERIFIED`, `SUPERSEDED`, or `ARCHIVED` by `Principal.AI_AGENT` is rejected.
- **Tests**:
  - `test_p0_004_ai_cannot_inject_active_lifecycle_at_creation`
  - `test_p0_ai_prohibited_creation_lifecycles`

### 4. Invariant P0-006 (Provenance Immutability)
- **Mechanism**:
  - `MemoryController.update()` checks if `provenance.source_type` differs from existing note's `source_type`. If different, raises `ValueError("Field provenance.source_type is immutable post-creation...")`.
- **Tests**:
  - `test_p0_006_provenance_source_type_immutable`

### 5. Invariant P0-007 / P0-008 (Attestation Gate & Direct Controller Protection)
- **Mechanism**:
  - Lifecycle updates are immutable on update (`immutable = {'id', 'lifecycle'}`).
  - `attest()` method gates promotion to `verified` via `Operation.ATTEST` requiring `{Principal.HUMAN, Principal.ADMIN}`.
  - Attestation requires non-empty `verification_reason` and `evidence_reference`.
  - Audited with full metadata: `attested_by`, `reason`, `evidence_reference`, `previous_verification_state`, `new_verification_state`.
- **Tests**:
  - `test_p0_007_lifecycle_immutable_on_update`
  - `test_p0_008_direct_controller_attack_blocked`
  - `test_p0_010_human_attestation`
  - `test_p0_011_admin_attestation_and_ai_agent_denied`

### 6. Invariant P0-009 / P0-015 (ToolRouter & Cognitive Security Bounds)
- **Mechanism**:
  - `ToolRouter.execute()` enforces capability bounds and risk policy.
  - `ToolRouter._check_knowledge_reconciliation_boundary()` prevents automated deletion/mutation of human-verified memories without approval (`ApprovalRequiredError`).
  - Rejection cleanly propagates through `ToolRouter`.
  - `LearningEngine` promotes memories to `partially_verified` without escalating to `verified`.
  - Non-persistence verified: `storage.set()` is never reached on invalid proposals, leaving storage completely clean (0 records written).
  - Supersession explicitly does not transfer verification trust (`test_p0_015`).
- **Tests**:
  - `cognitive_core/tests/test_tool_router_security.py` (`test_p0_009_tool_router_blocks_ai_verified_propose`, `test_p0_009_tool_router_blocks_ai_user_provenance_propose`, `test_p0_012_learning_engine_partially_verified_promotion`)
  - `memory_controller/tests/test_security_hardening.py` (`test_p0_013_atomic_non_persistence`, `test_p0_014_restart_preserves_attestation`, `test_p0_015_supersession_does_not_transfer_trust`)
  - `test_p0_sqlite_storage_security_hardening`

## Test Execution Results
- `pytest` executed across entire test suite (37 modules, 269 items).
- Result: **269 passed in 13.66s, 0 failures, 0 errors**.
