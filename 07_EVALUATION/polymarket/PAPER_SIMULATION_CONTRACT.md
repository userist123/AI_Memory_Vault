# Phase 10 — Paper Simulation Contract

## Purpose

Provide a deterministic paper-only execution layer between the analysis/risk
output and a simulated portfolio ledger.

## Required inputs

A simulation requires:

- market and outcome identity;
- timestamped historical quote;
- positive visible depth;
- positive research size;
- explicit fee assumption;
- explicit slippage assumption.

Missing or contradictory identity fails closed. No field is inferred from a
wall clock, network response, wallet, credential, or live venue.

## Fill model

The simulator is deliberately synthetic, not a claim about historical fills.

- visible depth limits filled notional;
- `max_depth_fraction` limits how much visible depth may be consumed;
- slippage is a declared basis-point model and is capped by the instruction's
  tolerance;
- fees are an explicit basis-point input;
- no randomisation or hidden market impact is introduced;
- excess requested size becomes an explicit partial fill.

## Ledger semantics

`PortfolioLedger` is immutable-by-value. Each accepted simulated fill produces
an explicit cash delta, position delta, and cumulative fee total. Refused fills
leave state unchanged.

## Safety boundary

This phase has no:

- venue adapter;
- credentials;
- wallet;
- signing;
- network calls;
- live submission path.

A paper fill is evidence about the chosen simulation assumptions, not evidence
that a corresponding historical fill occurred.

## Acceptance

Acceptance requires the dedicated Phase 10 workflow to pass the Phase 1–10
Polymarket regression tests, including full/partial/refused fills, deterministic
replay, explicit fee/slippage handling, and portfolio-state transitions.
