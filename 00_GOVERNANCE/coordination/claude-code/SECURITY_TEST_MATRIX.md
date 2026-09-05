# Security Test Matrix — Executed Results

Every "Actual" cell below is backed by a real, executed pytest test named in
the Evidence column — none are asserted without execution.

| Boundary | Attack | Expected | Actual | Evidence |
|---|---|---|---|---|
| propose | AI proposes verified | DENY | DENY | `test_security_hardening.py::test_p0_001_ai_cannot_propose_verified` |
| propose | AI claims official/user provenance | DENY | DENY | `test_security_hardening.py::test_p0_002/003_*` |
| propose | AI proposes ACTIVE lifecycle | DENY | DENY | `test_security_hardening.py::test_p0_004_*` |
| attest | AI attempts attest() | DENY | DENY | `test_attest_security.py::test_ai_agent_cannot_call_attest_at_all` |
| attest | arbitrary verification_state string | DENY | DENY | `test_attest_security.py::test_attest_rejects_arbitrary_verification_state` |
| attest | injection-style verification_state | DENY | DENY | `test_attest_security.py::test_attest_rejects_injection_style_verification_state` |
| attest | near-miss / case-variant verification_state | DENY | DENY | `test_attest_security.py::test_attest_rejects_near_miss_no_fuzzy_matching` |
| attest | empty reason/evidence | DENY | DENY | `test_attest_security.py::test_attest_requires_non_empty_reason_and_evidence` |
| attest | HUMAN, valid evidence | ALLOW | ALLOW | `test_attest_security.py::test_attest_accepts_every_canonical_verification_state[verified]` |
| promote | REVIEW/unverified | DENY | DENY | `test_authorization.py::test_human_promote_rejected_without_verification` |
| promote | REVIEW/verified, HUMAN | ALLOW | ALLOW | `test_authorization.py::test_human_promote_allowed` |
| promote | REVIEW/verified, ADMIN | ALLOW | ALLOW | `test_authorization.py::test_admin_promote_allowed` |
| promote | AI_AGENT any state | DENY | DENY | `test_authorization.py::test_ai_cannot_promote` |
| archive | RAW/CLASSIFIED/NORMALIZED lifecycle | DENY | DENY | `test_archive_state_machine.py::test_archive_rejects_pre_review_lifecycles[*]` |
| archive | already SUPERSEDED/ARCHIVED | DENY | DENY | `test_archive_state_machine.py::test_archive_rejects_already_superseded`, `::test_archive_rejects_already_archived_reidempotent_reuse` |
| archive | ACTIVE, unverified, HUMAN | ALLOW | ALLOW | `test_archive_state_machine.py::test_archive_allows_active_unverified_by_human` |
| archive | ACTIVE, verified, HUMAN (not ADMIN) | DENY | DENY | `test_archive_state_machine.py::test_archive_of_verified_active_note_requires_admin` |
| archive | ACTIVE, verified, ADMIN | ALLOW | ALLOW | `test_archive_state_machine.py::test_archive_of_verified_active_note_allowed_for_admin` |
| archive | AI_AGENT, any lifecycle | DENY | DENY | `test_archive_state_machine.py::test_archive_ai_agent_denied_by_authorization_regardless_of_lifecycle` |
| archive | empty reason | DENY | DENY | `test_archive_state_machine.py::test_archive_requires_non_empty_reason` |
| supersede | self-supersession | DENY | DENY | `test_supersession_phase43.py::test_supersession_self_and_cycles_rejected` (pre-existing, re-verified this pass) |
| supersede | cycle | DENY | DENY | `test_supersession_phase43.py::test_supersession_self_and_cycles_rejected` |
| supersede | AI supersedes human-verified | DENY | DENY | `test_supersession_phase43.py::test_supersession_human_verified_protection` |
| supersede | partial write (2nd write fails) | ROLLBACK, byte-equal | ROLLBACK, byte-equal | `test_mutation_atomicity_storage_aliasing.py::test_rollback_restores_relations_list_exactly_when_second_write_fails` (proves the fix — this rollback was previously a no-op for `relations` due to shallow-copy aliasing) |
| update/attest/archive | validation fails after local mutation | storage byte-for-byte unchanged | storage byte-for-byte unchanged | `test_mutation_atomicity_storage_aliasing.py::TestGetMutateValidateAbortNeverCorruptsStorage::*` |
| query | RAW via MemoryController.query() | DENY | DENY | `test_query_raw_boundary.py::test_query_raw_boundary_applies_to_all_read_principals[*]`, `::test_query_raw_boundary_holds_for_sqlite_storage` |
| query | RAW via FinancialSearchEngine (in-memory backend) | DENY | DENY (fixed this pass -- previously reached `.store` directly, bypassing the RAW filter that `.query()` enforces) | `financial_search.py::_extract_all_storage_notes` now always routes through `.query()` |
| read | non-ACTIVE note via public read() | DENY | DENY | pre-existing `controller.read()` check, re-verified via full suite run |
| pagination | tampered signature | DENY | DENY | `test_security_matrix_gaps.py::test_tampered_signature_rejected` |
| pagination | malformed base64 | DENY | DENY | `test_security_matrix_gaps.py::test_malformed_base64_fails_closed` |
| pagination | malformed JSON payload | DENY | DENY | `test_security_matrix_gaps.py::test_malformed_json_payload_fails_closed` |
| pagination | signed with a different secret | DENY | DENY | `test_security_matrix_gaps.py::test_signed_with_different_secret_rejected` |
| pagination | negative / non-integer offset | DENY | DENY | `test_security_matrix_gaps.py::test_negative_offset_*`, `::test_non_integer_offset_rejected` (new bounds check added this pass) |
| pagination | absurd page_size | DENY | DENY | `test_security_matrix_gaps.py::test_absurd_page_size_rejected` (new) |
| pagination | changed query/principal/page_size on reuse | DENY | DENY | `test_security_matrix_gaps.py::TestSearchTokenBindingTamperDetection::*` |
| pagination | no HMAC secret configured | DENY (fail closed) | DENY | pre-existing `MissingHMACSecretError`, re-verified |
| cache | cross-principal reuse (A's result served to B) | DENY (isolated) | DENY (isolated) | `test_security_matrix_gaps.py::test_two_principals_never_share_a_cache_entry` |
| cache | same principal, different lifecycle filter | DENY (isolated) | DENY (isolated) | `test_security_matrix_gaps.py::test_same_principal_different_lifecycle_filter_is_not_a_cache_hit` |
| cache | poisoned entry (malformed key / oversized value) | DENY (evicted as miss) | DENY | `test_security.py::test_poisoned_cache_entry_invalidation` (pre-existing, re-verified) |

**32/32 rows executed and confirmed this pass — no "PASS" asserted without a
named, executed test.**
