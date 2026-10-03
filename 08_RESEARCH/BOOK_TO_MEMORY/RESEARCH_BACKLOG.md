# Book-to-Memory Research Backlog — Remaining Raw Inbox Sources

Status: research-only candidate queue
PR: #206
Corpus: `book-to-memory-raw-inbox`
Current corpus status: `PARTIAL_SOURCE_CORPUS`

## Rules

- This file is a research queue, not a production specification.
- A candidate hypothesis is NOT evidence that the source supports the mechanism.
- Page-level claims require the exact source artifact and verified location.
- No candidate may modify production retrieval or MemoryController.
- No candidate may be promoted to ACTIVE.
- Final hypotheses require: source identity -> exact location -> evidence -> falsifiable prediction -> control -> metric -> pre-registered threshold.
- Sources marked `SOURCE_UNAVAILABLE` are excluded from empirical derivation.

## Sources requiring further research

| source_id | source | status | research disposition |
|---|---|---|---|
| 2504.05840v1 | Momentum Boosted Episodic Memory for Improving Learning in Long-Tailed RL Environments | AVAILABLE | Candidate: prioritize rare/long-tail episodic traces; validate on fixed long-tail vs uniform benchmark. |
| 2601.09113v1 | The AI Hippocampus: How Far are We From Human Memory? | AVAILABLE | Candidate: memory-type routing/functional separation; validate against single-pool retrieval. |
| ashby_design_for_a_brain | Design for a Brain | AVAILABLE | Candidate: adaptive regulation/ultrastability-inspired control; must first identify an exact source passage and translate it into a falsifiable control-loop experiment. |
| ashby_intro_to_cybernetics | An Introduction to Cybernetics | AVAILABLE | Candidate: feedback/regulation and variety-management mechanisms; must be grounded in exact chapters before hypothesis registration. |
| kandel_2001_molecular_biology_of_memory | The Molecular Biology of Memory Storage | AVAILABLE | Candidate: consolidation/stability mechanism; test persistence vs transient ingestion without changing production. |
| machine_learning_report | Machine learning: the power and promise of computers that learn by example | AVAILABLE | Candidate: provenance/trust-aware evaluation and human-in-the-loop verification; exact supporting section required before registration. |
| memory_in_the_age_of_ai_agents | Memory in the Age of AI Agents | AVAILABLE | Candidate: functional separation of memory forms and lifecycle operations; compare typed routing against undifferentiated retrieval. |
| minsky_society_of_mind | The Society of Mind | AVAILABLE | Candidate: specialist-agent decomposition for memory tasks; first identify source passages and a falsifiable routing experiment. |
| newell_unified_theories_of_cognition | Unified Theories of Cognition | AVAILABLE | Candidate: time-scale/latency budgeting and bounded retrieval; exact chapter evidence required before registration. |
| quantum_consciousness_framework | Unified Quantum-Consciousness Framework | AVAILABLE | Research-only review candidate. Non-peer-reviewed; no production hypothesis until independent corroboration and methodological validation. |
| sarfraz22a | Synergy Between Synaptic Consolidation and Experience Replay | AVAILABLE | Candidate: replay + consolidation scheduling; evaluate retention/interference under fixed continual-learning workload. |
| schacter_tulving_memory_systems_1994 | Memory Systems 1994 | AVAILABLE | Candidate: memory-system dissociation and intent-conditioned retrieval; exact chapter evidence required. |
| squire_kandel_mind_to_molecules | Memory: From Mind to Molecules | AVAILABLE | Candidate: consolidation/rehearsal gating; must separate biological analogy from engineering mechanism and test independently. |
| wcs_1488 | ACT-R: A cognitive architecture for modeling cognition | AVAILABLE | Existing H1-BOOK-002 grounding; do not create duplicate hypothesis without a distinct prediction. |
| laird_soar_cognitive_architecture | The Soar Cognitive Architecture | AVAILABLE | Existing H1-BOOK-001 grounding; do not create duplicate hypothesis without a distinct prediction. |

## Excluded from empirical derivation

- `wiener_cybernetics` — incomplete excerpt; B-0001 remains active.
- `why_we_forget` — Bookey summary, not the primary OUP monograph; B-0003 remains active.
- `7688_jkt_au` — promotional jacket; B-0004 closed.
- `comparison_cognitive_architectures` — unsigned tertiary table; B-0005 closed.
- `newell_how_can_human_mind_occur` — corrupted artifact; B-0002 closed.

## Candidate experiment queue

### EXP-CAND-001 — Rare-memory prioritization

Source: `2504.05840v1`

Prediction: a retrieval policy that explicitly prioritizes rare/long-tail episodic evidence can improve recall for rare-event queries without materially degrading common-event retrieval.

