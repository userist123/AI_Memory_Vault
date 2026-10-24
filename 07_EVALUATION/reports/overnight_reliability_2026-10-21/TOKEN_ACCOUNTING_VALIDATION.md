# Token accounting validation

VERDICT: TOKEN SAVINGS NOT DEMONSTRATED.

Evidence labels as in the other deliverables. No `claude` run and no paid spend this mission ($0, MEASURED by the absence of any run command in the GATE_LOG).

## Aggregation contract (SOURCE_VERIFIED, `30_SCRIPTS/evaluation/claude_cost_router_benchmark.py`)
- `result.usage` covers the MAIN agent only; `modelUsage` covers ALL models including subagents. Totals are reconciled against `modelUsage`, never against `result.usage` alone (`reconciliation` :1353).
- Per-request usage is read from stream events and deduplicated by `message.id` (`stream_metrics` :957, `attribute` :759); duplicated stream events must not double count (fixture `fx_dup_stream_events`).
- A residual row (modelUsage minus attributed streams) is kept; a negative residual or an attributed model without a modelUsage record is a MISMATCH. An output-only residual is expected (streaming output is a lower bound) and is not a mismatch.
- Completeness flags (`metrics_from` :914): `usage_complete`, `tokens_complete`, `missing_fields`, `incomplete` (stream without result event). Missing usage is UNKNOWN, never 0; run totals become lower bounds and are excluded from statistics.
- A run with no result event is `incomplete` and never `correct` (`run_once` :1005, `recompute` :1404).
- Savings deltas are computed only over (task, repeat) pairs where BOTH arms are correct AND complete (`summarize_tokens` :1285); `pairs_used` / `pairs_excluded` with reasons are reported. A failed cheap arm creates no saving (TEST_VERIFIED `test_claude_cost_router_benchmark.py::test_failed_cheap_arm_creates_no_fake_token_or_cost_saving`, `::test_summarize_failed_cheap_arm_is_not_a_saving`).
- Cost/token inconsistencies are flagged: `usage_mismatch`, `cost_without_tokens`, `tokens_without_cost`, `cost_partial`, with SUMMARY WARNINGS.

## Fixtures (`20_TESTS/fixtures/telemetry/`, each with `.expected.json`)
`fx_dup_stream_events`, `fx_failed_high_tokens`, `fx_main_two_subagents`, `fx_missing_usage`, `fx_modelusage_differs`, `fx_no_result_interrupted`, `fx_partial_provider_record`, `fx_success_low_tokens`. Parametrised test `test_claude_cost_router_benchmark.py::test_telemetry_fixture_matches_its_expected_json` (:685). Other tests: `::test_missing_usage_is_unknown_and_run_total_is_a_lower_bound_excluded_from_statistics` (:704), `::test_interrupted_stream_is_incomplete_never_correct_and_listed` (:716), `::test_pairing_follows_repeat_and_reports_each_exclusion_reason` (:768), `::test_negative_residual_and_model_mismatch_are_flagged_and_warned` (:450).

## Tool
`python 30_SCRIPTS/evaluation/token_accounting_check.py <stream.jsonl | results_dir>` prints per-role/per-model categories, completeness flags and reconciliation; exit 0 = all complete and reconciled, 1 = incomplete or mismatched, 2 = usage error. Offline.
Run in this gate on archived raw streams (RUNTIME_VERIFIED, read-only): phase3_final 37 streams, 37 ok; phase4_pilot_haiku 46 streams, 46 ok; phase4_pilot_sonnet 11 streams, 11 ok; 0 incomplete or mismatched in all three (so the archived measurements are internally consistent; consistency is not evidence of savings).

## Recomputed numbers (MEASURED by the telemetry worker on scratchpad copies; archived directories untouched; not recomputed by the gate author)
- phase3_final, both-correct pairs, all total_tokens: reduction figure -58.1 %, i.e. the router arm used MORE tokens (negative saving; 16 pairs, 2 excluded).
- phase4 ctx_new vs base_old: +20.8 % tokens, 15 pairs, CI includes 0 (sign test p about 0.12).
- Combined: -5.7 %, 14 pairs.
- Only demonstrated effect: the context prefix is smaller by 3,904 tokens per request (MEASURED in an earlier phase), which is not an end-to-end saving.
- Phase-4 pilot: a haiku session with hooks ON raised subagents to sonnet/opus: cost +842 %, 1 correctness failure (SOURCE_VERIFIED from the phase-4 report; this is what the escalation policy now prevents, TEST_VERIFIED by the escalation matrix, not re-measured live).

## Why the verdict stands
1. Pairs where both arms are correct and complete are few (14-16); intervals include zero or the sign is the wrong way.
2. The router arm used more tokens in the cleanest comparison.
3. Previously failed or incomplete runs had been pooled and missing usage counted as 0; both were corrected only this mission, so older summaries are not comparable.
4. No live re-run was permitted (budget), so the corrected pipeline was validated on fixtures and archived streams only.
5. Cache read/creation weighting (price) was not varied; cost savings were not demonstrated either.
