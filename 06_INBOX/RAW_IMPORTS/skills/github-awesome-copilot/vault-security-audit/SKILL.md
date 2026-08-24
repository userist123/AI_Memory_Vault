---
name: vault-security-audit
description: Security verification and forensic validation runbook for testing trust boundaries and invariants P0-P15.
---

# Vault Security Audit Skill

This skill outlines verification commands and adversarial test procedures to guarantee that memory trust boundaries are never compromised.

## Key Security Assertions
1. **Adversarial Test Suite**: Run `python -m pytest memory_controller/tests/test_security_hardening.py cognitive_core/tests/test_tool_router_security.py` to verify invariants P0-001 through P0-015.
2. **Audit Log Integrity**: Verify that `AuditLogger.verify_integrity()` returns `True` and detects any tampering in `audit_log.jsonl`.
3. **Database Concurrency & Constraints**: Run `python -m pytest memory_controller/tests/test_sqlite_storage.py` to verify SQL CHECK constraint enforcement and multi-threaded WAL concurrency.
4. **Continual Learning Regression**: Run `python -m pytest cognitive_core/tests/test_continual_learning.py` to ensure registered anchor memories remain intact.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[10 Imports and Sources Map]]
- [[Master_Skills_Catalog_251]]
- [[Knowledge Graph Home]]
