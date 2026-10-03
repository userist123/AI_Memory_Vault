# Book to Engineering Hypothesis Mapping (H1 Track)

**Track**: Book-to-Memory  
**Status**: Pre-registered experimental mapping  
**Corpus ID**: `book-to-memory-raw-inbox` (`PARTIAL_SOURCE_CORPUS`)  
**Corpus SHA-256**: `d7d6e25b77608598befb2812a7b93397286bdf0912c060bb12fb4efa5c409ce5`  

## 1. Operating Policy

Conform regulilor din `AGENTS.md` și `RESEARCH-TRACK-CONTRACT.md`:
1. "Cartea spune că mecanismul X este bun, deci implementăm X" este **strict interzisă**.
2. Formularea validă: *"Sursa susține principiul X la locația Y. Acesta generează ipoteza inginerească Z, care poate fi testată prin experimentul E."*
3. Pragurile numerice, metricele, controalele și condițiile de eșec sunt fixate **înainte** de orice rulare experimentală.
4. Nicio modificare nu se aplică pe `MemoryController` sau pe retrieval de producție în această etapă.

---

## 2. Testable Hypothesis Specifications

### H1-BOOK-001 (Laird 2012 — Architectural Memory Separation)

```yaml
hypothesis_id: H1-BOOK-001
source_id: laird_soar_cognitive_architecture
source_location: "Chapter 2 (Requirements for Cognitive Architectures, pp. 29-45) & Chapter 8-9 (Semantic & Episodic Memory, pp. 241-310)"
source_sha256: 5af8803a362cbda476ef2329c8e1e31fc53be7fac7501d813bf7dc46962f8dd8
edition: "MIT Press, 2012"
principle: "Cognitive architectures require structural differentiation between short-term working memory, procedural rules, semantic facts, and episodic temporal experience rather than a uniform, undifferentiated vector store."
engineering_hypothesis: "Routing memory queries through type-specialized candidate generators (filtering by note lifecycle and type prior to dense/lexical ranking) improves retrieval precision on structured queries compared to an undifferentiated single-pool BM25/dense search."
mechanism_variant: "Isolated candidate generation pipeline: type-filtered retrieval stages evaluated in benchmark harness."
baseline: "Current production retrieval (undifferentiated search across all non-RAW lifecycles with standard BM25 candidate generation)."
control: "Equal token and candidate budget (top-k=10) across baseline and variant."
metric: "Precision@5 on structured intent benchmark cases"
success_threshold: "Improvement >= +0.10 P@5 with 0% regression on unconstrained factual queries"
failure_condition: "Improvement < +0.05 P@5 or any latency increase > 25%"
leakage_risk: "Query syntax leaking target note tags or metadata fields"
selection_bias_risk: "Over-sampling queries targeting heavily typed notes"
repetitions: 3
negative_result_policy: "If threshold not met, record as HYPOTHESIS_NOT_SUPPORTED; retain all logs in research archive."
```

---

### H1-BOOK-002 (Anderson 2007 — Human Associative Memory Activation Spreading)

```yaml
hypothesis_id: H1-BOOK-002
source_id: newell_how_can_human_mind_occur # Content verified as John R. Anderson 2007
source_location: "Chapter 3 (Human Associative Memory, pp. 91-134)"
source_sha256: a697868c0077aff357f38d87dbc6464d4891ae4d949fe0f6d8faf26c34484e52
edition: "Oxford University Press, 2007"
principle: "Associative recall in declarative memory operates via activation spreading across network links based on contextual cues rather than direct surface word matching."
engineering_hypothesis: "1-hop graph neighbor expansion from initial top-3 lexical candidates will recover gold notes in indirect-cue queries where query tokens have zero lexical overlap with the gold note."
mechanism_variant: "BM25 candidate retrieval + 1-hop typed edge expansion (isolated research runner)."
baseline: "Pure lexical BM25 candidate retrieval (0-hop)."
control: "Exact same token budget (max 2500 synthesis tokens) and evaluation dataset."
metric: "Recall@10 on indirect_cue family cases (split=development/held_out)"
success_threshold: "Recall@10 >= 0.70 on indirect_cue family (baseline expected <= 0.20)"
failure_condition: "Recall@10 < 0.50 on indirect_cue family"
leakage_risk: "Graph edges constructed specifically around benchmark queries"
selection_bias_risk: "Selecting only highly-connected hub nodes as gold targets"
repetitions: 3
negative_result_policy: "Record as HYPOTHESIS_NOT_SUPPORTED; document graph density limitations."
```

---

### H1-BOOK-003 (Schacter & Tulving 1994 — Memory Systems Taxonomy & Dissociations)

