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
