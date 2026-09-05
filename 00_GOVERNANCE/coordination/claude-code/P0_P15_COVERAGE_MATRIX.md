# P0-001..P0-015 Coverage Matrix — Executed, Not Assumed

**Authoritative catalog source**: `07_EVALUATION/security_invariant_nomenclature_2026-09.md`
(P0-001..P0-015 are the 15 adversarial test contracts defined in
`99_SYSTEM/Phase43_P0_Implementation_Contract.md:Part 11`; do not confuse with
`I-001..I-012`, the invariants themselves).

Per the runtime-security brief: the filename
`test_adversarial_p0_p15_invariants.py` does **not** by itself mean complete
coverage — the 15 contracts are in fact spread across **three separate
files**. Every row below was confirmed by actually running the test (not
inferred from its name), on 2026-09-05, against the current
`memory_controller.controller.MemoryController` (post lifecycle/archive/
attest hardening in this pass).

```
pytest memory_controller/tests/test_adversarial_p0_p15_invariants.py \
       memory_controller/tests/test_security_hardening.py \
       cognitive_core/tests/test_tool_router_security.py -v
=> 32 passed in 0.45s
```

| Contract | Meaning | Test(s) | Result |
|---|---|---|---|
| P0-001 | AI cannot propose verified notes | `test_security_hardening.py::test_p0_001_ai_cannot_propose_verified`, `test_adversarial_p0_p15_invariants.py::test_attack_ai_propose_verified_strict_rejection_and_zero_writes` | PASS |
| P0-002 | AI cannot claim official provenance | `test_security_hardening.py::test_p0_002_ai_cannot_claim_official_provenance`, `test_adversarial_p0_p15_invariants.py::test_attack_ai_forge_privileged_provenance_types` | PASS |
| P0-003 | AI cannot claim user provenance | `test_security_hardening.py::test_p0_003_ai_cannot_claim_user_provenance` (also covered by the same forge test above) | PASS |
| P0-004 | AI cannot inject ACTIVE lifecycle at creation | `test_security_hardening.py::test_p0_004_ai_cannot_inject_active_lifecycle_at_creation`, `test_adversarial_p0_p15_invariants.py::test_attack_ai_propose_active_lifecycle_strict_rejection` | PASS |
| P0-005 | AI cannot escalate verification via update() | `test_security_hardening.py::test_p0_005_ai_cannot_update_verification_to_verified`, `test_adversarial_p0_p15_invariants.py::test_attack_ai_update_escalate_verification_strict_rejection` | PASS |
| P0-006 | provenance.source_type immutable post-creation | `test_security_hardening.py::test_p0_006_provenance_source_type_immutable`, `test_adversarial_p0_p15_invariants.py::test_attack_provenance_source_type_post_creation_immutability` | PASS |
| P0-007 | lifecycle immutable on update() | `test_security_hardening.py::test_p0_007_lifecycle_immutable_on_update`, `test_adversarial_p0_p15_invariants.py::test_attack_lifecycle_field_immutability_on_update` | PASS |
| P0-008 | direct controller attack blocked without ToolRouter | `test_security_hardening.py::test_p0_008_direct_controller_attack_blocked` | PASS |
| P0-009 | ToolRouter propagates security rejection | `cognitive_core/tests/test_tool_router_security.py::test_p0_009_tool_router_blocks_ai_verified_propose`, `::test_p0_009_tool_router_blocks_ai_user_provenance_propose`, `test_adversarial_p0_p15_invariants.py::test_attack_tool_router_reconciliation_boundary_blocks_unauthorized_mutations` | PASS |
| P0-010 | HUMAN can attest notes | `test_security_hardening.py::test_p0_010_human_attestation` | PASS |
| P0-011 | ADMIN can attest; AI_AGENT cannot | `test_security_hardening.py::test_p0_011_admin_attestation_and_ai_agent_denied`, `test_adversarial_p0_p15_invariants.py::test_attack_ai_attest_unauthorized_permission_error` | PASS |
| P0-012 | LearningEngine promotion restricted to partially_verified | `cognitive_core/tests/test_tool_router_security.py::test_p0_012_learning_engine_partially_verified_promotion` | PASS |
| P0-013 | Zero partial write on rejected proposal | `test_security_hardening.py::test_p0_013_atomic_non_persistence`, `test_adversarial_p0_p15_invariants.py::test_attack_file_storage_zero_disk_artifacts_on_rejected_proposals`, `::test_attack_multi_threaded_adversarial_barrage_zero_partial_writes` | PASS |
| P0-014 | Restart preserves attested trust state | `test_security_hardening.py::test_p0_014_restart_preserves_attestation` | PASS |
| P0-015 | Supersession isolates trust attributes | `test_security_hardening.py::test_p0_015_supersession_does_not_transfer_trust` | PASS |

**Result: 15/15 contracts have at least one real, executed, passing test.
0 MISSING.** (This was not the case for the *file name* alone — the
adversarial-invariants file's own docstrings only literally enumerate 9 of
the 15 numbers; P0-009's dedicated tests, P0-012, are only in
`cognitive_core/tests/test_tool_router_security.py`.)

This matrix does not certify that every contract's test is maximally
thorough — only that each has genuine, non-vacuous, currently-passing
coverage. New adversarial coverage added in this pass (mutation atomicity,
archive state machine, attest whitelist, pagination/cache gap-closure — see
`FINDINGS.md`) is additive to this matrix, not a replacement for it.
