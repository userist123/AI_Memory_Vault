---
agent: CLAUDE_SONNET
last_updated_utc: 2026-09-07T12:00:00Z
repository: userist123/AI_Memory_Vault_CODEX_READY
working_branch: r025/readme-rewrite (+ 5 sibling package branches, see below; each descends from the previous, see Evidence_refs)
base_main_sha: bc7a7df82 (origin/main tip at time of isolated-worktree verification; re-fetched and re-verified more than once this session, per the r025 brief's own push-discipline correction)
current_commit_sha: HEAD
project_id: AI_MEMORY_VAULT
application: AI Memory Vault / Memory Engine
working_folder: 03_IMPLEMENTATION/packages/, 07_EVALUATION/, 20_TESTS/, README.md, 00_GOVERNANCE/VAULT_STATE.md
current_task: r025 session — all 7 packages finished (WP-11, WP-8, WP-9, WP-12, WP-6, WP-10, WP-13, in that dependency order)
status: PAUSED (not merged to main, per brief) — every package DONE, each on its own branch, verified via isolated-worktree cherry-pick against a fresh origin/main tip and pushed with `git ls-remote` confirmation (not from memory, per this session's own corrected discipline)
in_progress: []
next_actions:
  - an owner reviews/merges the 6 finished package branches (order matters: readme-rewrite is the tip of the full chain and can be merged alone; the others are its ancestors)
  - WP-6's own finding (MemoryController.update() rejects 45/45 real edge writes: lifecycle gate + UUID-format schema requirement) needs an owner decision before any future edge-promotion attempt — this session did not loosen either gate, per its own no-policy-bypass requirement
  - WP-10's finding (write path still empty today, but fully exposed) needs an owner decision among WP-4's three restated options — option B (redirect propose() to a content root) now has a measured zero-note migration cost
  - WP-9's Phase A root cause (RRF fuses ranks, not raw scores, so a fully-tied zero-signal query still produces a complete, confident-looking ranking) likely affects other fused_score consumers beyond WP-11's abstention metric and WP-8's ranking arm — not audited further this session
blockers: []
risks:
  - one branch per package; do not combine packages in one branch (respected: 6 separate branches below, correctly nested by dependency)
  - do not touch path_resolver.py (WP-10 stayed measurement-only, no migration, per the brief's forbidden list)
  - do not adjust edge-proposer thresholds; WP-6 promoted from WP-2's own unmodified sample, did not re-run the proposer
  - never report a branch "safe on origin" without `git ls-remote --heads origin | grep <branch>` confirming the exact SHA — a wrong belief that work is safe is worse than knowing it is lost (this was the r024→r025 handoff's own correction, honoured throughout this session)
  - origin/main moved twice during this session's earlier work (unrelated commits); always re-fetch and re-verify the current tip before building an isolated baseline worktree, never trust a task brief's stated base SHA as still current
Evidence_refs:
  - 00_GOVERNANCE/VAULT_STATE.md (updated this session: graph ceiling 33->32, update()/write-path rows added)
  - 07_EVALUATION/r025_wp11_abstention/ (branch r025/abstention-metric, cff971b57)
  - 07_EVALUATION/r025_wp8_a1_heldout/ (branch r025/a1-heldout-validation, 284eea101)
  - 07_EVALUATION/r025_wp9_classifier/ (branch r025/query-classifier, cf7cbe02e)
  - 07_EVALUATION/r025_wp12_graph_expansion/ (branch r025/graph-expansion, 059ba9980)
  - 07_EVALUATION/r025_wp6_edge_promotion/ (branch r025/edge-promotion, ce275f600)
  - 07_EVALUATION/r025_wp10_write_path_damage/ (branch r025/write-path-damage, 646b518a8)
  - README.md, 00_GOVERNANCE/VAULT_STATE.md (branch r025/readme-rewrite, e6897653d)
related_agents: ANTIGRAVITY, CODEX, LUNA, PERPLEXITY, CLAUDE_OPUS
NEXT: an owner reviews/merges the chain (readme-rewrite carries all of it); separately decides the edge-promotion and write-path-redirect questions WP-6/WP-10 surfaced but deliberately did not resolve
---

# CLAUDE_SONNET — r025 session log (2026-09-07)

Sequence per the r025 brief's dependency graph: WP-11 first (blocking — it
changes scoring for 3/30 held-out cases), then WP-8 and WP-9 in either order
(both feed WP-12), then WP-12, then WP-6 (unblocked by WP-2's GO from r024),
then WP-10 (independent, measurement-only), then WP-13 last.

Three corrections carried from the r024→r025 handoff, honoured throughout:
verify branch safety with `git ls-remote`, never from memory; a wrong belief
that work is safe is worse than knowing it is lost; WP-6's blocking
ambiguity was the brief's own writing error, resolved this session (an edge
has no lifecycle — the NOTE whose `relations:` gains the edge does).

## WP-11 — abstention metric (blocking, ran first)

Branch: `r025/abstention-metric`

`run_production_arms.py` scored every "should abstain" case as automatically
correct (`not (gold & context)`, always true since gold is always `[]`).
Phase A measured the actual signal (top fused_score, margin, generator
agreement) across answerable/unanswerable/20 generated nonsense queries:
none separate. Root cause: RRF fuses ranks, not raw scores, so a fully-tied
zero-signal query still produces a complete, deterministic, confident-looking
ranking. Phase C fixed the metric regardless of the disappointing Phase B
result: abstain cases now score `UNMEASURABLE`, not a fabricated pass;
`summarise()`/`mcnemar()` report `n`/`n_measurable`/`n_unmeasurable`
everywhere. No abstention BEHAVIOR was added — this package only stopped the
metric from lying about there being one.

## WP-8 — A1 held-out validation

Branch: `r025/a1-heldout-validation`

r024 WP-1's A1 (rank by `fused_score`) was a dev-only result. Pre-registered
a threshold (>=+2 measurable context-recall cases confirms) before running.
Held-out, using WP-11's corrected scoring: 4/27 -> 6/27 measurable (+2,
meeting the threshold exactly). **Default flipped**: `ranking_arm` now
resolves to `RANKING_ARM_FUSED_SCORE` when unspecified;
`RANKING_ARM_BASELINE` stays available explicitly. Found and closed one trap
from the flip itself: `run_production_arms.py`'s own `build()` didn't pin
`ranking_arm`, so R016's graph study would have silently started comparing
under the new default — pinned it to `RANKING_ARM_BASELINE` explicitly, with
the reasoning recorded in code, and verified the pin by reproducing WP-11's
exact committed numbers after the flip.

## WP-9 — query-classifier filter collapse

Branch: `r025/query-classifier`

Proved deterministically (not just anecdotally) that any query containing
"verified" or "classified" as ordinary text triggers a classifier-inferred
lifecycle filter for a stage with zero notes in the real corpus — guaranteed
pool collapse. 10/42 benchmark cases affected (measured directly; an earlier
pool-size threshold undercounted this at 8). Implemented `classifier_filter_arm`
(`hard`/`boost`/`conditional`) in `RetrievalEngine.retrieve()` without
touching the single-gate AST invariant `test_candidate_generation_call_path.py`
already proves — softening logic runs only on `generate_candidates()`'s
output, never on the pre-ranking gated variable. `boost`/`conditional` both
recover 15/37 vs. `hard`'s 10/37 measurable context-recall cases. Production
default stays `hard` (unchanged), per the brief's own requirement 3 — this
is a reported finding, not a flip.

## WP-12 — graph benchmark statistical power

Branch: `r025/graph-expansion`

Re-measured the disjoint-node ceiling fresh instead of trusting the brief:
**32, not 33** (the brief's own number, traced to a stale "~33" estimate in
this vault's historical `R016_RESULT.md`) — confirmed via an unchanged
278-edge graph. Tried to add cases up to that ceiling: of 56 eligible
candidate edges, only **2** produced a case genuinely dependent on graph
traversal (verified via an actual `graph_expanded_ids` check, not a
structural guess). The other 54 fail because a real edge here usually
connects topically-similar notes, which ordinary ranking already finds
without the graph. **Found, while building this check, that 0 of the
EXISTING 12 graph cases ever reach gold via real traversal either** — every
prior graph on/off comparison (R016, r024, r025 WP-8) never actually
exercised the mechanism it claimed to measure. `heldout.json` 30->32 cases
(v2.1), re-frozen, `CONTRACT.md` updated with this correction.

## WP-6 — edge promotion

Branch: `r025/edge-promotion`

Attempted to promote WP-2's 45 TRUE-judged proposals (same sample, not a
fresh run) into their source notes' `relations:` via the real, unmodified
`MemoryController.update()`. **0 of 45 succeeded.** Measured via an actual
`update()` call per candidate, not simulated: 13 blocked by `update()`'s own
lifecycle gate (ADMIN/HUMAN may only touch an ACTIVE note; 46/~850 of this
corpus qualifies), 1 by the canonical schema's UUID-format `id` requirement,
2 by a note whose `created` field already deserializes as a live
`datetime.datetime` (an unquoted YAML timestamp already on disk, predating
this package), 29 by unresolvable proposal paths (at least one is not a
governed note at all). None of these four was fixed — doing so would be the
policy bypass the package's own requirement 5 forbids.
`test_wp6_promotion_call_path.py` (AST, 3 tests) proves the mechanism itself
has no bypass and is ready to run the moment any of the four is fixed
elsewhere. Zero new edges exist, so the mandatory R016 re-run on the v2.1 set
reproduces the same NO-GO, as expected — confirmed, not assumed.

## WP-10 — write-path damage

Branch: `r025/write-path-damage`

r024 WP-4 found 0/850 tracked notes in any legacy write root; this session
re-measured rather than assumed it still holds. **It does — still 0**, and 6
of the 7 legacy target directories don't even exist on disk. This means the
defect is untriggered, not contained: no `propose()` call has ever run
against the real, file-backed `MemoryController` (the test suite only
exercises the in-memory fixture). The next real one will still land
invisibly. Restated WP-4's three fix options with today's costs — option B
(redirect future writes) now has a measured zero-note migration cost.
`path_resolver.py` untouched, per this package's own restriction.

## WP-13 — README rewrite

Branch: `r025/readme-rewrite`

Replaced a 673-line README describing a repository layout
(`cognitive_core/` as a full module set, `09_COORDINATION/`, a top-level
`memory_controller/` implementation) that does not exist at those paths in
this checkout, with a shorter one that opens by pointing at
`VAULT_STATE.md`, names the shim/module-existence/documentation-drift traps
explicitly with their checks, states plainly that graph expansion does not
currently help retrieval, and gives every measured claim its exact source
report and exact reproduction command. Also corrected `VAULT_STATE.md`'s own
stale graph-ceiling number (33->32) and added rows for this session's
`update()`/write-path findings, since the new README points readers there
directly and pointing at a known-stale number would defeat the whole
rewrite.

## Cross-package note

WP-6 and WP-10 turned out to measure adjacent but distinct defects in the
mutation path: WP-10 is about WHERE `propose()` physically writes a new note
(a legacy folder the graph layer never scans); WP-6 is about whether
`update()` can subsequently touch an EXISTING real note at all (mostly, it
cannot). Neither package fixed the other's finding; both are recorded as
open, owner-level decisions.
