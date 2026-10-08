# PREREGISTRATION_B03_B05.md — real-model with/without-note ablation, blind multi-rater scoring

**Status**: PRE-REGISTERED, WRITTEN BEFORE ANY DATA EXISTS. No model has been run on any trial of this study, no
answer exists, no rater has seen anything. The commit that introduces this file precedes the commit that
introduces the task packet; the history shows the order. Findings **B03** (no ablation against real models) and
**B05** (single rater) of the PR #209 audit are what this document sets up; neither is closed until the owner (or
an orchestrator the owner runs) executes the packet and the analysis below is run on the real answers.

**Date**: 2026-10-08. **Branch**: `research/b2m-blockers-eval`.

## 1. Question

Does giving a model a Book-to-Memory candidate note (lifecycle `REVIEW`, verification `unverified`) in its prompt
change the quality of its answer to a question about the note's concept, compared with the same question asked
without the note, when the answers are scored blind by independent raters?

This is a question about the **notes of the track in aggregate**. It is not the per-note promotion gate of
`POLICY-LEARNING-QUALITY-02` section 7 (at least 2 models and 3 repetitions *per note*), which this study does not
evaluate and does not claim to satisfy.

## 2. Hypotheses

Let `d_i = score(WITH_NOTE, task i) - score(WITHOUT_NOTE, task i)`, scores on the 0-10 rubric of section 5.

* **H0 (primary)**: the mean of `d_i` is 0.
* **H1 (primary)**: the mean of `d_i` is not 0 (two-sided). A positive mean is the hypothesis of interest; a
  negative mean (the note makes answers worse) is a reportable result, not an error.

Secondary, exploratory, Holm-corrected together (three tests), run only if the primary data are complete:

* **S1 framing**: `WITH_NOTE_SKEPTIC` (the note is introduced as unchecked and possibly wrong) versus `WITHOUT_NOTE`.
  Does the effect survive a reversed framing?
* **S2 decoy**: `WITH_DECOY_NOTE` (an unrelated note from another source is shown) versus `WITHOUT_NOTE`. Is some of
  the effect due to the mere presence of a reference block?
* **S3 framing contrast**: `WITH_NOTE` versus `WITH_NOTE_SKEPTIC`. Is the result sensitive to how the note is introduced?

## 3. Population and selection rule (fixed here, applied mechanically)

The notes the track concerns are the candidate notes it produced: the **51** `Promoted_*` notes added by commit
`ce724a575` ("feat(knowledge): add 51 Promoted_* concept notes as REVIEW/unverified candidates", merged with PR #221),
the book-derived concept batch of the Book-to-Memory track. Rule:

1. Take the files added by that commit under `01_ARCHITECTURE/knowledge/` whose name starts with `Promoted_`.
2. Keep a note only if its frontmatter has `lifecycle: REVIEW` and `verification: unverified`, and its body has a
   `# <term>` heading, a `## Canonical Definition` section and a block-quoted source passage (`> "..."`).
3. **Every** note that passes is used. There is no sampling and no cherry-picking: the unit count is whatever passes
   (51 at the time of writing). The 43 other `Promoted_*` notes in the directory predate the track's notes batch and
   are not used. If a note fails rule 2, it is dropped and listed in the packet manifest with the reason.

The selection is performed by `generate_b03_task_packet.py` and the resulting list is recorded in
`b03_task_packet/scoring/tasks.json`.

## 4. Design

* **Paired within task.** Each task is run in four conditions: `WITHOUT_NOTE` (baseline), `WITH_NOTE` (neutral
  framing, primary), `WITH_NOTE_SKEPTIC` (reversed framing), `WITH_DECOY_NOTE` (control). 51 tasks give **51 primary
  pairs** and 204 trials.
* **Task text (fixed template).** `Explain the term "<term>"<source clause> and the role it plays. Answer in English
  in 3 to 6 sentences.` The source clause names the source only for sources with a human-readable title in the
  generator's table; opaque identifiers (for example `wcs_1488`) get no clause.
* **Note text shown.** Only the note's title (the term), its `Canonical Definition` paragraph and its block-quoted
  source passage. The frontmatter, the "Grounding & Source Context" boilerplate (which states a confidence) and the
  "Architectural Role" and "Invariants" boilerplate are never shown, because they carry claims about the note's own
  reliability that would bias the comparison.
* **Decoy.** For task `i` the decoy is the next note in id order (cyclic) with a different source and a different
  term.
* **Isolation.** One fresh context per trial; the `WITHOUT_NOTE` prompt contains none of the note's definition, passage
  or id (checked by the generator). Execution order is a seeded shuffle of all 204 trials (seed `20261008`), so
  conditions are interleaved.
* **Run config (B08).** One model per run. Fixed for every trial of a run: `temperature = 0.0`, `seed = 20261008`
  (where the API supports one), `max_tokens = 400`, and the prompt-template-set hash recorded in the manifest.
  `model_id` is filled by the orchestrator and must be identical across all trials of a run. A second model is a
  second run of the same packet; runs of different models are compared only through the comparability guard with
  `model_id` as the variable under test.
* **Raw data kept.** Every prompt and raw answer is stored as a transcript; nothing is edited after the fact.

## 5. Outcome (primary metric) and raters (B05)

* **Primary metric**: the `POLICY-LEARNING-QUALITY-02` section 6 rubric total, five dimensions scored 0-2
  (`corectitudine`, `completitudine`, `fara_ghicit`, `fara_surse_externe`, `reproductibilitate`), total 0-10, scored against
  the note's source passage and definition (`scoring/tasks.json`).
* **Raters**: at least 2 independent raters per answer, every rating blind to the condition and to the other raters.
  A rater is identified by `rater_id` and, for a model, its model id. At least one rater must come from a different
  model family than the model that produced the answers. Ratings are collected with the schema and blind packet of
  `book_to_memory_raters.py` / `b2m_blind_rating_packet.py`.
* **Per-trial score** = the mean of its raters' totals.
* **Reliability gate**: Krippendorff's alpha (interval) on the rubric totals must be at least 0.667. Below it the
  primary result is still reported, but it cannot be called supported (section 7). Fleiss' kappa (on the five-level
  `PASS`/`RETRY`/`FAIL` bands) and, for two raters, Cohen's kappa are reported alongside.
