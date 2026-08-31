# B5 Token Efficiency Report

**Runs analyzed:** 30

## Estimated vs Actual

- Estimated total: 40841
- Actual total: 40841
- Delta: 0 (0.0%)

## Distribution (actual tokens per run)

- avg: 1361.4
- median: 1246.0
- p95: 2362.0
- min/max: 360.0 / 2481.0

## Agent breakdown

| Agent | Calls | Estimated | Actual | Avg | P95 | Share % |
|---|---|---|---|---|---|---|
| ROUTER | 21 | 7745 | 7745 | 368.8 | 658.0 | 18.9638% |
| VERIFIER | 20 | 7842 | 7842 | 392.1 | 651.5 | 19.2013% |
| SYNTHESIS | 30 | 2851 | 2851 | 95.0 | 127.0 | 6.9807% |
| CRITIC | 22 | 8082 | 8082 | 367.4 | 647.1 | 19.7889% |
| CONSOLIDATOR | 19 | 6493 | 6493 | 341.7 | 584.2 | 15.8982% |
| RETRIEVAL | 21 | 7828 | 7828 | 372.8 | 634.0 | 19.167% |

## Tier breakdown

| Tier | Calls | Actual Total | Avg/Call | Share % |
|---|---|---|---|---|
| light | 62 | 23415 | 377.7 | 57.3321% |
| heavy | 30 | 2851 | 95.0 | 6.9807% |
| standard | 41 | 14575 | 355.5 | 35.6872% |

## Context efficiency

- context_to_input_ratio: 0.934
- output_to_input_ratio: 0.024

## Regressions

None detected.

## Council efficiency verdict

- Average actual tokens/run: 1361.4
- Median: 1246.0
- P95: 2362.0
- Estimated vs actual: +0.0%
- Specialist share: 93.0%
- Synthesis share: 7.0%
- Heavy tier share: 7.0%
- Context/input ratio: 0.934
- Token regression: NO

**Top optimization candidate:** CRITIC / light

**Reason:** 19.8% of total tokens; tier 'light' accounts for 57.3% of total tokens
