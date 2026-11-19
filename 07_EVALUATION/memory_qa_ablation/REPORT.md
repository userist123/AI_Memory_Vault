# Memory QA ablation — report

Date: 2026-11-19. Preregistration: [[PREREGISTRATION]] (frozen at `777f61525`, before any answer was generated).
Answer model: Ollama `qwen2.5:7b-instruct` (digest `845dbda0…697e`), temperature 0, seed 42, `num_ctx` 8192.

## 1. Result

**Verdict (section 5): PASSES_THROUGH.** On benchmark v3, the retrieval gain from the reranker passes through to the
answers of a local model.

| 130 answerable questions, judge 1 (primary) | A: no memory | B: memory today | C: memory + reranker |
|---|---:|---:|---:|
| CORRECT | 7 | 24 | **52** |
| INCORRECT | 37 | 26 | 35 |
| ABSTAIN | 86 | 80 | 43 |
| Gold note among the five served | — | 24 | 71 |
| CORRECT when the gold note was served | — | 21 / 24 | 50 / 71 |
| Hallucinations on the 30 unanswerable questions | 3 | 1 | 0 |

| Contrast (judge 1) | Wins | Losses | Exact McNemar p |
|---|---:|---:|---:|
| **C vs B (primary)** | **29** | **1** | 5.8e-8 |
| C vs B, Romanian questions only | 7 | 0 | 0.016 |
| B vs A | 21 | 4 | 0.0009 |
| C vs A | 45 | 0 | 5.7e-14 |

Every condition of the decision rule holds: net +28 ≥ 8, p < 0.05, 1 loss ≤ 29/3, Romanian net +7 ≥ 0, inter-judge kappa
0.95 ≥ 0.8, hallucinations C − B = −1 ≤ +3. Judge 2 agrees: CORRECT A 9 / B 26 / C 55, C vs B 30 wins / 1 loss.
The 30 discordant cases involve 30 distinct gold notes, so they are not a few notes counted many times.

By class (judge 1, CORRECT B → C): direct 14 → 23 of 60, conceptual 9 → 26 of 30, multi_hop 1 → 3 of 40.
By language: English 19 → 40 of 69, Romanian 5 → 12 of 61. Judge-free check, token recall of `required_facts`:
A 0.10, B 0.23, C 0.36.

## 2. What this shows, and what it does not

- **First positive causal result for memory in this vault.** Holding the model, prompt and question fixed and changing only
  which notes are served, the reranked memory makes the model answer correctly 28 more times out of 130. The two earlier
  ablations were null because they used tasks for which the vault holds no relevant note, or trivial tasks.
- **It is a reading test (claim boundary fixed in advance).** v3 answers sit in the first ~700 characters of the gold note
  and the context carries 1500. The result is "on head-text v3, retrieval gains pass through at about 50/71 when the gold
  note is served"; it is not evidence that answers improve in general, nor about an agent that decides when to search
  (retrieval was forced: always five results in the prompt).
- **More answers, also more wrong ones.** With better context the model abstains far less (80 → 43) and INCORRECT rises
  26 → 35. The net is strongly positive, but a reranked context makes the model more willing to answer.
- **Multi-hop stays weak** (3 of 40): the reranker serves single notes; questions that need two notes are not solved.
- **Romanian lags** (12 of 61 vs 40 of 69 English), consistent with the retrieval gap measured before.
- **Judges.** Two blind LLM judges from another family than the answer model (Opus, Sonnet), each told to open only the
  packet; judge 1 reported opening only the packet, judge 2 did not report it explicitly and wrote a generic reason for
  every item (its labels were spot-checked as semantic, not keyword-based). No human calibration subset: deviation stated
  in the preregistration, open work.

## 3. Process record

Design critiqued by a read-only second-opinion agent; adopted: two phases (contexts frozen with the reranker up, answers
generated with it down), manipulation check (B 24, C 71, C equal to the wired path 160/160), neutral closed-book A, judges
outside the answer model's family, abstain guardrail, dedupe of identical B/C prompts (4 cases). Independent harness
review: GO after a label-set validation was added before scoring (`memory_qa_judge.py`, invalid or duplicate labels abort).
All 480 model calls ended with `done_reason: stop`; the longest prompt was 2918 tokens.

## 4. Files

`contexts.json` (phase 1), `answers.json` (phase 2), `judge_packet.json` + `judge_key.json` (blind packet and key),
`judge1.json`, `judge2.json`, `results.json`. Scripts: `30_SCRIPTS/evaluation/memory_qa_{contexts,answer,judge}.py`.