* **Secondary, automatic, exploratory**: coverage of the content words of the source passage in the answer
  (no rater needed). It is reported but never replaces the primary metric.

## 6. Analysis plan

1. Pair answers by task. Drop a pair only if one of its two answers is missing or empty-file, or a score is missing
   (an answer that is a refusal or a wrong answer is an answer: it is scored, not dropped). Report the number dropped.
2. **Primary test**: paired t-test on `d_i`, two-sided, alpha = 0.05; 95% t confidence interval of the mean difference;
   effect size `d_z = mean(d) / sd(d)`.
3. **Sensitivity (reported next to the primary)**: sign-flip permutation test (exact for up to 20 pairs, otherwise
   100000 seeded draws, seed `20261008`) and a 10000-draw seeded percentile bootstrap CI of the mean.
4. Secondary tests use the same statistics and are Holm-adjusted across S1-S3.
5. No outcome-dependent change to the metric, the rubric, the exclusion rule or the test is allowed. Anything that has
   to change is appended as a dated entry under "Deviations" below, with the reason, before the data are looked at again.

## 7. Sample size and decision rules

* **n = 51 primary pairs.** For a paired t-test at alpha = 0.05 (two-sided), n = 51 gives about 80% power for
  `d_z = 0.4` and above 95% power for `d_z = 0.6`. Smaller effects are not claimed to be detectable.
* **INSUFFICIENT_DATA** when fewer than **40** complete primary pairs remain after section 6.1. The harness then
  reports no effect at all (no mean, no p-value, no interval).
* **Supported** only if all of: (a) two-sided p < 0.05; (b) the lower bound of the 95% CI is above 0;
  (c) the mean difference is at least **+1.0 rubric point**; (d) Krippendorff's alpha >= 0.667.
* **No effect of practical size demonstrated** if the whole 95% CI lies inside (-1.0, +1.0).
* **Inconclusive** otherwise. A negative result with CI below 0 is reported as "the note made answers worse".

## 8. Known limitations (stated before the data)

* Definition questions are partly answerable from the model's prior knowledge. A ceiling effect for well-known terms
  (`Markov chain`, `habituation`) will shrink the measurable difference.
* The note text is Romanian; the question and the answers are English. A cross-language effect is part of what is measured.
* The rubric reference is the note's own source passage, which favours the `WITH` conditions on "source-specific"
  content. The decoy condition (S2) and the framing contrast (S3) exist to bound this, not to remove it.
* One model per run, one run per model: the study does not establish that the effect carries across models, and it says
  nothing about notes that are not definitions or about production load (B10).
* If the raters share a provider with the answering model, independence is weaker than the schema can show.

## 9. Artefacts

| Artefact | Path |
|---|---|
| Task packet generator | `30_SCRIPTS/evaluation/generate_b03_task_packet.py` |
| Task packet (prompts, answer paths, no answers) | `08_RESEARCH/BOOK_TO_MEMORY/b03_task_packet/` |
| Harness (runner, paired statistics, `INSUFFICIENT_DATA`) | `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_real_ablation.py` |
| Analysis CLI | `30_SCRIPTS/evaluation/analyze_b03_packet.py` |
| Rating schema, agreement statistics | `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_raters.py` |
| Blind-rating packet generator (CLI) | `30_SCRIPTS/evaluation/b2m_blind_rating_packet.py` |

## Deviations

*(none from the design above; no data exist yet)*

### Clarifications recorded before any data (2026-10-08, after the packet was generated, before any model was run)

* Several source passages in the notes are noisy text extractions (hyphenation, page furniture, fragments cut mid-sentence). The
  design is unchanged: the passage is shown as the note contains it and raters score against the passage and the definition
  together. This adds noise to every condition that shows the note and none to `WITHOUT_NOTE`; it is a limitation, not a change.
* `WITH_DECOY_NOTE` uses the same prompt template as `WITH_NOTE` (only the shown note differs), so the S2 contrast is not
  confounded by wording.
* The packet generator, harness and analysis CLI named in section 9 exist as listed; the analysis is `analyze_b03_packet.py`
  with `--scores` (blind ratings) for the primary analysis, and `--auto-score` only for the exploratory coverage metric.