```yaml
hypothesis_id: H1-BOOK-003
source_id: schacter_tulving_memory_systems_1994
source_location: "Chapter 1 (What are the memory systems of 1994?, pp. 1-38)"
source_sha256: 00a9bfee997157d3d9c0ab110026fccbd0a926f692c5f7ce824ddf3dd714bfab
edition: "MIT Press, 1994"
principle: "Multiple memory systems exhibit functional and stochastic independence; tests tapping semantic versus procedural retrieval require distinct retrieval cues."
engineering_hypothesis: "Explicitly classifying query intent into semantic versus procedural categories allows applying distinct similarity scoring weights, reducing false-positive distractor retrieval."
mechanism_variant: "Two-stage intent classification + intent-conditioned scoring."
baseline: "Single static scoring configuration across all query types."
control: "Static candidate pool across both runs."
metric: "Distractor rejection rate (NDCG@10 penalty on distractor items)"
success_threshold: "Reduction of top-5 distractor intrusions by >= 25% without degrading gold recall"
failure_condition: "Distractor reduction < 10% or gold recall drop > 2%"
leakage_risk: "Intent classifier trained on benchmark query templates"
selection_bias_risk: "Constructing artificial distractors that have extreme surface dissimilarity"
repetitions: 3
negative_result_policy: "Mark HYPOTHESIS_NOT_SUPPORTED; preserve scoring telemetry."
```

---

### H1-BOOK-004 (Squire & Kandel 2008 — Synaptic Plasticity & Consolidation Dynamics)

```yaml
hypothesis_id: H1-BOOK-004
source_id: squire_kandel_mind_to_molecules
source_location: "Chapter 5 (Synaptic Plasticity, pp. 83-118) & Chapter 8 (From Short-Term to Long-Term Memory, pp. 175-212)"
source_sha256: 5246aa8001699b90bdf74d87d4221fb048024fee719db776f8f6163bf911336d
edition: "Roberts & Company, 2nd ed. 2008"
principle: "Long-term memory stability requires repetitive rehearsal and consolidation gating to prevent catastrophic interference from transient short-term inputs."
engineering_hypothesis: "Gating candidate memory additions behind repetition counts and attestation (Review -> Verified) maintains vault retrieval precision over multi-turn interactions compared to ungated immediate ingestion."
mechanism_variant: "Rehearsal-gated memory buffer prior to index consolidation."
baseline: "Immediate per-turn ingestion into searchable active index."
control: "Fixed 50-turn synthetic multi-agent conversation workload."
metric: "Retrieval precision (P@5) on core invariant queries after 50 turns"
success_threshold: "Baseline degrades by > 15% due to clutter, while gated variant degrades by < 3%"
failure_condition: "Gated variant fails to retain novel verified facts within 3 turns"
leakage_risk: "Evaluating on synthetic dialogs designed specifically for gating criteria"
selection_bias_risk: "Excluding ambiguous multi-turn dialog contexts"
repetitions: 3
negative_result_policy: "Record INCONCLUSIVE if baseline does not degrade sufficiently."
```

---

### H1-BOOK-005 (Newell 1990 — Unified Theories of Cognition & Time-Scale Hierarchy)

```yaml
hypothesis_id: H1-BOOK-005
source_id: newell_unified_theories_of_cognition
source_location: "Chapter 3 (Human Cognitive Architecture, pp. 111-158)"
source_sha256: cc26a315b873972ff9f2bb7a42d814b6b87076ddea9c7f8adb6279ae48878aeb
edition: "Harvard University Press, 1990"
principle: "Cognitive behavior is structured into strict time bands (~10ms biological, ~100ms deliberate act, ~10s unit task). Operations at lower bands must complete within strict latency envelopes to support higher band tasks."
engineering_hypothesis: "Hard token and latency budgeting in the retrieval pipeline (terminating graph traversal at 1-hop if latency exceeds 50ms) preserves agent deliberation quality under time-bounded execution."
mechanism_variant: "Time-bounded early-exit graph retrieval."
baseline: "Exhaustive multi-hop graph retrieval without latency cutoff."
control: "Identical benchmark tasks and prompt context constraints."
metric: "Time-to-first-token and end-to-end task completion rate under timeout (2000ms)"
success_threshold: "Task completion rate under timeout increases from <= 70% to >= 95%"
failure_condition: "Critical context omission rate exceeds 5%"
leakage_risk: "Synthetic timeouts tailored to exact variant completion times"
selection_bias_risk: "Benchmarking only on overly large graphs"
repetitions: 5
negative_result_policy: "Record as HYPOTHESIS_NOT_SUPPORTED."
```
