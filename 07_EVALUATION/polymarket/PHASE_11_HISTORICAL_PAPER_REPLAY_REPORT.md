# Phase 11 — Real historical multi-market paper replay

## Scope

Phase 11 closes the structural gap between the deterministic Phase 10 paper-fill simulator and actual historical Polymarket data.

It does **not** claim profitability, execution realism, or model alpha.

## External data contract verified

Polymarket's Gamma API exposes market metadata including outcomes and `clobTokenIds`; the public CLOB `/prices-history` endpoint exposes historical timestamp/price observations by outcome token. The documented batch prices-history endpoint accepts up to 20 token ids per request. Public read access requires no trading credentials.

Resolution is deliberately treated as a terminal evaluation fact. The collector learns the resolved outcome from a current closed-market snapshot and records `resolution_known_at`. The replay never allows that current resolution fact to influence the entry choice.

## New implementation

`03_IMPLEMENTATION/packages/polymarket/historical_paper_replay.py`

- binds every historical price point to an explicit `market_id` and `outcome_id`;
- normalizes Gamma market metadata and CLOB price history without inventing missing fields;
- refuses ambiguous terminal outcomes;
- records the time the terminal resolution became known to the collector;
- provides a deterministic mechanical control: buy the first listed outcome at its first observed historical price, then settle at the provider-reported terminal outcome;
- keeps the mechanical control separate from any claim about the Memory Vault model.

## Tests

`20_TESTS/polymarket/test_historical_paper_replay.py` covers market identity binding, terminal resolution validation, missing history, and the no-lookahead mechanical control path.

The Phase 11 workflow also runs the prior Polymarket regression gates and a live public-data smoke test which collects at least three suitable resolved binary markets and writes a JSON evidence artifact.

## Execution assumptions

Historical `/prices-history` provides prices, not historical visible depth. Phase 10's `PaperQuote.available_depth`, fee, and slippage remain explicit synthetic inputs for this control run. The live smoke therefore proves real historical data ingestion and settlement plumbing, **not** historical fill realism.

## Acceptance boundary

Phase 11 is acceptable only when the dedicated CI job shows:

1. the deterministic regression suite passes;
2. at least three distinct resolved binary markets are collected from the public Gamma + CLOB APIs;
3. each market produces a mechanically pre-declared paper control trade;
4. settlement is computed only after entry from the terminal outcome;
5. the evidence artifact is uploaded.

A failure to collect three suitable markets is reported as `UNVERIFIED`, not substituted with fixtures.

## Next missing capability

After Phase 11, the remaining blocker to a meaningful **model-driven** paper backtest is not live execution. It is producing leakage-safe historical prediction records for the same market/cutoff pairs and feeding those predictions into the existing edge/risk/paper-simulation path.
