# Model-Evaluated Memory Benchmark V1

Date: 2026-09-04
Evaluator: GPT-5.6 Luna
Repository under test: `userist123/AI_Memory_Vault_CODEX_READY`
Evaluation target: remote `main` at time of inspection

## 1. Purpose

This is an independent cognitive/knowledge benchmark performed by the evaluator directly against the repository artifacts. It is intentionally different from the Ollama runtime benchmark.

The test asks: **Is the memory/knowledge that has been produced actually good memory for an AI system?**

It evaluates the stored knowledge artifacts, provenance, epistemic state, structure, retrieval fitness, discrimination, and safety properties. It does not claim to measure model behavior at runtime.

## 2. Evidence inspected

Primary artifacts inspected:

- `README.md`
- `99_SYSTEM/Memory_V6_Architecture.md`
- `06_INBOX/DERIVED/BOOKS/2026-09-04/consolidated/knowledge_atoms.jsonl`
- `06_INBOX/DERIVED/BOOKS/2026-09-04/consolidated/candidate_clusters.json`
- `06_INBOX/DERIVED/BOOKS/2026-09-04/consolidated/relation_matrix.json`
- `06_INBOX/DERIVED/BOOKS/2026-09-04/consolidated/promotion_candidates.md`
- `07_EVALUATION/golden_memory_effectiveness_v1/EXPERIMENT_CONTRACT.md`
- `07_EVALUATION/golden_memory_effectiveness_v1/FINAL_REPORT.md`

The runtime benchmark report itself was treated as evidence of what was executed, not as proof that the memory is cognitively useful.

## 3. Benchmark design

Each dimension is scored 0-10.

| Dimension | Score | Finding |
|---|---:|---|
| Provenance | 9 | Strong source IDs, hashes, locators and candidate lineage are retained. |
| Epistemic hygiene | 9 | Human-review status, verification requirement and limitations are explicit. |
| Trust-boundary discipline | 9 | Architecture clearly prevents automatic promotion and keeps derived data non-authoritative. |
| Atomicity | 7 | The synthesis atoms are compact and reusable, but several combine multiple concepts into one statement. |
| Specificity | 6 | Many statements are high-level engineering principles rather than operationally discriminative knowledge. |
| Actionability | 5 | Most atoms explain what is true, but do not encode when/why/how to act or what failure signature to expect. |
| Retrieval fitness | 7 | Cluster IDs and stable IDs help, but the visible synthesis layer has limited query-oriented aliases/terms. |
| Cross-source synthesis | 8 | Several atoms deliberately combine evidence across books rather than storing isolated summaries. |
| Conflict/supersession modeling | 5 | The relation system exists, but the inspected matrix is dominated by conservative `UNRELATED` classifications and provides little demonstrated semantic relation density. |
| Ranking discrimination | 3 | All ten listed promotion candidates have exactly the same priority (`0.5984`), which provides almost no ranking information. |
| Signal calibration | 4 | The inspected atoms repeatedly use the same confidence/reliability/utility-style values, suggesting a template-driven score rather than evidence-calibrated differentiation. |
| Temporal usefulness | 7 | Temporal validity is represented, but several values are broad qualitative statements rather than operational validity windows. |

### Composite score

**79/120 = 65.8%**

Interpretation: **GOOD FOUNDATION / NOT YET HIGH-FIDELITY COGNITIVE MEMORY**.

The vault is substantially stronger at governance, provenance and epistemic containment than at representing highly discriminative, actionable knowledge.

## 4. Direct cognitive tests

### T1 — Can the memory distinguish a fact from a synthesis?

**PASS.** The inspected atoms explicitly identify themselves as `SYNTHESIS`, retain supporting candidates and state that the synthesis is an interpretation requiring human checking.

### T2 — Can an agent determine whether it is safe to treat the item as canonical?

**PASS.** The item is marked `READY_FOR_HUMAN_REVIEW` and `verification_required=true`; the architecture explicitly requires human approval before promotion.

### T3 — Can the agent trace the statement back to source material?

**PASS.** The atoms contain book IDs, source paths, SHA-256 hashes, locators and candidate IDs.

### T4 — Does the memory encode enough context to avoid overgeneralization?

**PARTIAL.** Limitations are present, but the statements themselves are broad. For example, the adaptation atom states that prompting, retrieval, fine-tuning, alignment and inference-time methods are distinct levers, but does not encode a decision boundary for choosing among them.

### T5 — Does the memory help choose an action?

**PARTIAL/WEAK.** The current synthesis layer mostly provides principles. It is not yet rich in conditional procedures such as `if X, prefer Y; if Z, verify W`.

### T6 — Does the memory discriminate between closely related concepts?

**PARTIAL.** Twelve topical clusters are useful, but the relation matrix is extremely conservative. The inspected matrix contains large numbers of `UNRELATED` relations justified by `conservative_lexical_rule`. This protects against false relations but sacrifices associative retrieval quality.

