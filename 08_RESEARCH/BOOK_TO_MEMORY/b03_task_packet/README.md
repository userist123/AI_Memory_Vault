# B03 task packet (pre-registered; contains prompts only, no answers)

Study: `08_RESEARCH/BOOK_TO_MEMORY/PREREGISTRATION_B03_B05.md`. Nothing in this directory has been run.

## What to do (orchestrator)

1. Read `manifest.json` (`execution_rules`) and `run_config.template.json`.
2. For every entry of `trials.json`, in `order_index` order, send `prompt` verbatim to ONE model, in a fresh context,
   under the run config of the template (temperature, seed, max_tokens are fixed; you set only `model_id`).
3. Write the raw answer, unedited, to `answer_path` (relative to this directory), e.g. `answers/B03-T01-WITH_NOTE.txt`.
   A trial that failed gets an empty file; do not retry it to get a better answer.
4. Save the run config you used, with `model_id` filled in, as `run_config.json` next to `answers/`.
5. Do NOT open `scoring/`: it holds the reference material the raters score against.

A second model is a second run: copy this directory, give it its own `answers/` and `run_config.json`.

## Afterwards

* Build the blind rating packet: `python 30_SCRIPTS/evaluation/b2m_blind_rating_packet.py build --b03-packet <this dir> --out <packet> --key <key.json>`
* Score it with at least two independent raters, then `... score --ratings ... --key <key.json> --out scores.json`
* Analyse: `python 30_SCRIPTS/evaluation/analyze_b03_packet.py --packet <this dir> --scores scores.json`
  With too few complete pairs the analysis prints `INSUFFICIENT_DATA` and reports no effect.

## Files

| File | Content |
|---|---|
| `manifest.json` | counts, hashes, the run-config template, the execution rules |
| `trials.json` | one entry per trial: `trial_id`, `condition`, `prompt`, `answer_path`, `order_index` |
| `run_config.template.json` | the run config every run must use (except `model_id`) |
| `scoring/tasks.json` | per task: question, note id, definition, source passage (for the raters only) |
| `answers/` | empty; the orchestrator writes `<trial_id>.txt` here |
