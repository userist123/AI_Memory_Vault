# Corpus Registry Report — Phase 8: Canonical Book Map Registry

**Document Version**: 1.0.0  
**Phase**: Phase 8 — Corpus Catalog & Reversible Consolidation  
**Authority**: `00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md` Secțiunea 2  
**Date**: 2026-10-04  

---

## 1. Registered Canonical Monographs in Catalog

The following 7 core monographs from `06_INBOX/Carti/` have been registered and validated in `BookToMemoryCatalog`:

| # | Source Identity | Monograph Title | Authors | Chapters Mapped | Status |
|---|---|---|---|---:|---|
| 1 | `kahneman-tfs-2011` | *Thinking, Fast and Slow* | Daniel Kahneman | 5 | `in_progress` |
| 2 | `ashby-dfb-1952` | *Design for a Brain* | W. Ross Ashby | 3 | `completed` |
| 3 | `ashby-itc-1956` | *An Introduction to Cybernetics* | W. Ross Ashby | 3 | `completed` |
| 4 | `laird-soar-2012` | *The Soar Cognitive Architecture* | John E. Laird | 5 | `in_progress` |
| 5 | `schacter-tulving-1994` | *Memory Systems 1994* | Daniel L. Schacter, Endel Tulving | 2 | `in_progress` |
| 6 | `squire-kandel-2000` | *Memory: From Mind to Molecules* | Larry R. Squire, Eric R. Kandel | 2 | `in_progress` |
| 7 | `kandel-2001` | *The Molecular Biology of Memory Storage* | Eric R. Kandel | 2 | `completed` |

---

## 2. Chapter Coverage & Gap Analysis Breakdown

### 2.1 `kahneman-tfs-2011`: *Thinking, Fast and Slow*
- **Total Parts/Chapters**: 5
- **Covered Sections**:
  - `Part 1: Two Systems` (System 1 & 2, Attention & Effort)
  - `Part 2: Heuristics & Biases` (Anchoring, Availability)
  - `Part 4: Choices` (Prospect Theory, Loss Aversion)
- **Uncovered Gaps**:
  - `Part 3: Overconfidence` (The Illusion of Understanding)
  - `Part 5: Two Selves` (Experienced Self vs Remembering Self)
- **Coverage Ratio**: **60.0%**

### 2.2 `ashby-dfb-1952`: *Design for a Brain*
- **Total Sections Mapped**: 3
- **Covered Sections**:
  - `Chapter 1: The Problem` (Homeostasis, Adaptive Behavior)
  - `Chapter 2: Dynamic Systems` (Variables, Equilibrium)
  - `Chapter 7: The Ultrastable System` (Step-mechanisms, Essential Variables)
- **Uncovered Gaps**: 0
- **Coverage Ratio**: **100.0%**

### 2.3 `ashby-itc-1956`: *An Introduction to Cybernetics*
- **Total Sections Mapped**: 3
- **Covered Sections**:
  - `Part 1: Mechanism` (Transformation, State-Determined System)
  - `Part 2: Variety` (Law of Requisite Variety)
  - `Part 3: Regulation & Control` (Error-Controlled Regulator)
- **Coverage Ratio**: **100.0%**

### 2.4 `laird-soar-2012`: *The Soar Cognitive Architecture*
- **Total Sections Mapped**: 5
- **Covered Sections**:
  - `Chapter 2: Requirements` (Cognitive Architecture Criteria)
  - `Chapter 3: PSCM` (Problem-Space Computational Model)
  - `Chapter 6: Chunking` (Impasses & Substate Flattening)
  - `Chapter 8: Semantic Memory` (SMem Associative Retrieval)
  - `Chapter 9: Episodic Memory` (EpMem Temporal Stream)
- **Coverage Ratio**: **100.0%**

### 2.5 `schacter-tulving-1994`: *Memory Systems 1994*
- **Total Sections Mapped**: 2
- **Covered Sections**:
  - `Chapter 1: What Are the Memory Systems?` (PRS, Episodic, Semantic, Procedural, Working Memory)
  - `Chapter 2: Neuropsychological Dissociations` (Amnesia & Priming Dissociations)
- **Coverage Ratio**: **100.0%**

### 2.6 `squire-kandel-2000`: *Memory: From Mind to Molecules*
- **Total Sections Mapped**: 2
- **Covered Sections**:
  - `Chapter 4: The Hippocampus and Declarative Memory` (Medial Temporal Lobe Structures)
  - `Chapter 6: Synaptic Plasticity and Long-Term Storage` (Cellular & Molecular Mechanisms)
- **Coverage Ratio**: **100.0%**

### 2.7 `kandel-2001`: *The Molecular Biology of Memory Storage*
- **Total Sections Mapped**: 2
- **Covered Sections**:
  - `Section: Short-term vs Long-term Facilitation` (CREB-Mediated Gene Expression)
  - `Section: Synaptic Tagging` (Synaptic Capture Hypothesis)
- **Coverage Ratio**: **100.0%**

---

## 3. Policy Adherence

All 7 monograph records:
1. Comply with `BookToMemoryType.BOOK_MAP` schema.
2. Are preserved in `VERIFIED` state (`AWAITING_OWNER_APPROVAL`).
3. Store structured bibliographic references without copying raw book texts into retrieval.
4. Have their integrity cryptographically anchored in the catalog digest.
