# Vault Security Audit Skill (Local Copy)

## Key Security Assertions
1. **Adversarial Test Suite**: Run `python -m pytest memory_controller/tests/test_security_hardening.py cognitive_core/tests/test_tool_router_security.py` to verify invariants P0-001 through P0-015.
2. **Audit Log Integrity**: Verify that `AuditLogger.verify_integrity()` returns `True` and detects any tampering in `audit_log.jsonl`.
3. **Database Concurrency & Constraints**: Run `python -m pytest memory_controller/tests/test_sqlite_storage.py` to verify SQL CHECK constraint enforcement and multi-threaded WAL concurrency.
4. **Continual Learning Regression**: Run `python -m pytest cognitive_core/tests/test_continual_learning.py` to ensure registered anchor memories remain intact.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
