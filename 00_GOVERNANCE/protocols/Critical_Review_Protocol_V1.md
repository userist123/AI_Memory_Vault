---
type: core
category: integrity
status: active
version: 1.0.0
id: "91ab1ed2-d47f-4d68-a4e7-49a8aef420a1"
document_kind: policy
document_status: active
provenance_status: complete
relations:
  - target_id: "fcd80a74-9192-4fb8-baa2-527c0a61f26e"
    type: related_to
    target: "[[No Fabrication Policy]]"
  - target_id: "3452fafe-8b80-445e-a742-9ceb68662e69"
    type: related_to
    target: "[[Confidence Model]]"
policy_scope: vault-governance
---

# Critical Review Protocol V1 — checks for conclusions, comparisons and plans

Owner rule, 2026-10-30. Applies to every agent in this repository (Claude, Codex/GPT, Gemini/Antigravity,
local models) and to the owner's own requests for a verdict. Reviewed before adoption by two independent
model families (Codex/GPT and Gemini via Antigravity); their objections and the decisions on them are in the
corrections ledger and in the PR that introduced this file.

## What it is for

`No_Fabrication_Policy.md` forbids presenting invented data as real. `Confidence_Model.md` grades how strong
the evidence is. Neither catches the error this protocol targets: **every fact stated is true, but the
conclusion claims more than the facts support.**

| Error class | What happens | Real example (see the ledger) |
|---|---|---|
| `scope` | evidence covers part of a population, the claim covers all of it | "no compromise" after scanning part of a 4700-file repo |
| `search_space` | the answer was looked for in some of the places it could be | changes called "unique" after comparing with `main` only, not the other 262 branches |
| `staleness` | evidence was true for another commit, branch, date or environment | a clone from GitHub analysed as if it were the owner's current working copy |
| `construct_validity` | the check measures a proxy, not the property claimed | a test that greps headings, cited as proof the protocol works |
| `composition` | parts pass in isolation, the integrated path is broken or not wired | a module with green unit tests that nothing in production imports |
| `alternative` | one explanation is accepted without testing rivals that fit the same evidence | |
| `disconfirmation` | nothing was done that could have shown the claim wrong | |
| `overconfidence` | the headline is stronger than the evidence written below it | |
| `fabrication` | covered by `No_Fabrication_Policy.md`; recorded here only for the record | |

## Two tiers

**Tier 1 — light** (routine "done", "fixed", "passing", "not found" inside ordinary work): state what was
checked and what was not (`CHECKED` / `NOT CHECKED`), and do not let the headline exceed it.
`NOT CHECKED` names only what bears on the question that was asked. When what you checked answers the
question completely, answer it plainly: a caveat about a question nobody asked (is the log current, does
runtime match the config, would a re-run agree) is noise, not care. Observed on 2026-10-30 (exploratory,
n = 18): such caveats in 8 of 18 simple answers with this protocol, 0 of 18 without it.

**Tier 2 — full card and challenge**, mandatory before:
1. a universal verdict: safe / compromised / clean / none / all / unique / always / never;
2. a comparison or ranking: "X is better than Y", "X beats everything that exists";
3. a root cause presented as established;
4. any irreversible, destructive or authority-sensitive action, or a plan of more than three steps that
   drives one;
5. anything written into `00_GOVERNANCE/VAULT_STATE.md`, into memory as verified, a release note or a
   security report.

Not required for routine factual answers, mechanical edits, or reading and summarising a file.

## The five checks (Tier 2)

1. **Scope** — What exactly did the evidence cover: n of N items, which branch and commit, which date,
   which machine and environment? Does the claim cover more, or a later state? If yes, narrow the claim or
   mark the rest unverified. If narrowing leaves part of the owner's question unanswered, say which part.
2. **Search space** — Where else could the answer be (other branches, history, other machines, private
   overlay, untracked files, other runtimes)? Name the places not searched.
3. **Alternatives** — At least two explanations a sceptic would actually propose for the same evidence,
   and the observation that separates them; state whether that observation was made. "None found" must
   say what was tried.
4. **Disconfirmation** — What result would prove the claim wrong? Was it looked for with a check that
   could have failed, that measures the claimed property itself (not a proxy), and on the integrated path
   (not only an isolated part)? A check that cannot fail is not evidence.
5. **Confidence and residual** — Level per `Confidence_Model.md`, the explicit list of what stays
   unverified, and the cheapest next check that would raise confidence.

