# Deviations from the preregistration

## D-1 — the first run was void; its numbers are recorded here so they cannot be quoted

The preregistration says the arms "start from the same fusion pool" and reorder
its first K. The first harness did that outside the controller: it read
`candidate_trace.fused_ranking`, reordered it, took the first five ids and
scored them. That bypasses everything production does **after** the sort —
pagination and the context-pack builder — and the premise check the
preregistration required ("production's page equals the fusion top-5") came
back **93/130**, not 130/130.

In the other 37 cases the page is a strict subset of the fusion top-5, or empty
(R3-003: fusion ranks 1-3 and 5 never appear; R3-004: all five gone, page `[]`,
195 candidates unused). The first run's arms therefore "gained" by promoting
notes production would never have shown. Its figures, **void**:

| arm | recall@5 | vs production 24/130 |
|---|---|---|
| `embed_top20` | 45/130 | +21 |
| `embed_top50` | 59/130 | +35 |
| `embed_top200` | 64/130 | +40 |
| `rrf_top50` | 46/130 | +22 |

A second attempt took the pool from `search(page_size=200)` and died on the
egress token budget (`DataRouteViolation … 3073>3000`); it produced no numbers.

**What changed.** The arms now run *inside* production: the controller's sort
key (`_ranking_key_fn`, patched on the module object the controller actually
uses) ranks the first K of the fusion order by cosine and leaves the rest in
fusion order, and the real `search()` then paginates and packs as it does
today. The premise check must read 130/130 — it does.

## D-2 — why the pages were short, found by measuring rather than guessing, in three steps

Two explanations were written into earlier versions of this file and were
wrong; they are kept here because each was *plausible from the trace* and the
trace is what misled.

1. *"A lifecycle/type filter after fusion."* No: `classifier_filter_arm` was
   `hard` and excluded nothing in these cases.
2. *"The context-pack token budget; the top-ranked notes are 400-1,000 k-character
   statutes."* The notes are that large, and the trace does say
   `BUDGET_EXCEEDED` — but a five-note metadata pack is ~590 tokens against a
   3,000 budget. The label is wrong: `search()` records `BUDGET_EXCEEDED` for
   *any* page note missing from the final pack, whatever the builder's reason
   (controller.py, context_pack stage).

The actual mechanism, confirmed by calling `ContextPackBuilder.build()` directly
on R3-003's five page notes: `items_rejected_unverified: 4`. The builder's
`_verify_and_reduce()` rejects any result with **no provenance mapping and no
`source_ref`**, because the egress contract requires provenance on every
model-facing result and the builder never fabricates it. The four `legal_source`
notes have `provenance: null`. They pass the agent's lifecycle floor (REVIEW),
rank first on BM25 and entity overlap, take the page's slots, and are rejected
at egress — with nothing backfilled.

Measured on the corpus: **51 notes have no provenance mapping; 27 of them pass
the agent floor** (26 REVIEW, 1 ACTIVE; `legal_source`, `legal_index`,
`legal_atomic_obligation`, `core`, `index`): the 8 legal acts, their 8 indexes,
10 atomic obligations and one MOC. Those are the notes that filled and emptied
the pages. They are **not** the benchmark's unreachable gold: an earlier version
of this paragraph said 16 cases' gold notes were among them, and that was wrong.
Checked note by note on 2026-10-10, the 16 cases whose gold can never be shown
to an agent fail the **lifecycle floor**, not provenance — 12 point at `core`
governance documents with no `lifecycle` at all (`Rules.md`, `Memory_Protocol.md`,
`Confidence_Model.md`, `No_Fabrication_Policy.md`, `Identity.md`,
`System_Architecture.md`, `Goals.md`, the multi-agent execution protocol), 3 at
`NORMALIZED` knowledge notes and 1 at a lowercase `raw` one. **9 more cases' gold
ids are `path:` files without frontmatter**, present in the index but absent from
the storage pool production searches. The agent ceiling on this benchmark is
therefore at most 105/130, not 130 — and lifting it is a governance decision
(which lifecycle the core documents should carry), not a provenance fix.

**Fixed, on branch `claude/pack-size-gate-backfill`** (`846b4c04a`): eligibility
is settled before the page is cut, with the true reason recorded
(`PROVENANCE_MISSING`, `UNVERIFIED_AT_EGRESS`), and a bounded backfill follows a
genuine budget drop. Measured at the agent point after the fix: **recall 26/130
(from 24), short pages 4 (from 41), empty pages 1 (from 22)**, 7.0 ineligible
notes removed per query on average, 0 backfill rounds needed. The small recall
gain is honest: full pages help only when the gold note is showable and just
below the cut; most misses are elsewhere.

**Still open, separately.** The eight 100 k-1 M-character notes do pollute the
fusion top ranks (they match almost any query); with provenance they would now
be shown and would dominate pages. The 27 floor-passing provenance-less notes
get a derived provenance on branch `claude/legal-provenance-derivation`
(`30_SCRIPTS/knowledge/derive_legal_provenance.py`: `official` with the declared
publication URL for the acts, `ai` pointing at the parent act for indexes and
obligations, `user` for the owner-committed MOC; nothing invented, dry-run by
default). The remaining 24 provenance-less notes have no lifecycle and never
reach egress. The giant-note problem is not decided here.

## D-3 — what this means for the arms

The arms run inside production, so they already pay the egress rule; their
pages can only contain showable notes. But the reachable set the
preregistration defines — "gold in the pool below rank 5" — includes the 16
never-showable gold notes. The report therefore states reachability twice: on
the fusion pool as preregistered, and restricted to showable gold, which is the
number a reranker can actually move. This is a reporting addition, not a change
to the decision rule.
