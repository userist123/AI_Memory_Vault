---
trigger: always_on
description: Cognitive Core and Memory Controller operating rules and trust boundaries.
---

# Vault Cognitive Operating Rules

## Security Model Nomenclature
- **`P0`**: Phase 4.3 Priority-0 Security Hardening designation.
- **`P0-001..P0-015`**: 15 adversarial test contracts defined in `99_SYSTEM/Phase43_P0_Implementation_Contract.md`.
- **`I-001..I-012`**: Canonical Phase 4.3 memory security invariants.
- **`I-RETRIEVAL`**: Unified secure retrieval invariant introduced subsequently.
- **`P1 / P2 / P3`**: Forensic priority tiers (correctness, architecture, maintainability).
- **`P16 / P17 / P18`**: Desktop / hardware forensics invariants.
- **`P0-P18`**: Umbrella shorthand only; not 19 sequential memory invariants.

## 1. Memory Trust Boundary Invariants (I-001..I-012, I-RETRIEVAL)
- **I-001 (AI Self-Verification Gated)**: `Principal.AI_AGENT` cannot set `verification = "verified"`.
- **I-004 (Attestation Authorization)**: Only `Principal.HUMAN` and `Principal.ADMIN` can invoke `controller.attest()` via `Operation.ATTEST`.
- **I-002 (Privileged Provenance Gated)**: `Principal.AI_AGENT` cannot claim `source_type` of `user`, `official`, `experience`, or `import`. Permitted: `execution`, `ai`, `inference`, `unknown`.
- **I-003 (Creation Lifecycle Restricted)**: `Principal.AI_AGENT` can only propose into `{RAW, CLASSIFIED, NORMALIZED, REVIEW}`. Direct promotion to `ACTIVE` requires human review/attestation.
- **I-005 (Provenance Immutability)**: `provenance.source_type` cannot be modified after initial creation.
- **I-RETRIEVAL (Unified Secure Retrieval Invariant)**: Toate interogările de memorie (API REST sau CLI fallback) sunt supuse autorizării `MemoryController.search()`. Dacă serverul local e offline, se folosește exclusiv `python -m cognitive_core.recall_cli --query ...` (versiune securizată, trece prin aceleași verificări I-001..I-012). Orice ocolire prin scanare directă nesecurizată de fișiere este strict interzisă.

## 2. Multi-Agent Least Privilege Scoping
- **Router Agent**: Analyzes queries and decomposes goals (Read/Search only).
- **Retrieval Agent**: Associative and semantic recall + supersession lineage traversal (Read/Search only).
- **Verifier Agent**: Audits provenance and canonical frontmatter schema (Read only).
- **Consolidator Agent**: Synthesizes ephemeral review lessons into canonical knowledge (Search, Read, Propose, Archive).
- **Critic Agent**: Formal 6-stage Reflexion and SelfRefine critique (Read, Propose).

## 3. Storage & Integrity Invariants
- Storage engine is thread-safe and supports SQLite WAL mode with `PRAGMA busy_timeout=5000` and `BEGIN IMMEDIATE` atomic transactions.
- Checkpoints (`wm.json`, `plan.json`) must be written atomically via temporary files and `os.replace`.
- All audit log events are chained using tamper-evident SHA-256 hashes.

## 4. Hardware Telemetry & Forensics Invariants (P16-P18)
- **P16 - Hardware Telemetry Immutability**: Datele fizice generate de sistemul de operare (VID, PID, Hardware Serial Number, Capacitate fizica, System Host ID, Timestamp generat, Hash SHA-256) sunt strict Read-Only; interfețele UI blochează orice modificare manuală a acestora.
- **P17 - Friendly Name Isolation**: Utilizatorul poate modifica exclusiv denumirea prietenoasă / eticheta logică a volumului și metadatele administrative (gestionar, plafon clasificare, politică acces), fără a altera identificatorii fizici unici.
- **P18 - Forensics & Chain of Custody Integrity**: Orice transfer leagă automat amprenta hardware imutabilă a mediului detectat fizic în jurnalul de audit tamper-evident.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