## Claim card

```
CONCLUSION:   <one sentence, no stronger than the lines below>
CHECKED:      <what, with counts/paths/commands/commit — e.g. "1592 files vs objects of all 262 remote branches incl. history, commit d8f978fa">
NOT CHECKED:  <places, populations, states left out>
ALTERNATIVES: <rival explanations, the separating observation, made: yes | no>
FALSIFIER:    <what would prove it wrong> — searched: yes | no
CONFIDENCE:   unknown | low | medium | high | very_high
```

Rules:
- The headline must not be stronger than the card. "No signs found in a targeted scan" is allowed;
  "it is not compromised" is not, unless the scope check passed.
- Every line names concrete evidence (paths, counts, commands, ids, commit). Evidence is adequate only if
  it could distinguish the conclusion from its main alternative; a pile of correct but non-diagnostic
  citations does not count.
- Confidence is capped at `medium` when `FALSIFIER — searched: no`, and also for any universal claim
  (trigger 1) while `NOT CHECKED` is non-empty.

## Pre-mortem for plans

Before executing a Tier-2 plan: assume it has failed one month from now and list the three most likely
reasons. For each: a mitigation in the plan, or an accepted risk recorded with the owner. State a stop
criterion (the observation that ends or re-plans the work).

## Independent challenge (Tier 2)

- The challenger is a **different model family** from the author (author Claude → Codex/GPT via
  `codex exec --sandbox read-only`, Gemini via `agy --mode plan --sandbox`, or a local model). Same-family
  review does not count as independent, consistent with the routing independence rule in `AGENTS.md`.
- It gets the claim and **pointers to the raw sources**, and retrieves and reads the evidence itself; it
  does not get the author's curated excerpts or full reasoning. It cites what it read. Its task is the
  strongest counter-argument, not a summary.
- The author answers every objection `accepted` (and changes the conclusion) or `rejected` (with evidence).
  Rejected objections go back to the challenger once; if it still disputes them on a Tier-2 matter, the
  owner decides. Unanswered objections block the verdict.
- Only text the vault may share with that runtime is sent (`04_CONFIG/access_policy.yaml`); never the
  private overlay, never owner material.

## Corrections ledger

`00_GOVERNANCE/review/CORRECTIONS_LEDGER.jsonl`, one JSON line per corrected conclusion, fields
`date, agent, model_family, claim, correction, error_class, caught_by, severity, evidence_ref`;
`error_class` from the table above, `caught_by` = `owner | self | challenger | test`,
`severity` = `low | medium | high`. No owner identifiers, owner machine paths or owner material.

An entry is **mandatory** whenever an owner objection, a challenger objection or the agent's own retraction
changes a stated conclusion — rewording silently is a violation.

What the ledger is: a log of discovered errors, used to see which classes recur and where a deterministic
test should replace judgement. What it is **not**: proof that the protocol works. It counts only noticed
corrections and has no denominator.

## Is it effective? — status UNVERIFIED

Effectiveness will be measured, not asserted, by a seeded benchmark: claims with planted overreach of each
error class, answered with the protocol off and on, scored blind by a different model family, reported as
rates over the total number of claims (overreach rate, post-challenge owner reversal rate =
owner reversals / challenged verdicts) with the pre-protocol baseline. Until that runs, this file makes
no claim that the protocol reduces errors.

First run, 2026-10-30 (`07_EVALUATION/critical_review_benchmark/`, pre-registered; Sonnet, 9 trap and 6
control cases, 3 repeats, graded blind by Codex): **benchmark not discriminating** — without the protocol the
model overclaimed in 0 of 27 trap runs (several traps put the answer in plain sight), so it shows no
reduction for this model on these traps. Exploratory observation, not an established cost: caveats outside
the question on 8 of 18 control answers with the protocol, 0 of 18 without (n = 18, style-dependent
grading); the Tier 1 scope sentence above is a hypothesis under test. Still UNVERIFIED for benefit; next step: traps that a baseline
actually fails (larger repositories, partial tool output, time pressure) and a second model.

## Limits and deferred work

Process rule, not proof: a challenger from another family can share the same blind spot, and judgement
cannot be fully tested. `20_TESTS/test_critical_review_protocol.py` only checks wiring and ledger format.
Deferred, owner decision: a gate in code that refuses `VAULT_STATE.md` edits and verified-memory writes
without a claim card and challenge receipt (it touches the memory write path).
