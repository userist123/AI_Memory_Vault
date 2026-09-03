# Cross-book provisional knowledge map

**Batch:** `BOOK_INGEST_2026-09-04_01`  
**Status:** `PROVISIONAL / HUMAN_GATED_ONLY`  
**Purpose:** connect recurring engineering and AI-system principles across the six ingested books without promoting them to canonical memory.

## 1. Knowledge is layered
AI applications sit on multiple layers: environment/task formulation, models and representations, retrieval/tools, orchestration, runtime infrastructure, evaluation, and operational feedback. Treating a single layer as the entire system hides failure modes at the boundaries.

**Sources:** AIMA; Building Agent-Powered Applications; Designing LLM Applications; Designing ML Systems; DDIA.

## 2. External context changes the architecture
When a system depends on data outside the model parameters, retrieval, context construction, freshness, provenance, and conflict handling become part of correctness rather than optional enhancements.

**Sources:** Designing LLM Applications; Building Agent-Powered Applications; DDIA.

## 3. Memory should be observable
A reusable memory mechanism needs explicit retrieval and application traces so that later evaluation can distinguish available context from context actually used. Merely storing knowledge does not establish effectiveness.

**Sources:** Building Agent-Powered Applications; Designing ML Systems; DDIA; project runtime-ablation evidence.

## 4. Retrieval is a trade-off
Top-k retrieval is not automatically beneficial: too little context can miss relevant evidence, while too much context can add distraction, latency, and competing information. Retrieval depth should therefore be evaluated as an application parameter.

**Sources:** Designing LLM Applications; Building Agent-Powered Applications; project memory-ablation evidence.

## 5. System correctness includes interfaces
Serialization, schema evolution, tool contracts, action permissions, feature pipelines, and model/application boundaries all behave like interfaces whose compatibility affects runtime correctness.

**Sources:** DDIA; Designing ML Systems; Building Agent-Powered Applications; Designing LLM Applications.

## 6. Offline success is not production proof
A model or agent can score well on a static benchmark and still fail under distribution shift, changed tool behavior, unseen inputs, operational constraints, or different workload characteristics.

**Sources:** Designing ML Systems; Building Agent-Powered Applications; AIMA.

## 7. Evaluation must match the claim
Claims about model quality, retrieval usefulness, agent reliability, or memory effectiveness require evaluation procedures that measure the claimed behavior. Structural presence of a component is weaker evidence than observed runtime behavior and verified outcomes.

**Sources:** Designing ML Systems; Building Agent-Powered Applications; project evaluation methodology.

## 8. Distributed uncertainty is a first-class constraint
Partial failures, timing differences, replication lag, partitioning, and coordination costs mean distributed execution cannot safely inherit single-machine assumptions.

**Sources:** DDIA; AIMA.

## 9. Learning is iterative and feedback-driven
Production learning systems loop through data collection, labeling, features, model development, evaluation, deployment, monitoring, and revision. Similar loops apply to agent memory: observe outcomes, retain useful evidence, re-evaluate, and revise.

**Sources:** Designing ML Systems; AIMA; project learning-engine backlog.

## 10. Adaptation mechanisms solve different problems
Prompting, retrieval, fine-tuning, alignment, and inference-time optimization are different control levers. They should not be treated as interchangeable fixes for the same failure mode.

**Sources:** Designing LLM Applications; Building Agent-Powered Applications.

## 11. Learned representations are infrastructure
Embeddings, hidden representations, and other learned features become reusable interfaces for downstream similarity, retrieval, classification, and generation. Their value depends on the task and evaluation distribution.

**Sources:** Learning Deep Learning; Designing LLM Applications; AIMA; Designing ML Systems.

## 12. Governance belongs in the technical design
Security, authorization, provenance, human review, social impact, and operational constraints are not downstream paperwork; they influence which system behaviors are acceptable and which memories or actions may be promoted.

**Sources:** AIMA; Building Agent-Powered Applications; project memory/security invariants.

## Promotion rule
These cross-book statements are synthesis candidates only. Promotion into canonical memory requires evidence-level verification against the original source text, duplicate/conflict analysis, explicit confidence, and the existing human-gated promotion process.