### T7 — Are importance scores meaningful?

**FAIL.** The ten promotion candidates inspected all have the exact same priority value `0.5984`. A ranking in which every candidate ties cannot prioritize scarce human review capacity.

### T8 — Are confidence/reliability fields evidence-sensitive?

**FAIL/PARTIAL.** The inspected synthesis atoms repeatedly use identical values (`confidence=0.78`, `utility_score=0.88`, `reliability=0.72`, `reuse_probability=0.9`, `stability=0.84`, `misleading_risk=0.32`, `retention_cost=0.2`). This is consistent with a generation template and is not sufficient evidence of calibrated confidence.

### T9 — Does the memory separate knowledge quality from runtime effectiveness?

**PASS.** The repository's own experimental contract explicitly distinguishes exposure from causal influence, and the nightly report correctly labels the 90-run memory effectiveness conclusion as inconclusive.

### T10 — Does the memory system avoid silently converting provisional book material into canonical memory?

**PASS.** The inspected book atoms remain human-gated and the architecture states that no candidate becomes active automatically.

### T11 — Does the memory represent a useful multi-level abstraction?

**PARTIAL/PASS.** The system has raw candidates, clusters, synthesis atoms and promotion candidates. The missing layer is richer operationalization: examples, counterexamples, decision rules, failure modes and validated usage contexts.

### T12 — Is the benchmark itself epistemically honest?

**PASS.** The existing runtime benchmark explicitly records unexecuted suites rather than fabricating results. This is a strong property of the evaluation architecture.

## 5. Most important findings

### Finding A — Governance is ahead of cognition

The strongest part of the current memory is its trust model: provenance, human gating, lifecycle boundaries and separation of evidence from canonical memory are mature.

The weaker part is the actual cognitive compression: the knowledge atoms are mostly broad principles. They are useful as orientation but not yet sufficiently discriminative to function as expert-level procedural memory.

### Finding B — The ranking function is currently not useful enough

All ten promotion candidates shown in the promotion list have the same priority `0.5984`. This is a structural failure for prioritization. A human review queue needs meaningful ordering.

### Finding C — The numeric metadata appears under-calibrated

Repeated identical confidence/utility/reliability/etc. values across atoms are a warning sign. These numbers should be derived from observable evidence, source agreement, contradiction state, validation quality, reuse outcomes and temporal stability—not copied as defaults.

### Finding D — Conservative relation detection is safe but cognitively sparse

The relation matrix contains many `UNRELATED` classifications based on a conservative lexical rule. This minimizes hallucinated associations but prevents the graph from becoming a genuinely associative memory. The next stage should add evidence-backed semantic relations without relaxing the trust boundary.

### Finding E — The current book knowledge is a good semantic index, not yet a complete expert memory

The ten synthesis atoms are excellent candidates for high-level orientation. To become operational memory they need, where justified:

- decision conditions;
- counterexamples;
- failure modes;
- implementation consequences;
- positive/negative examples;
- dependencies;
- validated usage contexts;
- temporal/supersession rules;
- observed reuse outcomes.

## 6. Independent verdict

**Memory foundation: 8.0/10**

**Knowledge quality: 7.0/10**

**Operational usefulness: 5.5/10**

**Epistemic safety: 9.0/10**

**Associative/cognitive richness: 5.5/10**

**Ranking/calibration maturity: 4.0/10**

### Overall: **6.6/10 — SOLID FOUNDATION, COGNITIVE LAYER STILL IMMATURE**

This is not a failure of the project. It identifies the exact imbalance: the system has done a very good job making memory safe and auditable; it has not yet demonstrated that the memory representation is sufficiently rich and discriminative to behave like expert cognitive memory.

## 7. What I would build next

Priority order:

1. **Evidence-calibrated scoring** — eliminate identical default scores and compute confidence/reliability/utility from explicit evidence features.
2. **Operationalization layer** — turn high-level principles into conditional decision knowledge without promoting unverified claims.
3. **Semantic relation engine** — replace lexical-only relation sparsity with evidence-backed semantic links and explicit counterexamples/conflicts.
4. **Retrieval challenge set** — create a deterministic, human-authored query set from the existing knowledge atoms and score Precision@K, Recall@K and MRR.
5. **Memory quality gate** — require minimum provenance + specificity + actionability + calibration before promotion.
6. **Outcome feedback** — update usefulness estimates only from verified runtime outcomes, not from model self-report.

## 8. Boundary of this benchmark

This benchmark is an evaluator inspection of repository artifacts. It is **not** a substitute for real provider execution, causal ablation, or end-to-end poisoning tests. Those require runtime evidence.

The existing nightly report itself records that its 90-run treatment/control matrix had equal aggregate success (19/30 each) and McNemar p=1.0, and that the dedicated poisoning, harmful-memory, current-ablation, temporal and provenance harnesses were not executed in that run. Therefore this report deliberately does not claim those properties were empirically proven.


## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
