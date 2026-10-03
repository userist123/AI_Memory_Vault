# BOOK_MATERIAL_DISPOSITION.md — Material Disposition & Integrity Classification
**Research Track**: `research/book-to-memory` (PR #206)  
**Target Scope**: `06_INBOX/Carti/Altele/` (18 items: 11 Carti, 7 Lucrari)  
**Date**: 2026-10-03  
**Auditor**: Antigravity (AI Senior Systems & Security Agent)  
**Status**: DISPOSITION FINALIZED (READ-ONLY)

---

## 1. Cardinal Governance Rule on Ingestion Status

> [!CRITICAL]
> **`READY_FOR_EXTRACTION` DOES NOT EQUAL `ACTIVE` OR `VERIFIED`**  
> Under Memory Invariants `I-001`, `I-003`, and `I-004`, **NO external material can produce active memory directly**.  
> Any concepts extracted from an authorized source enter staging solely as candidate proposals (`REVIEW` lifecycle status). Promotion to `ACTIVE` memory requires human owner review, attestation, and proof of non-fabrication.

---

## 2. Complete Material Disposition Table (18 Items)

| # | Material Path | Type | Pages | SHA-256 (First 16) | Disposition Status | Primary Rationales & Gate Findings |
|---|---|---|---|---|---|---|
| 1 | `Carti/Accelerate The Science of Lean Software...pdf` | PDF | 286 | `99aaa200b86adc15...` | **`READY_FOR_EXTRACTION`** | Complete book (IT Revolution). Text density 1,307 c/p. 0 security findings. |
| 2 | `Carti/am08-complete_22Feb09.pdf` | PDF | 408 | `2fb5936be4dda969...` | **`READY_FOR_EXTRACTION`** | Complete electronic edition (Princeton Univ Press / Caltech). Text density 2,235 c/p. 0 security findings. |
| 3 | `Carti/aposd2ndEdExtract.pdf` | PDF | 20 | `ccb9cc3de8c75957...` | **`BLOCKED`** | Truncated 20-page extract (full book is ~200p). Scanned image with 0.0 c/p (OCR required). |
| 4 | `Carti/Daniel Kahneman-Thinking, Fast and Slow.pdf` | PDF | 533 | `2ebd90d54b93c2ac...` | **`READY_FOR_EXTRACTION`** | Complete monograph (FSG, 2011). Text density 2,176 c/p. 0 security findings. |
| 5 | `Carti/Deep+Learning+Ian+Goodfellow.pdf` | PDF | 801 | `880b976d7951de25...` | **`READY_FOR_EXTRACTION`** | Complete textbook (MIT Press, 2016). Text density 2,221 c/p. 0 security findings. |
| 6 | `Carti/Designing Data Intensive Applications...pdf` | PDF | 613 | `3480da3ae9a35306...` | **`READY_FOR_EXTRACTION`** | Complete monograph (O'Reilly, 2017). Text density 2,334 c/p. `role_header` at p.463 is report-only false positive on technical prose. |
| 7 | `Carti/Designing Machine Learning Systems.pdf` | PDF | 461 | `e3b542b0350a4c24...` | **`SECURITY_REVIEW_REQUIRED`** | Complete book (O'Reilly, 2022). Hits blocking rule `hidden_characters` at p.157 (`\u200b`) and p.385 (`\u2060`). Requires normalizer sanitization. |
| 8 | `Carti/dokumen.pub_making-software...azw3` | AZW3 | 0 | `3c9a2372cdde4806...` | **`BLOCKED`** | Non-PDF Amazon Kindle format. Incompatible with `convert_pdf_to_text.py`. Scraped third-party container with binary control characters. |
| 9 | `Carti/ilide.info-ai-engineering-pr_...pdf` | PDF | 3 | `bccd6fb8ff924422...` | **`BLOCKED`** | Scam preview teaser: only 3 pages (cover + ISBN + praise blurb). 100% devoid of technical book content. Full book is ~400p. |
| 10 | `Carti/ilide.info-llm-engineer-s-handbook-pr_...pdf` | PDF | 14 | `ab732f0f337e5f7d...` | **`BLOCKED`** | Truncated 14-page sample (handbook is ~400p). Contains operational commands referencing `cat ~/.aws/credentials` and API keys. |
| 11 | `Carti/SuttonBartoIPRLBook2ndEd.pdf` | PDF | 352 | `db8fcf0fbb245d8c...` | **`READY_FOR_EXTRACTION`** | Complete textbook draft (MIT Press). Text density 2,049 c/p. 0 security findings. |
| 12 | `Lucrari/2018-dora-accelerate-state-of-devops...pdf` | PDF | 78 | `bd3690e42d3f1e5e...` | **`READY_FOR_EXTRACTION`** | Complete industry research report (DORA 2018). Text density 1,354 c/p. 0 security findings. |
| 13 | `Lucrari/2504.15965v2.pdf` (From Human to AI Memory) | PDF | 26 | `0b869b8a3fd1530e...` | **`READY_FOR_EXTRACTION`** | Complete scientific survey (Huawei Noah's Ark Lab). Text density 3,939 c/p. 0 security findings. |
| 14 | `Lucrari/2512.13564v2.pdf` (Memory in Age of AI Agents) | PDF | 107 | `10de3c050903bfa1...` | **`READY_FOR_EXTRACTION`** | Complete major academic survey (Fudan Univ et al.). Text density 4,304 c/p. 0 security findings. |
| 15 | `Lucrari/2602.02007v4.pdf` (Beyond RAG for Agent Memory) | PDF | 26 | `dab7e73744bbf87b...` | **`READY_FOR_EXTRACTION`** | Complete research paper (KCL & Tencent). Text density 3,535 c/p. 0 security findings. |
| 16 | `Lucrari/2602.19320v2.pdf` (Anatomy of Agentic Memory) | PDF | 19 | `2a0dc213369e195a...` | **`READY_FOR_EXTRACTION`** | Complete research paper. Text density 4,295 c/p. 0 security findings. |
| 17 | `Lucrari/ilide.info-rag-architecture-explained...pdf` | PDF | 19 | `b1cfb0ad9c41594c...` | **`BLOCKED`** | Commercial marketing blog post (Orq.ai) scraped by ilide.info. Contains UI prompt injection text (*"Summarize with AI"*). Non-academic. |
| 18 | `Lucrari/TheEvolutionofRAG.pdf` | PDF | 7 | `88a531e21b8c227b...` | **`READY_FOR_EXTRACTION`** | Complete academic paper (Univ. of AL-Zaytoonah). Text density 3,357 c/p. 0 security findings. |

---

## 3. Deep Analysis: Chip Huyen / Invisible Unicode (`Designing Machine Learning Systems.pdf`)

### Empirical Forensic Evidence:
Inspection of the extractable text layer in `Carti/Designing Machine Learning Systems.pdf` revealed two exact hits:

1. **Page 157 (offset ~line 12)**:
   * **Character**: `\u200b` (`ZERO WIDTH SPACE`, `U+200B`)
   * **Context**:
     ```text
     ...split your text into n-grams with n values of your choice.\u200b
     For those unfamiliar, an n-gram is a contiguous...
     ```
   * **Analysis**: Placed immediately preceding a newline between two sentences. This is a common artifact generated by automated typography and justified text layout engines (e.g. InDesign / Pandoc) to allow break points.

2. **Page 385 (offset ~line 8)**:
   * **Character**: `\u2060` (`WORD JOINER`, `U+2060`)
   * **Context**:
     ```text
     ...usually the Ops/platform/ML engineering team, production\xadi\u2060zes the models in prod...
     ```
   * **Analysis**: Follows a soft hyphen (`\xad`) inside the hyphenated word `production-izes`. It prevents the typesetting engine from breaking the word across lines after the hyphen.

### Assessment & Architectural Recommendation:
- **Typographical vs. Adversarial**: The characters are **100% benign typographical layout artifacts**, not malicious prompt injection steganography or base64 droppers.
- **Extractable Text**: They reside directly in the standard PDF text stream.
- **Deterministic Removal**: They can be stripped deterministically with regular expressions (`re.sub(r'[\u200b\u2060]', '', text)`) without any semantic alteration to technical concepts, definitions, or equations.
- **Normalizer vs. Allowlist**:
  - *Allowlisting*: Placing the whole 461-page PDF in `untrusted_content_allowlist.json` silences all future checks on that file, including if a genuine exploit were appended.
  - *Normalizer Sanitization (Recommended)*: Updating `_normalize()` in [`30_SCRIPTS/ingestion/convert_pdf_to_text.py`](../../30_SCRIPTS/ingestion/convert_pdf_to_text.py) to automatically strip non-printing zero-width and word-joiner characters during PDF conversion is strictly superior, preserves security rigor across the entire corpus, and removes the blocking finding cleanly.

---

## 4. Disposition of `ilide.info` & Preview Materials

The empirical audit confirms that files originating from `ilide.info` in this inbox represent significant integrity hazards:

1. **`ilide.info-ai-engineering-pr_d40f465cd950bfe282145288ae264c2b.pdf`**:
   - **Page Count**: Exactly **3 pages** out of ~400 pages.
   - **Content**: Dust jacket, ISBN barcode, and marketing blurbs. Zero technical chapters or conceptual content.
   - **Disposition**: **PERMANENTLY BLOCKED**. Excluded from concept extraction.

2. **`ilide.info-llm-engineer-s-handbook-pr_1877b944131838642c8d9a591e0c3884.pdf`**:
   - **Page Count**: Exactly **14 pages** out of ~400 pages.
   - **Content**: Introductory preface extract containing AWS CLI credentials setup commands (`cat ~/.aws/credentials`).
   - **Disposition**: **PERMANENTLY BLOCKED**. Excluded from concept extraction.

3. **`ilide.info-rag-architecture-explained-a-comprehensive-guide-2026-pr_f270193b63fe08f6ef29b43d2f2fbb0b.pdf`**:
   - **Content**: 19-page commercial product marketing post from Orq.ai, containing AI assistant invocation buttons.
   - **Disposition**: **BLOCKED**. Excluded from authoritative technical ontology corpus.

4. **`aposd2ndEdExtract.pdf`**:
   - **Page Count**: 20 pages out of ~200 pages.
   - **Content**: Scanned raster pages (0 text density) of John Ousterhout's book.
   - **Disposition**: **BLOCKED**. Must be replaced with full authorized digital edition.
