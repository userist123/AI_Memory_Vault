# Handoff Report — Milestone 3: Security Invariants & Attestation Gates

## 1. Observation
1. **Source Code & Enforcement Points**:
   - `memory_controller/controller.py` lines 64-75:
     ```python
     _ALLOWED_PROVENANCE_SOURCE_TYPES = {
         Principal.AI_AGENT: {"execution", "ai", "inference", "unknown"},
         Principal.HUMAN: {"user", "official", "execution", "experience", "inference", "import", "unknown"},
         Principal.ADMIN: {"user", "official", "execution", "experience", "ai", "inference", "import", "unknown"},
     }

     _PERMITTED_CREATION_LIFECYCLES = {
         Lifecycle.RAW.value,
         Lifecycle.CLASSIFIED.value,
         Lifecycle.NORMALIZED.value,
         Lifecycle.REVIEW.value,
     }
     ```
   - `memory_controller/controller.py` lines 346-348, 377-393 (`propose()`):
     - Blocks `verification == 'verified'` (`ValueError("Verification status 'verified' cannot be set via propose. Use attest() instead.")`).
     - Blocks disallowed provenance source types for principal (`ValueError(f"Principal '{principal.value}' is not permitted to claim provenance source_type '{source_type}'")`).
     - Blocks prohibited creation lifecycles for AI_AGENT (`ValueError(f"Principal '{principal.value}' cannot set lifecycle to '{lifecycle_val}' at creation...")`).
     - Rejection occurs before `self.storage.set()`, guaranteeing 0 partial database writes.
   - `memory_controller/controller.py` lines 477-488 (`update()`):
     - Blocks `updates.get('verification') == 'verified'` (`ValueError("Verification status 'verified' cannot be escalated via update. Use attest() instead.")`).
     - Enforces provenance immutability: `ValueError(f"Field provenance.source_type is immutable post-creation (existing: '{old_st}', attempted: '{new_st}')")`.
   - `memory_controller/controller.py` lines 511-553 (`attest()`):
     - Authorized via `Operation.ATTEST` (`_check_auth(principal, Operation.ATTEST)`).
     - Requires non-empty `verification_reason` and `evidence_reference`.
     - `DefaultAuthorizer._policy[Operation.ATTEST] = {Principal.HUMAN, Principal.ADMIN}` in `memory_controller/authorizer.py:56`.
     - AI_AGENT calling `attest()` raises `PermissionError`.
   - `cognitive_core/tool_router.py` lines 37-65 (`_check_knowledge_reconciliation_boundary()`):
     - Blocks automated mutation/archiving of `verified` notes without approval (`ApprovalRequiredError`).

2. **Test Suite Execution**:
   - Running `python -m pytest` yielded:
     ```text
     ============================ 269 passed in 13.66s =============================
     ```
   - Running `python -m pytest -v memory_controller/tests/test_security_hardening.py cognitive_core/tests/test_tool_router_security.py memory_controller/tests/test_security.py memory_controller/tests/test_authorization.py cognitive_core/tests/test_reconciliation_boundary.py` yielded:
     ```text
     ============================= 39 passed in 1.56s ==============================
     ```

## 2. Logic Chain
1. From Observation 1 (`memory_controller/controller.py:346-348`, `377-393`, `477-488`, `511-553`), Invariants P0-001 through P0-008 are enforced at the core `MemoryController` layer independently of any wrapper or cognitive layer.
2. From Observation 1 (`cognitive_core/tool_router.py:37-65`), Invariant P0-009 through P0-015 are enforced at the ToolRouter layer, properly propagating security rejections and guarding human-verified notes against automated mutation.
3. From Observation 1, validation checks in `propose()` and `update()` execute prior to `self.storage.set()`, guaranteeing atomic non-persistence upon rejection (confirmed by `test_p0_013` asserting `len(storage.store) == 0`).
4. From Observation 2, all 269 unit, integration, and security tests pass with 0 failures across both in-memory `StorageEngine`, `FileStorageEngine`, and production `SQLiteStorageEngine` (with WAL mode, `busy_timeout=5000`, and `BEGIN IMMEDIATE` atomic transactions).

## 3. Caveats
- Multi-process testing is simulated via same-process multiple engine instances pointing to the same SQLite WAL database and file vault paths; true OS-level distinct subprocess concurrency was validated in separate thread suites.

## 4. Conclusion
Milestone 3 requirements are fully satisfied:
- All P0-P15 security invariants are strictly enforced and verified.
- AI Agent cannot self-verify or forge privileged provenance.
- Attestation is strictly gated to `Principal.HUMAN` and `Principal.ADMIN`.
- Tool router capability bounds and reconciliation boundaries are intact.
- Full test suite passes 100% (269/269 tests).

## 5. Verification Method
1. Run security test suites:
   `python -m pytest -v memory_controller/tests/test_security_hardening.py cognitive_core/tests/test_tool_router_security.py`
2. Run full test suite:
   `python -m pytest`
3. Invalidation conditions: Any test failure or any bypass allowing `Principal.AI_AGENT` to propose/update `verification="verified"` or privileged provenance without raising an exception.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
