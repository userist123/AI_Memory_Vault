# WP-6 — edge promotion blocked at 0/45 by four pre-existing, compounding defects in `update()`

package: WP-6 | intent: implement (conditional on WP-2's GO, already granted in r024) | status: DONE,
result is a blocking finding, not a promotion
baseline: 45 proposals judged TRUE in WP-2's own sample (81.8% precision, GO at ≥70%)
result: **0 promoted** through the real, unmodified `MemoryController.update()` path | n: 45 attempted
decision: do not bypass any gate to force a write through — report the block, implement the mechanism
correctly so it is ready the moment the underlying defects are fixed elsewhere.

## What was measured

`promote.py` calls `controller.update()` — the real, unmodified production path — exactly once per
promotable source note, for all 45 of WP-2's `judgement == "correct"` items from
`review_worksheet.json` (requirement 1: same sample; requirement 2: only the 45, not the unsampled
remainder). **Every one of the 45 failed.** Failure taxonomy, from the actual `update()` call (not a
static guess):

| blocking cause | count | first found on |
|---|---:|---|
| `source_path`/`target_path` does not resolve to a real note id | 29 | review #37 (`02_PRODUCT/ORIGINAL_REQUEST.md` has no YAML frontmatter at all — not a governed note) |
| `update()`'s own lifecycle gate: ADMIN/HUMAN may only update an ACTIVE note; AI_AGENT may additionally update RAW/CLASSIFIED/NORMALIZED — REVIEW is permitted for no principal | 13 | review #40 (source lifecycle REVIEW) |
| `id`/`target_id` fails the canonical schema's `{"format": "uuid"}` requirement | 1 | review #39 (`proc-brain-arch-0001`) |
| a field on the note ALREADY deserializes as a live `datetime.date`/`datetime.datetime` object (an unquoted YAML timestamp already on disk) — fails schema's `{"type": "string"}` before this package changes anything | 2 | review #38 |

Full per-item detail: `phase_a_promotability_report.json` (id resolution + UUID-format check),
`phase_a2_report.json` (adds the lifecycle-gate + full schema-validation simulation, in-memory, no
writes), `promotion_ledger.json` and `promotion_run_report.json` (the actual `update()` attempt —
these numbers are the ones reported above, not the simulation's, though they agree).

## Why 0, not a smaller number: four independent, pre-existing defects, none introduced here

1. **The lifecycle gate.** `controller.py`'s `update()`:
   ```python
   if note['lifecycle'] != Lifecycle.ACTIVE:
       if principal == Principal.AI_AGENT and note['lifecycle'] in {RAW, CLASSIFIED, NORMALIZED}:
           pass
       else:
           raise ValueError('Updates not permitted for this lifecycle and principal')
   ```
   REVIEW is in neither allowed set, for either principal. Per this vault's own corpus census (r025
   WP-9's Phase A): only 46 of ~850 real notes are ACTIVE; the corpus is overwhelmingly REVIEW (661).
   **`update()` is therefore unusable by ADMIN/HUMAN for the large majority of this vault's real
   content, independent of anything about relations or graph edges.**
2. **The canonical schema's `id`/`target_id` UUID-format requirement**
   (`03_IMPLEMENTATION/packages/lifecycle/validation/schema.py`). Confirmed directly:
   `controller.update()` on a real, existing, ACTIVE note whose `id` is a slug
   (`proc-enterprise-integration-0001`) raises `ValidationError` on the `id` field alone. This vault's
   real corpus mixes UUID ids with slug ids (`knw-*`, `leg-*`, `cat-*`, `proc-*` — seen extensively in
   r025 WP-9 and WP-12's own work this session); the schema silently assumes only the former exist.
3. **Already-corrupt date fields.** At least one real note's `created` field deserializes as a live
   `datetime.datetime` object, not a string — an unquoted YAML timestamp already on disk, predating
   this package, that the schema's `{"type": "string"}` check then rejects. This is a LIVE instance of
   exactly the failure mode this package's own requirement 6 ("quote YAML dates") warns about — not
   hypothetical, and not something this package's own writes caused (this package never touches
   `created`, only `relations`).
4. **Some proposal endpoints in WP-2's own sample are not governed notes at all.** `02_PRODUCT/
   ORIGINAL_REQUEST.md` (source of review #37, target of review #42) has no YAML frontmatter — it is
   a plain project-brief markdown file. `edge_proposer.py`'s candidate pool apparently draws from
   `VaultIndex`'s broader path-based indexing, which includes files `FileStorageEngine`/`update()`
   cannot treat as notes at all.

**None of these four is this package's own defect, and none is fixed here.** Loosening the lifecycle
gate, relaxing the schema's UUID format, or coercing corrupt date fields to force a write through
would each BE the policy bypass requirement 5 forbids — the AST proof below establishes there is no
such bypass in the promotion code itself; it does not, and should not, extend to rewriting the policy
`promote.py` calls into. Fixing any of the four is a vault-wide, security/schema-relevant decision
with its own blast radius, squarely out of scope for an edge-promotion package.

## Requirements checklist

1. **Same sample, not a fresh run**: `promote.py` loads `07_EVALUATION/r024_wp2_precision/
   review_worksheet.json` directly — the exact file WP-2 wrote — and never calls `edge_proposer.py`
   again.
2. **Only the 45 judged TRUE**: enforced with an `assert len(correct) == 45` guard before anything
   else runs.
3. **No hub linking**: moot at n=0 promoted, but confirmed by design — `promote.py` never adds an
   edge beyond exactly what each of the 45 items already proposed; nothing was done to compensate for
   the corpus's unconnected notes (per this requirement, this is reported, not "fixed": most of this
   vault's notes remain outside any graph edge, exactly as before this package ran).
4. **Provenance per edge**: recorded in `promotion_ledger.json` for all 45 attempts (promoted or not)
   — proposer (`edge_proposer.py`), score (`confidence`), shared entities (`evidence_entities`),
   approval (WP-2's `judgement`/`judgement_reason`), sample seed (`8675309`). Not written into the
   relations entry itself: the canonical schema's `relations` items have `additionalProperties: False`
   allowing only `relation`/`target`/`target_id`, so adding provenance keys there would itself be a
   schema violation this package does not commit.
5. **No policy bypass, proven by AST**: `20_TESTS/regression/test_wp6_promotion_call_path.py` (3
   tests, passing) parses `promote_one()`'s source and asserts it calls exactly one
   `controller.update(...)`, touches `.storage` only via the read-only `.get()`, and contains no
   `open()` call — the only way a note is ever written is through the real, unmodified
   `MemoryController.update()` -> `_validate_note()` -> lifecycle-gate/schema path.
6. **Quote YAML dates**: this package's own single new date field
   (`promotion_ledger.json`'s `promoted_at`) is always written via `.date().isoformat()`, never a raw
   `datetime` object — moot for the note frontmatter itself since 0 writes happened, but see finding 3
   above: this exact failure mode already exists elsewhere in the vault, unrelated to this package.

## Mandatory: R016 re-run on the v2.1 set (WP-12's 32-case heldout)

Zero new edges exist, so this is not expected to move the graph-arm comparison — confirmed, not
assumed, by actually re-running `run_production_arms.py` against the current `heldout.json` (32
cases, 12 `one_hop_graph_expansion` after WP-12's addition of `H31`/`H32`).

| arm | n (graph class) | candidate_recall | context_recall | answer_correctness |
|---|---:|---:|---:|---:|
| graph_off | 12 | 0.6667 | 0.1667 (2/12) | 0.1667 |
| graph_on (strict, 7 errored out) | 10 | 1.0 | 0.30 (3/10) | 0.30 |

McNemar (paired by id, 3 unanswerable skipped): `candidate_recall` off_only=0/on_only=2;
`context_recall` and `answer_correctness` both off_only=2/on_only=2 (net zero — 2 cases better, 2
worse). The 7 `graph_on` errors are `GraphExpansionDegraded` ("expanded no nodes") on `H02, H04, H06,
H14, H15, H22, H24` — exactly the cases r025 WP-9's Phase A identified as classifier-inferred-filter
collapses (`lifecycle_filters=['VERIFIED']`/`['CLASSIFIED']`). `run_production_arms.py`'s `build()`
does not pass `classifier_filter_arm`, so it resolves to the production default (`HARD`, i.e.
unchanged) — this re-run does not exercise WP-9's fix, matching R016's own deliberate pinning
philosophy (measure graph expansion holding everything else at its production default).

**Graph expansion's NO-GO conclusion is unchanged**, on a re-run against a genuinely updated
(v2.1) benchmark set, with genuinely zero new edges. This does not newly confirm anything about the
GRAPH — see r025 WP-12's own finding that 0/12 of these cases (old or new) ever reach gold via actual
`graph_expanded_ids` traversal, so this comparison, here as before, is not actually testing graph
traversal for a single one of these 12 cases.

## A correction, found while writing this report

The historical `R016_RESULT.md` (pre-r024) states the graph-case ceiling as "~33 disjoint ones" — this
is very likely the origin of this session's own brief citing 33. r025 WP-12 re-measured this fresh and
found the verified, current figure is **32** (`CONTRACT.md`'s own number, confirmed unchanged from an
exact 278-edge match). `R016_RESULT.md` is left untouched here (a historical snapshot, not something
this package's scope covers); the correction is recorded in `CONTRACT.md`'s own v2.1 section and
repeated here for continuity.

## What remains open

- **This vault's write path (`update()`) is effectively non-functional for the large majority of real
  content right now** — not a WP-6-specific problem, but a systemic one this package's own attempt
  surfaced with an exact, reproducible taxonomy. This overlaps with, but is distinct from, r025
  WP-10's own scope (`propose()`-path damage, notes invisible to the index) — WP-10 should treat this
  report as adjacent context, not a substitute for its own separate measurement.
- Fixing any of the four blocking causes (lifecycle gate, id/target_id UUID format, corrupt date
  fields, non-note proposal endpoints) is explicitly not attempted here and would need its own
  package, with its own review of blast radius across every other caller of `update()`/
  `validate_frontmatter()`.
- If any of those four are fixed later, `promote.py` and its ledger format are ready to re-run
  unmodified against the same 45-item sample — no proposal re-sampling would be needed, per
  requirement 1.
- The 16/45 items excluded from `phase_a_promotability_report.json`'s original UUID-only check but
  newly counted under "path unresolved" here (29 total) were not individually root-caused beyond
  confirming at least one (`ORIGINAL_REQUEST.md`) is a non-note file; the remainder likely split across
  the same causes already documented for WP-9/WP-12 (duplicate paths across content-root vs
  legacy-write-root trees, stale worksheet paths) but this was not exhaustively attributed per item.
