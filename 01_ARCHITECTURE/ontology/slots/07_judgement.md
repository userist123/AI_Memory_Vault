---
id: slot-07-judgement
ontology_slot: judgement
type: ontology_definition
lifecycle: ACTIVE
provenance:
  source_type: design
  source_author: ANTIGRAVITY
confidence: 1.00
status: scaffold
tags: [ontology, cognitive-architecture]
relations: []
created: 2026-09-07
---

# Ontology Slot: Judgement

## Question
How are decisions made?

## Theoretical sources
- Anderson (ACT-R conflict resolution)
- Minsky (Society of Mind)

## Validates module
- `30_SCRIPTS/prompt/compile_task_prompt.py` (verified to provide the `--infer` flag for task intent inference and reasoning)

## Candidate concepts
| concept | source_book | confidence | status | date_added | promoted_note_id | evidence | occurrences |
|---|---|---|---|---|---|---|---|
| numeric preference | laird_soar_cognitive_architecture | 0.90 | proposed | 2026-09-12 | | If the new rule creates a numeric preference, it is automatically tuned in the future by RL, so that the value function is dynamically extended through a combination of processing in a substate and chunking (Laird, Derbinsky, and Tinkerhess 2011). | 3 |
| reflection | memory_in_the_age_of_ai_agents | 0.95 | proposed | 2026-09-12 | | Few-shot prompting e.g., CoT, PALM - Self-Reflection e.g., Self-refine, CRITIC - KV compression/reuse e.g., AutoCompressor, SnapKV - Attention KV management e.g., Mixture-of-Memory - Long context processing e.g., Mamba, Memformer, MoA, | 6 |
| b-brain | minsky_society_of_mind | 0.95 | proposed | 2026-09-12 | | There is one way for a mind to watch itself and still keep track of what’s happening. Divide é the brain into two parts, A and B. Connect the A-brain’s inputs and outputs to the real world— E so it can sense what happens there. | 4 |
| default assumption | minsky_society_of_mind | 0.95 | proposed | 2026-09-12 | | represented by a frame to whose terminals are already attached, as default assignments, G the most usual solutions to that particular kind of problem. | 3 |
| false memory | why_we_forget | 0.95 | proposed | 2026-09-12 | | while learning new information can reduce errors in recall and help combat false memories. | 6 |
