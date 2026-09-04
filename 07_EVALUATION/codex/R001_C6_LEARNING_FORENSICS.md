# R001 C6 — learning loop forensics

Baseline observed before this lane: `e43cc81e09789e284ef35a7e326297194f429a9e`.

`scripts/label_council_outcome.py` appends validated outcome records to an
append-only JSONL ledger. It does not read those records back into retrieval,
ranking, or candidate generation. This is telemetry/evidence, not a closed
learning loop.

`cognitive_core/reflection.py` can create a new `REVIEW` note after an error or
blocked action. `cognitive_core/learning.py` can update eligible `ACTIVE`
notes through `ToolRouter`, based on relations/confidence/provenance. The
executed tests confirm these local mechanisms and the review gate:

```text
python -m pytest -q cognitive_core/tests/test_learning.py cognitive_core/tests/test_reflection.py cognitive_core/tests/test_tool_router_security.py
9 passed in 0.50s
```

There is no observed path in these components from an appended Council
outcome ledger record to a future retrieval ranking change. Classification:

- outcome labeling: `TEST_VERIFIED` / `CODE_VERIFIED`;
- reflection-to-REVIEW: `TEST_VERIFIED`;
- gated learning update: `TEST_VERIFIED`;
- outcome → evidence → memory → future retrieval closed loop: `UNVERIFIED`;
- autonomous self-improvement: `NOT_IMPLEMENTED` in the inspected path.
