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
| misinformation effect | openstax_psychology_2e_ch08 | 0.95 | promoted | 2026-10-04 | afb5de0a-9538-42cc-ab49-618f890e8867 | Loftus also developed the misinformation effect paradigm, which holds that after exposure to additional and possibly inaccurate information, a person may misremember the original event. | 1 | 
| numeric preference | laird_soar_cognitive_architecture | 0.90 | promoted | 2026-09-12 | 7e93f968-16e9-4d3a-bde3-cf8e417dfc9d | If the new rule creates a numeric preference, it is automatically tuned in the future by RL, so that the value function is dynamically extended through a combination of processing in a substate and chunking (Laird, Derbinsky, and Tinkerhess 2011). | 3 | 
| reflection | memory_in_the_age_of_ai_agents | 0.95 | promoted | 2026-09-12 | ab693c77-2325-48a2-ae99-c869dc72782a | Few-shot prompting e.g., CoT, PALM - Self-Reflection e.g., Self-refine, CRITIC - KV compression/reuse e.g., AutoCompressor, SnapKV - Attention KV management e.g., Mixture-of-Memory - Long context processing e.g., Mamba, Memformer, MoA, | 6 | 
| b-brain | minsky_society_of_mind | 0.95 | promoted | 2026-09-12 | 29fe72cf-57bb-4a5e-a912-3eb8f945e05f | There is one way for a mind to watch itself and still keep track of what’s happening. Divide é the brain into two parts, A and B. Connect the A-brain’s inputs and outputs to the real world— E so it can sense what happens there. | 4 | 
| default assumption | minsky_society_of_mind | 0.95 | promoted | 2026-09-12 | bafb06ff-e437-4d23-b576-af8df6defc63 | represented by a frame to whose terminals are already attached, as default assignments, G the most usual solutions to that particular kind of problem. | 3 | 
| false memory | why_we_forget | 0.95 | promoted | 2026-09-12 | a2607d49-d1be-495b-9a28-32bfda843693 | while learning new information can reduce errors in recall and help combat false memories. | 6 | 