Control:
- same corpus;
- same candidate budget;
- same query set;
- only prioritization policy changes.

Metrics:
- Recall@10 on rare queries;
- Recall@10 on common queries;
- latency;
- false-positive rate.

Status: candidate only; page-level source mapping required.

### EXP-CAND-002 — Functional memory routing

Source: `2601.09113v1`

Prediction: explicit memory-function routing can reduce distractor retrieval relative to a single undifferentiated pool.

Control:
- identical corpus;
- identical token budget;
- fixed held-out query set.

Metrics:
- Precision@5;
- Recall@10;
- distractor rate;
- latency.

Status: candidate only; exact evidence mapping required.

### EXP-CAND-003 — Feedback regulation

Sources:
- `ashby_design_for_a_brain`
- `ashby_intro_to_cybernetics`

Prediction: an explicit bounded feedback controller can stabilize a measurable retrieval or consolidation variable under changing workload.

Required safeguards:
- define controlled variable before experiment;
- define disturbance before experiment;
- define stability criterion before experiment;
- no production modification.

Status: candidate only; exact passages and engineering variable must be fixed before registration.

### EXP-CAND-004 — Consolidation gating

Sources:
- `kandel_2001_molecular_biology_of_memory`
- `squire_kandel_mind_to_molecules`
- `sarfraz22a`

Prediction: delayed/rehearsal-gated consolidation can reduce interference compared with immediate ingestion under a fixed continual workload.

Metrics:
- P@5 on invariant queries;
- false-memory/distractor rate;
- retention of verified novel facts;
- recovery after repeated exposure.

Status: candidate only; biological evidence must not be treated as direct engineering proof.

### EXP-CAND-005 — Provenance-aware learning

Source:
`machine_learning_report`

Prediction: requiring provenance/trust metadata before memory consolidation reduces unsupported retrieval claims under a fixed task set.

Metrics:
- unsupported-claim rate;
- provenance completeness;
- task success;
- latency overhead.

Status: candidate only; exact source section required.

### EXP-CAND-006 — Lifecycle/type separation

Sources:
- `memory_in_the_age_of_ai_agents`
- `schacter_tulving_memory_systems_1994`

Prediction: explicit lifecycle/type routing reduces cross-type distractors while preserving gold recall.

Metrics:
- distractor rate;
- Recall@10;
- P@5;
- regression on unconstrained factual queries.

Status: overlaps conceptually with existing H1-BOOK-001/H1-BOOK-003; merge only if the prediction is genuinely distinct.

### EXP-CAND-007 — Specialist routing

Source: `minsky_society_of_mind`

Prediction: decomposing a memory task into specialist retrieval roles can improve task accuracy under a fixed token budget.

Required control:
- same model;
- same token budget;
- same benchmark;
- only routing topology differs.

Status: candidate only; exact source evidence required.

### EXP-CAND-008 — Time-bounded retrieval

Source: `newell_unified_theories_of_cognition`

Prediction: explicit latency budgets and bounded retrieval depth improve completion rate under a fixed timeout, provided critical-context omission stays below a pre-registered threshold.

Metrics:
- task completion rate;
- time-to-first-token;
- end-to-end latency;
- critical-context omission rate.

Status: overlaps conceptually with existing H1-BOOK-005; do not duplicate.

### EXP-CAND-009 — Replay/consolidation scheduling

Source: `sarfraz22a`

Prediction: scheduled replay plus consolidation can improve continual-learning retention relative to either immediate ingestion or replay-free consolidation.

Controls:
- fixed stream;
- fixed memory budget;
- fixed evaluation points;
- same model/configuration.

Metrics:
- retention;
- interference;
- recovery after distribution shift.

Status: candidate only; exact source-to-prediction mapping required.

### EXP-CAND-010 — Non-peer-reviewed source quarantine

Source: `quantum_consciousness_framework`

Prediction: no engineering hypothesis should be promoted from a non-peer-reviewed source without independent corroboration.

This is a governance experiment, not a production mechanism.

Metrics:
- corroboration rate;
- unsupported-claim rate;
- false-positive hypothesis rate.

Status: governance candidate only.

## Mandatory next step

Before converting any candidate into a registered H1 hypothesis:

1. retrieve the exact source artifact;
2. identify exact chapter/page/section;
3. extract only the minimum necessary evidence;
4. write the engineering prediction in falsifiable form;
5. define baseline/control/metric;
6. freeze the success threshold before execution;
7. add leakage and selection-bias checks;
8. register the experiment;
9. run it in isolation;
10. record positive, negative, and inconclusive results.

No candidate in this document is an implementation instruction.
