# Interpretation — run 2026-10-30

Pre-registered decision (`RESULTS.md`): **benchmark not discriminating.** Without the protocol, Sonnet
overclaimed in 0 of 27 trap runs (Wilson 95%: 0.00–0.12); with it, also 0 of 27. Every trap's planted fact
was found in both conditions. There was no overreach for the protocol to remove, so this run says nothing
about whether the protocol reduces overreach.

What the run did measure: **a cost.** With the protocol, 8 of 18 control answers carried caveats outside the
question (did the log reflect the current code, does runtime match the config, no LICENSE file); without it,
0 of 18. That fails pre-registered condition 2 (at most +15 points of unneeded hedging) by a wide margin
(+44 points; Wilson for 8/18: 0.25–0.66). The cross-family grader and the deterministic rules disagreed on
exactly these 8 runs: the keyword rules do not recognise a trailing caveat as a hedge; the grader's label
is the primary one, as pre-registered.

Process notes (not changes to the design):
- The first launch failed on all 90 runs in seconds (relative `--out`, subject runs with `cwd` = fixture);
  the failed runs produced no answer and were deleted; the harness now resolves paths and stops after three
  process errors.
- The first `grade` call wrote the grader's answer to the wrong folder for the same reason; it was re-run
  with the fix. No label was edited by hand.
- Turns: median 4 in both conditions; estimated API-equivalent cost 4.32 (A) and 4.44 (B) USD, paid from the
  owner's subscription, not billed.

Response: the Tier 1 rule of the protocol now limits `NOT CHECKED` to what bears on the question
(commit on `claude/entry-contract-sync`). An exploratory re-run of condition B with that wording is reported
separately and is not part of this pre-registered result.

Next for a discriminating benchmark: traps that the baseline actually fails — larger repositories where a
full read is expensive, truncated tool output, conflicting sources with dates — and a second model.

Owner check: `OWNER_SAMPLE.md` (18 runs, fixed seed) is open for the owner's labels.
