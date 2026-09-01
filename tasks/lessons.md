# Lessons Learned

## Task 1
- Circular import between `temporal_controller.py` and `temporal_conflict.py` prevented `tests/test_temporal_controller.py` from loading during pytest collection when `tests/` is included. Moving the import inside the function or ordering imports cleanly resolves the issue.
- Context budget limits during pagination: when large `limit` (e.g. 50,000) or high numbers of duplicate test records are stored, `apply_degradation` must dynamically prune `ordered` notes in a loop by checking both `usage(ordered) <= soft_limit_bytes` and `serialized_size(ordered) <= hard_limit_bytes` to prevent `BudgetExceededError` on legitimate search queries.
- Note frontmatter schema strictly rejects additional arbitrary top-level properties like `title` and `symbol` (`additionalProperties: False`); structured fields must be nested within `content` or represented via canonical `tags`.

## Task 2
- Ollama silent truncation hazard: Ollama drops context when requests exceed `num_ctx` without returning HTTP errors. By passing `num_ctx` in `options` and performing pre-flight token estimation in `LocalProvider.generate`, we fail closed immediately before making HTTP calls.

## Fix Pack 2 Lessons

### Task 1 — Pairwise Conflict Deduplication & Cap
- Negation token preservation: `_tokenize` previously dropped tokens with `len <= 2`, which inadvertently discarded `"nu"` (the primary Romanian negation token). Ensuring tokens in `_NEGATION_TOKENS` are retained regardless of length prevents false negative contradiction classifications.
- Pairwise comparisons: By iterating `i` from `0..n` and `j` from `i+1..n` on pre-tokenized active notes, we eliminate the previous `N * (N - 1)` duplicate evaluations down to `N * (N - 1) / 2` and enforce a fail-closed hard cap (`max_notes: int = 2000`).

### Task 2 — Sleep Consolidation Budgeting
- Age-based prioritization: When canonical notes exceed the `max_items_per_consolidation_run` profile budget, sorting eligible notes by oldest timestamp (`updated` or `created`) guarantees consistent starvation prevention across incremental sleep runs while reporting `eligible_notes` vs `processed_notes`.

### Task 3 — Multi-Graph Node Type Controlled Vocabulary
- Graph node typing: Adding `node_type` with controlled vocabulary `{"fact", "decision", "procedure", "lesson", "task", "intent", "tool", "failure", "correction", "outcome"}` and mapping existing categories (`architecture` -> `decision`, `policy-lesson` -> `lesson`) enables typing across all 4 orthogonal graphs without requiring destructive migration of existing Markdown notes.

## Fix Pack 3 Lessons

### Task 1 & 2 — Outcome Events Telemetry & Immutability
- Frozen Dataclass Immutability: Defining `OutcomeEvent` with `frozen=True` and returning copies from `CouncilRunWithExecution.outcome_events` structurally guarantees append-only semantics, preventing in-place mutations from external callers or evaluation engines.
- Minimal Automatic Telemetry: Auto-populating a single `source="exit_code", confidence="low"` event upon model execution completion provides a baseline observation without premature coupling to proposal queues or evaluation engines.

### Task 3 — Human Labeling CLI
- Disconnected Human Audit Logging: Storing CLI-generated human labels in `04_MEMORY/outcome_events.jsonl` preserves append-only event logs on disk with timestamps, run IDs, and explicit confidence scores, ready for future offline analysis without runtime overhead.

## P0a Outcome Tracker Lessons
- Strict Storage Isolation: Enforcing hard boundary checks against canonical vault directories (`00_CORE`..`05_DECISIONS`, `99_SYSTEM`) at tracker initialization guarantees that execution outcome telemetry remains decoupled from verified memory.
- Fail-Closed Success Invariant: Disallowing `outcome="success"` when `verification_method="none"` prevents silent assumption of task success.
- Append-Only Provenance: Supporting multiple observations per `run_id` without destructive overwrites preserves full chronological audit trails (e.g. initial automated observation followed by human operator attestation).

## P0 Diagnostic Lessons — Budget vs Retrieval vs Model Capability
- Retrieval & Graph Horizon Bottleneck: 1-hop semantic retrieval alone isolates single documents and fails on multi-hop questions (Q06, Q07, Q11, Q12) by omitting cross-document governance premises (e.g. `AGENTS.md` operating contract $\leftrightarrow$ `vault_cognitive_rules.md` trust invariants).
- Multi-Signal Synergy (R4): Layering Lexical BM25, Entity Anchoring, and 2-hop Graph Expansion recovers accuracy from 57.8% (R1) to 85.0% (R4) while consuming ~492 tokens (44% fewer than Full-Context 878 tokens).
- Model Scaling Sensitivity: Upgrading from 3B to 7B on Full Context improves accuracy from 77.8% to 88.9%, but 3B models suffer heavily when negation tokens or negative constraints require multi-step deduction without explicit entity prompting.


