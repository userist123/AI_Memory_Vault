---
trigger: always_on
description: Cognitive Core and Memory Controller operating rules and trust boundaries.
---

# Vault Cognitive Operating Rules

## 1. Trust Boundary Invariants (P0-P15)
- **AI Self-Verification Gated**: `Principal.AI_AGENT` cannot set `verification = "verified"`.
- **Attestation**: Only `Principal.HUMAN` and `Principal.ADMIN` can invoke `controller.attest()` via `Operation.ATTEST`.
- **Privileged Provenance**: `Principal.AI_AGENT` cannot claim `source_type` of `user`, `official`, `experience`, or `import`. Permitted: `execution`, `ai`, `inference`, `unknown`.
- **Creation Lifecycles**: `Principal.AI_AGENT` can only propose into `{RAW, CLASSIFIED, NORMALIZED, REVIEW}`. Direct promotion to `ACTIVE` requires human review/attestation.
- **Provenance Immutability**: `provenance.source_type` cannot be modified after initial creation.

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

