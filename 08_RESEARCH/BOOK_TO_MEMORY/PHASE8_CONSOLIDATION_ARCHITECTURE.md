# Architecture Specification — Phase 8: Book-to-Memory Corpus Catalog & Reversible Consolidation

**Document Version**: 1.0.0  
**Phase**: Phase 8 — Corpus Catalog & Reversible Consolidation  
**Authority**: `00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md` (Sections 2, 8, 11)  
**Module**: `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_catalog.py`  
**Test Suite**: `20_TESTS/test_book_to_memory_catalog.py` (14/14 PASS)  

---

## 1. Executive Summary & Purpose

`BookToMemoryCatalog` establishes the centralized registry for managing, validating, and auditing canonical monograph maps (`type: book_map`) and their associated atomic concept notes under `POLICY-LEARNING-QUALITY-02`.

It provides:
1. **Canonical Book Map Management**: Rigorous schema validation and persistent tracking of source identity, authors, edition, chapter coverage, and processing status.
2. **Bidirectional Concept Linkage**: Associating atomic notes in the `VERIFIED` state with their parent book map and dynamically updating chapter coverage.
3. **Coverage & Gap Analysis**: Quantitative metrics on chapter coverage ratios and identification of unexplored chapters.
4. **Zero-Trust Untrusted Isolation**: Strict interception of prompt injection directives (`tool_call`, `exec`, `shell_command`) in both book maps and linked notes.
5. **No Auto-Promotion Enforcement**: Invariant preservation where all cataloged book maps and linked concepts remain strictly in `VERIFIED` state (`AWAITING_OWNER_APPROVAL`).
6. **Deterministic State Digests**: Tamper-evident SHA-256 state hashing across all registered maps and linked note counts.

---

## 2. Core Architectural Flow

```text
[06_INBOX/Carti Monograph]
            │
            ▼
[register_book_map()] ─────────────► Validates against BookToMemoryType.BOOK_MAP schema
            │
            ├──────────────────────► Intercepts prohibited keys (validate_untrusted_security)
            │
            ▼
[BookToMemoryCatalog Registry]
            │
            ├──────────────────────► link_atomic_note(): Verifies provenance parity
            │
            ├──────────────────────► calculate_coverage(): Computes covered vs gap ratio
            │
            ├──────────────────────► export_canonical_markdown(): Emits YAML frontmatter + Markdown
            │
            ▼
[compute_catalog_digest()] ────────► Generates tamper-evident SHA-256 catalog signature
```

---

## 3. Strict Invariants

| Invariant ID | Policy Rule | Catalog Enforcement Mechanism |
|---|---|---|
| **INV-CAT-01** | **One Book, One Map** | `register_book_map()` enforces uniqueness of `source_identity`; duplicate registration attempts raise `DuplicateBookMapError`. |
| **INV-CAT-02** | **Provenance Parity** | `link_atomic_note()` verifies that candidate note's `source_title` matches parent book map title. Unmatched notes raise `UnlinkedNoteError`. |
| **INV-CAT-03** | **No Auto-Promotion** | All book maps default to `VERIFIED`. Promotion to `ACTIVE` requires explicit cryptographic `OwnerApprovalToken`. |
| **INV-CAT-04** | **Gap Auditing** | `calculate_coverage()` reports exact count and names of uncovered chapters to ensure transparent, evidence-based extraction. |
| **INV-CAT-05** | **Tamper-Evident Digest** | `compute_catalog_digest()` produces a deterministic SHA-256 fingerprint of all catalog metadata and note linkage counts. |

---

## 4. Unit Test Results

The catalog was validated across 14 tests in [test_book_to_memory_catalog.py](file:///C:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/20_TESTS/test_book_to_memory_catalog.py):
- Registration, retrieval, duplicate rejection, and schema validation: PASS
- Atomic note linkage and idempotence: PASS
- Chapter coverage calculation & gap analysis: PASS
- Untrusted input injection prevention in book maps and linked notes: PASS
- Canonical markdown generation with frontmatter: PASS
- Deterministic digest computation and drift detection: PASS
- Multi-monograph catalog registration (7 core books): PASS
