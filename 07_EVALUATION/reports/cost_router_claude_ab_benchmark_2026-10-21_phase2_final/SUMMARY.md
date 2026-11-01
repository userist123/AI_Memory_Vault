# Claude Code cost-router A/B benchmark

HEAD of benchmark worktree: `2c72a7228`; model flag: `sonnet`; max-turns 15 (T6: 40); max-budget-usd 1.5 (T6: 2.5); repeats 2; router hooks from working tree (overlay).

Metric definitions: tokens come from `usage` summed over ALL stream-json `result` events, cost from the last (cumulative) `total_cost_usd`; latency = harness wall clock; tool_calls/delegations are counted from `tool_use` blocks in the stream (Agent/Task = delegation); `context_tokens` is NOT reported. Correctness = the per-task check in this script. Baseline = `--setting-sources user` (no project hooks); router = `user,project`. Formulas: see script header.

Total spend: $7.0633 (cap 15.0).


**Recomputed on 2026-10-21 after a harness fix** (no new claude runs): the token, turn, tool-call and latency columns were rebuilt from the privacy-reduced raw streams with usage summed over all `result` events (delegated runs have 2+) and `latency s` = wall clock (`wall_s`) instead of the last event's duration_ms. Cost values are unchanged (last `total_cost_usd`, equal to the sum of modelUsage.costUSD). Correctness of T1, T3, T4, T5 is as stored (checks at run time); T6 is re-scored only for the new admission-of-unfixed-issues rule, applied to the stored final answer text (the stricter `--list` vs plan() structural check needs the worktree state and applies to new runs only). T2 rows are excluded: the v1 premise (PLUGINS_DIRECTORY) does not exist at HEAD.

Re-scoring notes:
- T6_baseline_1 re-scored correct -> INCORRECT by the T6 admission rule (stored result text matches)
- T6_router_1 re-scored correct -> INCORRECT by the T6 admission rule (stored result text matches)

| task | kind | condition | n | correct | cost USD | tool_calls | delegations | latency s | cached in | uncached in | output tok |
|---|---|---|---|---|---|---|---|---|---|---|---|
| T1 | explore | baseline | 2 | 2/2 | 0.1831 ± 0.0023 [0.1815..0.1847] | 1.0 ± 0.0 [1.0..1.0] | 0.0 ± 0.0 [0.0..0.0] | 15.2 ± 4.5 [12.1..18.4] | 99307 ± 5129 [95680..102934] | 4 ± 0 [4..4] | 180 ± 13 [170..189] |
| T1 | explore | router | 2 | 2/2 | 0.2040 ± 0.0014 [0.2030..0.2050] | 2.0 ± 0.0 [2.0..2.0] | 0.0 ± 0.0 [0.0..0.0] | 16.9 ± 0.8 [16.3..17.4] | 209706 ± 8881 [203426..215986] | 6 ± 0 [6..6] | 408 ± 39 [381..436] |
| T3 | small-change | baseline | 2 | 2/2 | 0.2427 ± 0.0059 [0.2386..0.2469] | 6.5 ± 0.7 [6.0..7.0] | 0.0 ± 0.0 [0.0..0.0] | 26.5 ± 9.8 [19.6..33.4] | 339838 ± 9371 [333212..346464] | 10 ± 0 [10..10] | 1686 ± 255 [1506..1867] |
| T3 | small-change | router | 2 | 2/2 | 0.3993 ± 0.0185 [0.3862..0.4124] | 13.0 ± 0.0 [13.0..13.0] | 1.0 ± 0.0 [1.0..1.0] | 73.8 ± 5.9 [69.7..78.0] | 638090 ± 69766 [588758..687422] | 15 ± 1 [14..16] | 3114 ± 175 [2990..3238] |
| T4 | multi-file | baseline | 2 | 2/2 | 0.2300 ± 0.0014 [0.2290..0.2310] | 8.0 ± 0.0 [8.0..8.0] | 0.0 ± 0.0 [0.0..0.0] | 43.5 ± 5.7 [39.4..47.5] | 330376 ± 799 [329811..330941] | 10 ± 0 [10..10] | 1688 ± 93 [1622..1754] |
| T4 | multi-file | router | 2 | 2/2 | 0.3997 ± 0.0505 [0.3640..0.4354] | 17.5 ± 3.5 [15.0..20.0] | 1.0 ± 0.0 [1.0..1.0] | 71.3 ± 17.0 [59.3..83.4] | 612556 ± 51920 [575843..649269] | 15 ± 1 [14..16] | 3266 ± 187 [3134..3399] |
| T5 | review | baseline | 2 | 2/2 | 0.2287 ± 0.0016 [0.2276..0.2299] | 1.0 ± 0.0 [1.0..1.0] | 0.0 ± 0.0 [0.0..0.0] | 38.2 ± 0.6 [37.8..38.7] | 98828 ± 5901 [94656..103001] | 4 ± 0 [4..4] | 3176 ± 361 [2920..3431] |
| T5 | review | router | 2 | 2/2 | 0.2492 ± 0.0166 [0.2375..0.2610] | 1.5 ± 0.7 [1.0..2.0] | 0.0 ± 0.0 [0.0..0.0] | 43.7 ± 3.3 [41.4..46.0] | 161935 ± 64341 [116439..207431] | 5 ± 1 [4..6] | 3620 ± 367 [3360..3879] |
| T6 | long-multistep | baseline | 2 | 1/2 | 0.4169 ± 0.1098 [0.3393..0.4946] | 13.0 ± 1.4 [12.0..14.0] | 0.0 ± 0.0 [0.0..0.0] | 55.6 ± 5.9 [51.4..59.8] | 621934 ± 1915 [620580..623288] | 16 ± 0 [16..16] | 4895 ± 247 [4720..5070] |
| T6 | long-multistep | router | 2 | 1/2 | 0.5350 ± 0.0265 [0.5163..0.5538] | 20.5 ± 2.1 [19.0..22.0] | 1.0 ± 0.0 [1.0..1.0] | 101.4 ± 14.4 [91.2..111.6] | 859086 ± 82233 [800939..917234] | 19 ± 1 [18..20] | 5384 ± 81 [5327..5441] |

Cells: mean ± sample sd [min..max]. Router vs baseline (cost, 95% CI of the mean difference):

| task | n base/router | correct base vs router | mean cost base | mean cost router | delta % | CI95 of delta USD | excludes zero |
|---|---|---|---|---|---|---|---|
| T1 | 2/2 | 2/2 vs 2/2 | 0.1831 | 0.2040 | +11.4% | [-0.0034, +0.0452] | no |
| T3 | 2/2 | 2/2 vs 2/2 | 0.2427 | 0.3993 | +64.5% | [-0.0179, +0.3311] | no |
| T4 | 2/2 | 2/2 vs 2/2 | 0.2300 | 0.3997 | +73.8% | [-0.2841, +0.6235] | no |
| T5 | 2/2 | 2/2 vs 2/2 | 0.2287 | 0.2492 | +9.0% | [-0.1295, +0.1705] | no |
| T6 | 2/2 | 1/2 vs 1/2 | 0.4169 | 0.5350 | +28.3% | [-0.8969, +1.1332] | no |

Sign test across tasks: router cheaper in 0 of 5 tasks (two-sided exact binomial p=0.062); magnitude ignored. With n=2 per arm no per-task conclusion is supported.

Excluded rows (premise invalid at HEAD; excluded): T2_baseline_0, T2_baseline_1, T2_router_0, T2_router_1.
