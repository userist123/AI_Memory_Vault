---
name: critical-review
description: Run before stating a verdict (safe, compromised, done, fixed, none, all, unique), a comparison or ranking ("X is better", "beats everything"), a root cause, a claim that something does not exist, a plan of more than three steps, an irreversible action, or anything written into VAULT_STATE.md or memory as verified.
---

# Critical review (procedure)

Canonical rule: `00_GOVERNANCE/protocols/Critical_Review_Protocol_V1.md`. On conflict that file wins.

1. Pick the tier. Tier 1 (routine done/fixed/passing/not found): state `CHECKED` and `NOT CHECKED`; the
   headline never exceeds them. Tier 2 (universal verdict, comparison, root cause, irreversible action or
   the plan driving it, VAULT_STATE/verified memory, release or security report): everything below.
2. Five checks: scope (n of N, commit, date, environment; say which part of the question stays unanswered),
   search space, alternatives a sceptic would raise, disconfirmation (a check that could fail, measures the
   claimed property, on the integrated path), confidence and residual.
3. Claim card: `CONCLUSION / CHECKED / NOT CHECKED / ALTERNATIVES / FALSIFIER (searched: yes|no) / CONFIDENCE`.
   Evidence must be diagnostic. Confidence ≤ `medium` if the falsifier was not searched, or for a
   universal claim while `NOT CHECKED` is non-empty.
4. Plans: pre-mortem (three failure reasons one month out, each mitigated or accepted) and a stop criterion.
5. Tier 2 challenge from a different model family, read-only (`codex exec --sandbox read-only` or
   `agy --mode plan --sandbox`): give the claim and pointers to raw sources, not curated excerpts; answer
   every objection `accepted` or `rejected` with evidence; disputed rejections go back once, then to the owner.
6. Whenever an objection or a retraction changes a conclusion, append a line to
   `00_GOVERNANCE/review/CORRECTIONS_LEDGER.jsonl` (schema in the protocol; no owner identifiers).
