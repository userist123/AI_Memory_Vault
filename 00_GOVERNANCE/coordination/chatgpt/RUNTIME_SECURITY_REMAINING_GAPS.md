# Runtime Security — Remaining Gaps

## Scope

This document records runtime trust-boundary gaps identified during the security/lifecycle closure work on `runtime-security-lifecycle-closure`.

## Confirmed gaps

### 1. Direct financial ingestion persistence is not yet wired to the canonicalizer

`memory_controller/financial_ingestion_security.py` defines `canonicalize_financial_ingest_frontmatter()` and correctly rejects privileged lifecycle/verification values while normalizing accepted input to `REVIEW` + `unverified`.

However, `FinancialSourceIngestionManager._persist_note()` currently reads lifecycle and verification directly from the source frontmatter when constructing the SQLite canonical record. The canonicalizer must be invoked before file and storage persistence so caller-supplied privileged state cannot survive into either representation.

### 2. `MemoryController.propose()` currently restricts creation lifecycle only for AI_AGENT

The controller contains `_PERMITTED_CREATION_LIFECYCLES = {RAW, CLASSIFIED, NORMALIZED, REVIEW}` and checks it for `Principal.AI_AGENT`. HUMAN and ADMIN proposals are not subjected to the same creation-state boundary by this check.

The intended invariant is that `propose()` creates an unverified proposal and must not establish privileged lifecycle state regardless of principal. Verification must continue through `attest()`, and promotion to ACTIVE must continue through `promote()`.

### 3. Lifecycle single-source-of-truth remains a separate integration step

`memory_controller/lifecycle_policy.py` provides a pure lifecycle transition policy, but not every mutating controller/persistence path is wired to it yet. Wiring must preserve existing operation authorization and fail-closed behavior.

## Non-goals for this document

- No retrieval ranking changes.
- No graph/synapse changes.
- No `PROJECT_BRAIN/PROJECT_STATE.md` changes.
- No direct merge into `main`.

## Evidence

P1.3 retrieval boundary is intentionally separate from these runtime gaps. The Antigravity P1.3 contract establishes `ACTIVE` + `verified` as the standard retrieval trust boundary and enforces caller narrowing only; it is not the mechanism that closes write-path lifecycle bypasses.
