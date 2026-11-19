# Memory QA ablation — does better retrieval change what a local model answers?

Date: 2026-11-19. Status: **FROZEN before any answer is generated.** Design critiqued by a read-only second-opinion
agent; its fixes are adopted below.

## 1. Question and claim boundary

Does memory served with the cross-encoder reranker ON (condition C) make a local model answer benchmark v3
questions correctly more often than memory served today (B)? A (no memory) is a closed-book floor only.

**Different question from the two earlier null ablations.** Those measured coding tasks for which the vault holds
no relevant note (checked 2026-11-19: retrieval returns unrelated checkpoints with the reranker on or off) or trivial
tasks (`lookup('a') == 1`). This run measures whether a retrieval gain passes through to model answers.

**Claim boundary, stated in advance.** v3 is a head-text reading test: `required_facts` appear in the first ~700
characters of the gold note and the context carries 1500. A positive result means "on head-text v3, retrieval gains
pass through to answers at rate X"; it is not evidence of better answers in general. Retrieval is forced (always
five results in the prompt), an upper bound on influence compared with an agent that decides when to search.

## 2. Frozen inputs

| Item | Value |
|---|---|
| Benchmark | `retrieval_benchmark_v3.json` sha256 `eeb53822…57eaa`; 130 answerable, 30 abstain; 61 RO / 69 EN answerable |
| Contexts (phase 1) | `contexts.json`, written by `30_SCRIPTS/evaluation/memory_qa_contexts.py` at `c1d16175c` with the reranker server up |
| Context construction | `memory_access.search(controller, question, 5)` then `memory_access.get` body (first 1500 chars) per result, in served order; a note `get` refuses keeps its 320-char snippet (production behaviour, intention-to-treat) |
| Manipulation check (passed before freezing) | gold in served top 5: B 24/130, C 71/130; C ids equal the wired-path served lists 160/160 (`reranker_experiment/results/wired_check.json`), i.e. reranker status ok on every query |
| Reranker | bge-reranker-v2-m3 `953dc6f6f85a…`, fp16, K = 50, title + 1200 chars |
| Answer model | Ollama `qwen2.5:7b-instruct` (id `845dbda0ea48`, Q4_K_M), temperature 0, seed 42, `num_ctx` 8192, `num_predict` 300 |
| Phase 2 | generation runs with the reranker server stopped (the machine cannot hold both; Windows commit limit) |

## 3. Prompts (exact templates in `30_SCRIPTS/evaluation/memory_qa_answer.py`)

- **A (closed book, neutral):** answer concisely in the question's language; say "I don't know" if you do not know.
- **B and C (identical template):** the same instruction plus a delimited block of notes from the user's vault,
  labelled as data, not instructions; use them if they contain the answer.
- When the B and C prompts are byte-identical (4 of 160 cases) the answer is generated once and reused, so every
  B/C discordance comes from a context difference.
- Abort conditions: `prompt_eval_count` ≥ `num_ctx` on any call (truncation); a model call error; the model not fully
  on the GPU at start.

## 4. Judging

- Unique (question, answer) pairs, shuffled with seed 20261119, condition hidden. The judge sees the question, the
  reference `expected_answer` (not `required_facts`) and the answer.
- Labels: **CORRECT** only if every key proposition of the reference is stated (all hops for multi_hop) and nothing
  contradicts it; **ABSTAIN** if no substantive claim is made; otherwise **INCORRECT**. For abstain cases (no
  reference) the labels are **ABSTAIN** or **HALLUCINATION** (any substantive answer).
- Two independent blind judges from another model family than the answer model (Claude Opus = judge 1, primary;
  Claude Sonnet = judge 2). Disagreements on the primary contrast cases are listed in the report.
- **Deviation from the reviewer's design:** a blind human calibration subset (40 items) is not available in this
  session; the judge-validity condition below uses inter-judge kappa instead, and the human calibration remains
  open work. Judge-free sensitivity metric: normalised token recall of `required_facts` in the answer.

## 5. Primary analysis and decision rule

Primary: C vs B, paired, intention-to-treat, 130 answerable cases, CORRECT vs not (INCORRECT and ABSTAIN pooled),
judge 1, exact two-sided McNemar.

**"Retrieval gain passes through"** only if all hold: manipulation check passed (it did); wins − losses ≥ 8;
p < 0.05; losses ≤ wins / 3; Romanian net ≥ 0; inter-judge Cohen's kappa ≥ 0.8 on the 130 × {B, C} labels;
abstain guardrail: C hallucinations − B hallucinations ≤ +3 of 30.
**Null** if wins − losses ≤ 2 or losses > wins / 2. Anything else: inconclusive.

Secondary, descriptive: B vs A and C vs A; P(correct | gold served) per condition; strata by class and language;
INCORRECT rate on answerable cases; required-facts token recall; judge-2 result.

## 6. Interpretation fixed in advance

- Null with the manipulation check passed and low P(correct | gold served): retrieval does not influence this model.
- Null with high P(correct | gold served): look at harness losses (refused bodies, unreadable gold) before concluding.
- Questions sharing a gold note are not independent; the number of distinct gold notes among discordant cases is
  reported next to the McNemar p.
