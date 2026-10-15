# todo-claude-retrieval-ceiling
STATUS: IN_PROGRESS        UPDATED: 2026-10-10T00:00:00Z (see git log for the exact time)
TASK: raise what memory_search can actually show an agent on benchmark v3 (pages, provenance, reranker)
BRANCH / PR: claude/legal-provenance-derivation / none yet    BASE: 21da5bbf2 (origin/main)
SPEC: 07_EVALUATION/reranker_envelope/DEVIATIONS.md (D-1..D-3, on claude/ranking-formula-experiment),
      07_EVALUATION/reranker_envelope/REPORT.md, 00_GOVERNANCE/VAULT_STATE.md (retrieval bullets)

DONE:
- PR #254 claude/ranking-formula-experiment: ranking arms, reranker envelope (arms inside production),
  tokenizer fix, state card. Reranker: production 24/130; embed_top20 45, top50 59, top200 64 (+41/-1),
  all within the latency budget; held-out v2 one run: 2/29 -> 6/29, p=0.22. K choice is the owner's.
- PR #255 gateway. PR #256 claude/pack-size-gate-backfill: egress eligibility settled before the page is
  cut + bounded backfill. Agent point: empty pages 22 -> 1, short 41 -> 4, recall 24 -> 26. CI green.
- This branch: 30_SCRIPTS/knowledge/derive_legal_provenance.py (dry-run default, --apply) gave the 27
  floor-passing provenance-less notes a schema-valid provenance (8 acts official, 18 derived notes ai ->
  parent act URL, 1 MOC user). 20_TESTS/test_legal_notes_carry_provenance.py guards it. 77 tests pass.
  Measured at the agent point on this branch alone (without #256): recall 24/130 (unchanged), short pages
  41 -> 4, empty 22 -> 1; the legal notes now take 149 of 639 page slots on 37 of 130 pages
  (07_EVALUATION/reranker_envelope/agent_point_prov.json). Egress is now correct; item 4 became urgent.
- Corrected a wrong claim (DEVIATIONS D-2, state card): the 16 unreachable benchmark cases fail the
  lifecycle floor (12 core governance docs with no lifecycle, 3 NORMALIZED, 1 raw), not provenance.

NEXT (in order):
1. Merge order: #256 first, this branch second (independent files).
2. Owner decision: reranker K (rule says embed_top20; top200 is twice the gain at the same cost).
   Then wire the chosen arm into search() with tests (separate PR).
3. Owner decision: lifecycle for the core governance documents (Rules, Memory_Protocol, Identity,
   System_Architecture, Goals, protocols). Until set, 12 benchmark cases are unreachable by design.
4. Giant notes (8 x 100k-1M chars) dominate fusion ranks and, now showable, pages: chunk or cap.
5. Benchmark defect: 9 cases' gold ids are path: files without frontmatter (not in the storage pool).

BLOCKERS: none (items 2 and 3 wait on the owner).
KEY FILES: 03_IMPLEMENTATION/packages/memory/controller.py (search, egress_ineligibility),
  03_IMPLEMENTATION/packages/retrieval/context/pack_builder.py (_verify_and_reduce),
  30_SCRIPTS/evaluation/run_reranker_envelope.py, 30_SCRIPTS/knowledge/derive_legal_provenance.py
