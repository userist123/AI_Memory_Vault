# CI Reproducibility & Verification Evidence

* **Status**: FAIL
* **Commit SHA**: 32889fdadbcdd069af360e246b68c4f4a05e4813
* **Python**: 3.14.2 (Windows-11-10.0.26200-SP0)
* **Pytest Version**: 9.0.2
* **Manifest Hash**: ecfd4cc44e2f7b5ee2f8340651fbc25a0457aeff5d99b4117855d396c4afe654
* **Timestamp**: 2026-09-05T10:30:43Z

## Collection Summary
* **Tests Collected**: 941
* **Collection Errors**: 0
* **Discovered Suites**: cognitive_core/tests, memory_controller/tests

## Test Execution
* **Passed**: 932
* **Failed**: 7
* **Errors**: 0
* **Skipped**: 2
* **Duration**: 19.01s

## Diagnostic Status
* **SHA Check**: PASS
* **Dependency Check**: PASS
* **Codex Runtime Block**: BLOCKED — CODEX

### Failed Tests
- memory_controller/tests/test_audit.py::test_audit_promote_success_and_fail
- memory_controller/tests/test_authorization.py::test_human_promote_allowed
- memory_controller/tests/test_authorization.py::test_admin_promote_allowed
- memory_controller/tests/test_cache.py::test_mutation_invalidation_review_promote
- memory_controller/tests/test_milestone3_empirical_challenge.py::test_concurrent_attest_and_update_race_sqlite
- memory_controller/tests/test_query_classifier.py::test_verified_is_still_detected_as_whole_word
- memory_controller/tests/test_query_raw_boundary.py::test_query_raw_boundary_holds_for_sqlite_storage
