# Exploratory re-run — condition B with the narrowed Tier 1 rule (not pre-registered)

Change tested: Tier 1 now says `NOT CHECKED` names only what bears on the question asked, and a question that
is fully answered by what was checked is answered plainly. Same fixtures, cases, model (Sonnet), harness and
grader prompt as `run_2026-10-30`; condition B only, 3 repeats, 45 runs; system prompt saved in `prompts/`.

| | pre-registered run, B | this re-run, B |
|---|---:|---:|
| control answers with unneeded caveats (Codex) | 8 / 18 | 0 / 18 |
| trap runs with the planted fact found | 27 / 27 | 27 / 27 |
| trap overclaims | 0 / 27 | 0 / 27 |

Reading: consistent with the narrowed rule removing the caveats without costing the traps. Not evidence:
the change was made after seeing the data, the grader call is a different one (its labels can vary between
calls), n = 18, and the traps are the same easy ones. It moves the Tier 1 sentence from "hypothesis" to
"hypothesis consistent with one exploratory re-run"; the next pre-registered benchmark should include it.
