# OPEN_BLOCKERS.md — Active Blocker Registry for Book-to-Memory
**Research Track**: `research/book-to-memory` (PR #206)  
**Target Scope**: `06_INBOX/Carti/Altele/` and Governance Ingestion Pipeline  
**Date**: 2026-10-03  
**Status**: ACTIVE TRACKING

---

## Blocker Summary Table

| Blocker ID | Severity | Category | Target / Component | Status | Required Action / Owner Decision |
|---|---|---|---|---|---|
| **B-POLICY-01** | **CRITICAL** | Governance | `POLICY-LEARNING-QUALITY-02` | `CLOSED (RESOLVED)` | Materialized by owner in `00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md` (commit `cb3cba48`). Validated: 0 conflicts. |
| **B-SOURCE-01** | **HIGH** | Corpus Integrity | `Carti/aposd2ndEdExtract.pdf` | `OPEN (BLOCKED)` | Provide full 2nd edition PDF of Ousterhout (~200 pages) with text layer. |
| **B-SOURCE-02** | **HIGH** | Format Pipeline | `Carti/dokumen.pub_making-software...azw3` | `OPEN (BLOCKED)` | Convert or provide clean standard PDF edition of *Making Software*. |
| **B-SOURCE-03** | **CRITICAL** | Scam Teaser | `Carti/ilide.info-ai-engineering...pdf` | `OPEN (BLOCKED)` | Acquire complete ~400-page O'Reilly edition of Chip Huyen's *AI Engineering*. |
| **B-SOURCE-04** | **HIGH** | Truncated Preview | `Carti/ilide.info-llm-engineer-s-handbook...pdf` | `OPEN (BLOCKED)` | Acquire complete ~400-page Packt edition of *LLM Engineer's Handbook*. |
| **B-SOURCE-05** | **MEDIUM** | Security Gate | `Carti/Designing Machine Learning Systems.pdf` | `OPEN (REVIEW)` | Authorize deterministic stripping of `\u200b` and `\u2060` in `convert_pdf_to_text.py`. |
| **B-SOURCE-06** | **LOW** | Corpus Relevance | `Lucrari/ilide.info-rag-architecture-explained...pdf` | `OPEN (BLOCKED)` | Approve permanent exclusion of commercial promotional blog post from canonical corpus. |

---

## Detailed Blocker Profiles

### B-POLICY-01: Materialization of Authoritative Policy Text (RESOLVED)
* **Description**: `POLICY-LEARNING-QUALITY-02` required materialization by owner.
* **Evidence**: Committed by owner (`cb3cba48`) at `00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md`. SHA-256: `52ac836ad5f796fe0fc2bed2d6b4111cdd37f50567ffadb32ed9206f853513ff`.
* **Affected Component**: Governance spine (`00_GOVERNANCE/rules/`), extraction quality gates.
* **Status**: **RESOLVED / CLOSED**. Compatibility matrix verified 0 conflicts.

---

### B-SOURCE-01: Truncated Scanned Extract (`aposd2ndEdExtract.pdf`)
* **Description**: A 20-page excerpt of John Ousterhout's *A Philosophy of Software Design* (2nd Ed), lacking a digital text layer (0.0 chars/page).
* **Evidence**: File size 13.9 MB, 20 pages, image-only raster scans requiring OCR. Full book is ~180-200 pages.
* **Affected Component**: `06_INBOX/Carti/Altele/Carti/aposd2ndEdExtract.pdf`.
* **Required Owner Decision**: Confirm whether full authorized edition is available or permanently remove file from corpus.
* **Remediation Path**: Replace with complete PDF containing a native digital text layer.

---

### B-SOURCE-02: Non-PDF Amazon Kindle Format (`dokumen.pub_...azw3`)
* **Description**: *Making Software* is provided as a proprietary `.azw3` Amazon Kindle archive, which cannot be processed by `convert_pdf_to_text.py` and contains binary controls that trip `hidden_characters`.
* **Evidence**: Extension `.azw3`, size 10.9 MB. 14 matches for control Unicode sequences in binary stream.
* **Affected Component**: `06_INBOX/Carti/Altele/Carti/dokumen.pub_making-software...azw3`.
* **Required Owner Decision**: Provide PDF format or approve adding an `.azw3`/`.epub` extraction script.
* **Remediation Path**: Ingest clean digital PDF of *Making Software: What Really Works, and Why We Believe It*.

---

### B-SOURCE-03: 3-Page Scam Teaser (`ilide.info-ai-engineering...pdf`)
* **Description**: An `ilide.info` file masquerading as Chip Huyen's *AI Engineering: Building Applications with Foundation Models*. It contains only the front cover, back cover blurb, and praise quotes (exactly 3 pages).
* **Evidence**: 3 pages, 1.7 MB. Pages 1-3 contain zero technical text, zero chapters, zero code examples. Full book is ~400 pages.
* **Affected Component**: `06_INBOX/Carti/Altele/Carti/ilide.info-ai-engineering...pdf`.
* **Required Owner Decision**: Acknowledge scam teaser and replace with complete monograph.
* **Remediation Path**: Exclude file; acquire genuine 400-page O'Reilly volume.

---

### B-SOURCE-04: 14-Page Preview Sample (`ilide.info-llm-engineer-s-handbook...pdf`)
* **Description**: A 14-page truncated sample of *LLM Engineer's Handbook* (Iusztin & Labonne) containing credential extraction commands (`cat ~/.aws/credentials`, `OPENAI_API_KEY`).
* **Evidence**: 14 pages, 563 KB. Security detector fired on `secret_access` (p.6, p.7, p.10). Full volume is ~400 pages.
* **Affected Component**: `06_INBOX/Carti/Altele/Carti/ilide.info-llm-engineer-s-handbook...pdf`.
* **Required Owner Decision**: Replace with full volume or exclude from pipeline.
* **Remediation Path**: Remove sample; acquire complete authorized edition.

---

### B-SOURCE-05: Benign Typographical Unicode in Full Monograph (`Designing Machine Learning Systems.pdf`)
* **Description**: Complete, high-quality 461-page textbook by Chip Huyen contains two typographical Unicode characters (`\u200b` Zero-Width Space at p.157, `\u2060` Word Joiner at p.385) that trip `BLOCKING_RULES` in `untrusted_content_guard.py`.
* **Evidence**: Inspection confirms both instances are harmless typesetting artifacts from automated InDesign/Pandoc line breaking.
* **Affected Component**: `30_SCRIPTS/ingestion/convert_pdf_to_text.py` (`_normalize()`).
* **Required Owner Decision**: Approve updating `_normalize()` in `convert_pdf_to_text.py` to strip `\u200b` and `\u2060` during text normalization rather than broad allowlisting.
* **Remediation Path**: Add `re.sub(r'[\u200b\u2060]', '', text)` to `_normalize()`.

---

### B-SOURCE-06: Commercial Promotional Blog Post (`ilide.info-rag-architecture-explained...pdf`)
* **Description**: 19-page PDF print of an online marketing post from Orq.ai by Reginald Martyr, containing AI assistant UI prompts (*"Summarize with AI"*).
* **Evidence**: 19 pages. Commercial blog content, not a peer-reviewed paper or academic monograph.
* **Affected Component**: `06_INBOX/Carti/Altele/Lucrari/ilide.info-rag-architecture-explained...pdf`.
* **Required Owner Decision**: Confirm permanent exclusion from canonical scientific ontology corpus.
* **Remediation Path**: Exclude file from concept extraction.


---

## External audit findings (PR #209) that remain open on this track

These come from the independent audit in PR #209 (`docs/security/AUDIT_REMEDIATION.md`). Most are
methodology gaps, not code defects, and cannot be closed by editing this branch: each needs real
data. B01, B02, B11 and B12 were code defects and are fixed. They are listed here in plain words so that no report on this track reads as empirical evidence.
They are **not** entries of the hash-chained `BLOCKER_REGISTER.md`; moving them there is an owner decision.

| Finding | Gap | State on this branch |
|---|---|---|
| B01 | Positive results could be built in (default scores) | **Fixed in code.** The ablation runner and the pipeline's usage-test stage no longer have default answers, rubrics or scores; missing observations give `INSUFFICIENT_DATA` and a closed gate. Reports produced from the old defaults (`PHASE7_PILOT_EXECUTION_REPORT.md`) are marked invalid. |
| B02 | The owner-approval token (HMAC) signed only note id, approver and timestamp: a note could be edited after approval and still be promoted | **Fixed** (commit `db6e42cd7`). Token `b2m-owner-approval-v2` signs the SHA-256 of the canonical note (frontmatter + body; lifecycle state fields excluded), the revision marker if the note has one, and an expiry (max 30 days). `verify()` recomputes the digest from the note being promoted and refuses on content or revision mismatch, replay to another note, expiry, missing fields or an old-format token; secret resolution stays fail-closed. `security/runtime_enforcer.py` was not reused: it binds tool-execution requests (tool, parameters, nonce), has no production consumer, and has no note digest. Tests: `20_TESTS/test_book_to_memory_lifecycle_gates.py::test_32_*` (valid accepted; approve-then-edit body / frontmatter / added field, replay, expired, old format, missing field, revision refused), `20_TESTS/test_book_to_memory_pipeline.py::test_pipeline_blocks_active_when_note_edited_after_owner_approval`. |
| B03 | No ablation against real models | **Open.** Every ablation in the tests uses fabricated numbers on purpose. |
| B04 | Prompt bias / leading questions in the harness | **Open.** Task prompts have not been reviewed against counter-factual baselines. |
| B05 | Single rater | **Open.** One rater (or one LLM) per judgement; no multi-rater consensus, no agreement statistic. |
| B06 | Metrics calibrated on a synthetic corpus | **Open.** No calibration on human-attested ground truth. |
| B07 | Possible leakage between evaluation sets | **Open.** Overlap between scenarios and benchmark questions has not been checked. |
| B08 | Baselines not comparable | **Open.** No identical temperature/seed controls across models. |
| B09 | Claims exceed evidence | **Partly fixed.** Status lines and the pilot/phase reports now say "unit tests pass", not "verified"; the individual phase reports still use the older wording and should be read with the caveat banners. |
| B10 | External validity | **Open.** No measurement under production load; the modules are not wired into any production path. |
| B11 | Cleanup rewrote note bodies | **Fixed.** `clean_source_frontmatters.py` is frontmatter-only; the 9 injected bodies were restored (see `VAULT_STATE.md`, section 5, for the islands this exposes). |
| B12 | A blocker's severity could be lowered (HARD_BLOCKER to WARNING) by editing the register or this table; `validate_severity_transition` was a library function nothing called | **Fixed** (commit `db6e42cd7`). Any severity decrease, and any deleted blocker, now needs a `SeverityDowngradeAttestation` (typed owner `Principal` checked by `require_owner_principal`, evidence reference, exact blocker and transition) recorded in `SEVERITY_ATTESTATIONS.md`. `validate_blocker_registry.py --base-ref <base>` compares `BLOCKER_REGISTER.md` and the severity table of this file with the base branch; the `Repository Hygiene` workflow runs it on every pull request. Tests: `20_TESTS/research/test_blocker_severity_downgrade.py` (downgrade without attestation fails; owner attestation passes; upgrade allowed; `ai_agent` refused; CLI end to end in a throw-away repository), `security/tests/test_audit_remediation.py::test_b12_severity_downgrade_blocked` unchanged. Limit: the ledger is a file in the pull request, so the attestation is as strong as the owner review of that file (CODEOWNERS `*`). |
