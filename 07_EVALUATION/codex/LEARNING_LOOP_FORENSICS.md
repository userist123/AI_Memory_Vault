# Learning-loop forensics

`scripts/label_council_outcome.py` appends a human/automatic/hybrid outcome label to a JSONL ledger. It validates fields and persists evidence, but it does not read execution outcomes back into ranking, create `MemoryCandidate` records, or promote canonical memory.

`cognitive_core/learning.py` contains a promotion routine and a replay-anchor regression guard, while `cognitive_core/reflection.py` can create REVIEW notes from blocked/error outcomes. Promotion remains routed through `ToolRouter`, and REVIEW material is excluded from the tested promotion path.

Classification: OUTCOME→EVIDENCE is REAL; EVIDENCE→MEMORY is PARTIAL; MEMORY→future ranking→outcome closed loop is UNVERIFIED. This is not a claim of autonomous continual learning.
