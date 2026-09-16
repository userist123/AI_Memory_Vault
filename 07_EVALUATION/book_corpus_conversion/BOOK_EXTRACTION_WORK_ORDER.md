# BOOK_EXTRACTION_WORK_ORDER.md — Corpus Ingestion Work Order (Package r033/corpus-ingestion)

**Package**: `r033/corpus-ingestion`  
**Target Corpus**: `06_INBOX/Carti` (20 Books, 1,120 pre-flighted chunks, 8.13 MB text)  
**Status**: COMPLETED  
**Procedure Reference**: [`10_DOCUMENTATION/procedures/Ingesting_A_Book_Into_The_Ontology.md`](../../10_DOCUMENTATION/procedures/Ingesting_A_Book_Into_The_Ontology.md)  
**Evidence Log**: [`07_EVALUATION/book_corpus_conversion/FINDINGS.md`](FINDINGS.md)  

---

## Executive Summary & Work Order Mandate

This work order governs the automated model-assisted extraction of technical cognitive concepts from the 20 pre-flighted monograph and paper text files in `06_INBOX/Carti` and their merging into the 16 canonical ontology slot markdown files under `01_ARCHITECTURE/ontology/slots/`.

### Core Invariants & Execution Rules

1. **Strict Gate Enforcement**: All extracted candidate concepts must pass 4 non-negotiable validation gates in `model_extract_concepts.py` and `gate_agent_candidates.py`:
   - **Grounding Gate**: 12-word verbatim run covering $\ge 70\%$ of evidence quote in the source chunk.
   - **Paraphrase Gate**: No shared 8-gram with evidence AND Jaccard token overlap $< 0.60$.
   - **Shape Gate**: Noun phrase term 1–4 words, definition $\ge 12$ words ending in clean punctuation.
   - **Slot Gate**: Must map to one of the 16 canonical ontology slots.
2. **Selectivity & Ranking**:
   - Ranked exclusively by `occurrences` (count of distinct sections defining the term).
   - Candidates with `occurrences >= 3` are prioritized for review.
3. **Promotion Boundary**:
   - **PROMOTE NOTHING**. Promotion to `REVIEW` notes is out of scope for this package and requires a separate gated evaluation step with individual candidate reasoning.

---

## Corpus Inventory & Execution Summary

All 20 books have been extracted and gated using agent-assisted extraction and validated against all 4 strict gates:

| # | Short Name | Chunks | Candidates Extracted | Net-New Merged | Status |
|---|---|---:|---:|---:|---|
| 1 | `7688_jkt_au` | 1 | 4 | 4 | COMPLETED |
| 2 | `comparison_cognitive_architectures` | 1 | 0 | 0 | COMPLETED |
| 3 | `wiener_cybernetics` | 4 | 1 | 1 | COMPLETED |
| 4 | `kandel_2001_molecular_biology_of_memory` | 4 | 11 | 11 | COMPLETED |
| 5 | `quantum_consciousness_framework` | 7 | 5 | 5 | COMPLETED |
| 6 | `2504.05840v1` | 9 | 28 | 28 | COMPLETED |
| 7 | `sarfraz22a` | 12 | 16 | 16 | COMPLETED |
| 8 | `wcs_1488` | 15 | 39 | 35 | COMPLETED |
| 9 | `machine_learning_report` | 19 | 43 | 43 | COMPLETED |
| 10 | `2601.09113v1` | 22 | 59 | 58 | COMPLETED |
| 11 | `memory_in_the_age_of_ai_agents` | 35 | 61 | 61 | COMPLETED |
| 12 | `ashby_intro_to_cybernetics` | 53 | 341 | 336 | COMPLETED |
| 13 | `why_we_forget` | 84 | 74 | 72 | COMPLETED |
| 14 | `squire_kandel_mind_to_molecules` | 87 | 253 | 239 | COMPLETED |
| 15 | `ashby_design_for_a_brain` | 94 | 304 | 303 | COMPLETED |
| 16 | `schacter_tulving_memory_systems_1994` | 95 | 380 | 364 | COMPLETED |
| 17 | `minsky_society_of_mind` | 111 | 358 | 351 | COMPLETED |
| 18 | `laird_soar_cognitive_architecture` | 126 | 450 | 442 | COMPLETED |
| 19 | `newell_how_can_human_mind_occur` | 154 | 149 | 147 | COMPLETED |
| 20 | `newell_unified_theories_of_cognition` | 187 | 765 | 742 | COMPLETED |

**Total Candidates Extracted**: 3,342  
**Total Net-New Merged**: 3,258  
**Total Deduplicated**: 84  

---

## Deliverables Checklist

- [x] Pre-flight verification of 20 text files (1,120 chunks, 0 over context limit)
- [x] Extraction pipeline chunk preparation (`gate_agent_candidates.py chunks`)
- [x] Staging JSON files generated under `staging/*.json` and `staging/*_rej.json` for all 20 books
- [x] Corpus summary report written to `staging/corpus_run_report.json`
- [x] Net-new candidate concepts (3,258 rows) merged into 16 slot table files under `01_ARCHITECTURE/ontology/slots/*.md`
- [x] Test suite verification (`20_TESTS/test_book_ingestion_pipeline.py`, `20_TESTS/test_ontology_scaffold.py`, `20_TESTS/test_concept_promotion.py`)
- [x] Layout validation (`python 30_SCRIPTS/verification/validate_repository_layout.py --repo .`)
