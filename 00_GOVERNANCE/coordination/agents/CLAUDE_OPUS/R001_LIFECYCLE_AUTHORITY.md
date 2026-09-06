# R001 — Canonical Lifecycle Policy Authority

- **Owner:** CLAUDE OPUS
- **Branch:** `r001/opus-lifecycle-authority`
- **Baseline:** `0449dafee` (`ci(r001): make enforcement fail-closed`)
- **Commits:** `2a72b16e9` (implementation), `0143b4d98` (tests + creation-bypass closure)
- **Merged to main:** NO — explicitly out of scope.
- **Evidence level:** `CODE_VERIFIED` + `TEST_VERIFIED`. `RUNTIME_VERIFIED` is **not** claimed: no production workload was executed against this branch.

---

## 1. What was unified

`03_IMPLEMENTATION/packages/lifecycle/policy.py` (new, 398 lines) is now the only
module in the runtime that decides whether a memory note may change lifecycle
state. It is pure (no I/O, no storage, no audit side effects, no mutation of its
arguments) and fail-closed: anything not explicitly listed is denied.

All nine canonical states are modelled: `RAW`, `CLASSIFIED`, `NORMALIZED`,
`REVIEW`, `VERIFIED`, `ACTIVE`, `RECONSOLIDATING`, `SUPERSEDED`, `ARCHIVED`.

### The two semantics

The audit found that the codebase was conflating two genuinely different
questions, which is why a single transition table could not previously be
extracted without breaking one caller or the other:

| Semantics | Question | Modelled as |
|---|---|---|
| **Operational** | "May `archive()` retire this note?" | `Mutation.{CREATE, REVIEW, PROMOTE, UPDATE, ATTEST, ARCHIVE, SUPERSEDE, RECONSOLIDATE_CHALLENGE, RECONSOLIDATE_RESOLVE}` |
| **Structural** | "Is this bare `lifecycle` field value sequence legal?" | `Mutation.STRUCTURAL_REWRITE`, a strict linear pipeline |

These are deliberately different in breadth. `archive()` may retire a `REVIEW`
note, while a bare `REVIEW -> ARCHIVED` field rewrite is structurally illegal.
Both rules now live in one module, so the duplication is eliminated **without**
changing either behavior. This is what made zero-regression unification
possible; the previously reported `BLOCKED — ARCHITECTURE DECISION` is resolved
by modelling the mutation, not just the state pair.

### Verification stays a separate axis

No auto-attestation was introduced. `UPDATE` and `ATTEST` are declared
non-transitioning: the policy denies them if `from_state != to_state`.
The ADR gate `REVIEW -> VERIFIED -> ACTIVE` is represented by a single module
constant, `RESTORE_PROMOTE_VERIFICATION_GATE`, which **defaults to `False` to
match the behavior currently shipping on main**. Flipping that one constant is
the entire implementation of the ADR decision. It is left unflipped here
because enabling it is a behavior change, not an audit finding, and belongs to
whoever owns the ADR.

---

## 2. Call-path proof

Every function in `03_IMPLEMENTATION/packages/**` that assigns the `lifecycle`
field, verified by AST scan rather than by grep:

| Path | Mutation | Gate |
|---|---|---|
| `memory/controller.py::propose` | `CREATE` | policy, before `storage.set` |
| `memory/controller.py::review` | `REVIEW` | policy |
| `memory/controller.py::promote` | `PROMOTE` | policy |
| `memory/controller.py::archive` | `ARCHIVE` | policy (+ new `previous_lifecycle`/`new_lifecycle` audit fields) |
| `memory/controller.py::supersede` | `SUPERSEDE` | policy, **before** the rollback snapshot |
| `memory/controller.py::_validate_note` | `STRUCTURAL_REWRITE` | policy — the duplicated hardcoded table was deleted |
| `learning/consolidation.py::challenge` | `RECONSOLIDATE_CHALLENGE` | policy |
| `learning/consolidation.py::resolve_challenge` | `RECONSOLIDATE_RESOLVE` | policy |

AST scan result: **7/7 lifecycle-writing functions gated, 0 gaps.**

### Paths audited and found already covered

- **`update()`** — declares `lifecycle` immutable and rejects any change; then
  passes through `_validate_note`, which is policy-gated. No lifecycle movement
  is reachable.
- **`attest()`** — never touches `lifecycle`; also passes through
  `_validate_note`. It writes only the verification axis.
- **`lifecycle/queue_promoter.py::promote_approved`** — holds no lifecycle rules
  of its own; it translates an `APPROVED` queue record into
  `controller.propose()`, which is gated. Clean by construction.

`_validate_note` is reached from `propose`, `update` and `attest` — precisely
the three paths that must not move lifecycle. `review`/`promote`/`archive`/
`supersede` carry their own operational gates instead. That separation is why
the strict structural pipeline and the broader operational rules coexist
without conflict.

### Creation bypasses closed

Two paths wrote canonical records **straight to storage**, never reaching
`propose()`:

- `interfaces/financial_ingestion.py` took `lifecycle` from externally supplied
  front-matter (`fm.get("lifecycle", "REVIEW")`). An imported document could
  therefore self-declare `VERIFIED` or `ACTIVE` with no policy evaluation and no
  principal check. Now gated as an `AI_AGENT` `CREATE`.
- `interfaces/financial_query.py` had the same shape with a hardcoded `REVIEW`.
  Gated identically so the value cannot be widened later without the policy
  agreeing.

Both **reject** rather than silently downgrading. A silent downgrade would be a
compatibility bypass and would hide the attempted escalation.

### Reconsolidation bypass closed (P0)

`Consolidator.challenge()` / `resolve_challenge()` previously rewrote settled
`ACTIVE`/`VERIFIED` memory directly to storage with no authorization and no
policy. The principal argument defaulted to `AI_AGENT` and was used only for
audit logging. Reconsolidation is now restricted to `HUMAN`/`ADMIN`, and a
denial emits a failure audit event before raising.

---

## 3. Test results

Measured in an isolated `git worktree` at the baseline commit with **only** this
branch's commits cherry-picked, so that other agents' uncommitted working-tree
changes could not contaminate the comparison. (A first attempt compared against
the dirty main tree and produced 6 spurious deltas; those were foreign changes,
not policy effects.)

| Run | Result |
|---|---|
| Baseline `0449dafee` | `17 failed, 746 passed, 1 skipped, 14 errors` |
| Baseline + this branch | `17 failed, 911 passed, 1 skipped, 14 errors` |

**Regression diff of the failure sets: empty.** Identical 17 failures and 14
errors, which are pre-existing repository-restructuring fallout on the baseline
and are untouched by this work. `+165` net passing tests.

### New coverage

- `20_TESTS/test_lifecycle_policy_authority.py` — **158 tests**. The structural
  pipeline is asserted **exhaustively over all 81 ordered state pairs**; every
  operational mutation's permitted sources are asserted over all 9 states.
  Fail-closed is asserted for unknown, `None` and malformed states, mutations,
  principals and requests. Table-completeness tests fail if a state or mutation
  is ever added without a policy entry. Purity is asserted by AST import
  inspection.
- `20_TESTS/test_reconsolidation.py` — **8 tests**, rewritten (see below).

### One deliberately changed test

`test_memory_reconsolidation_challenge_and_resolution` was the single test whose
behavior changed, and it is worth stating plainly: **it asserted the
vulnerability as expected behavior.** It drove a full challenge → resolve cycle
over settled `ACTIVE` memory as `Principal.AI_AGENT`.

It was rewritten rather than deleted or weakened. The legitimate cycle keeps its
original assertions under `HUMAN`, and new tests cover `AI_AGENT` denial on both
halves, denial via the defaulted principal, `ADMIN` retaining the capability,
and the fail-closed guarantee that a denied challenge leaves storage unchanged.

---

## 4. Remaining gaps

Stated as gaps, not as completed work:

1. **`RESTORE_PROMOTE_VERIFICATION_GATE` is `False`.** Main currently permits
   `REVIEW -> ACTIVE` without attestation. The policy can enforce the ADR with a
   one-line change and has tests for both settings, but flipping it is a
   behavior change and needs the ADR owner's decision.
2. **The financial ingestion gate assumes `AI_AGENT`.** These interfaces carry no
   principal parameter, so the most restrictive plausible role was chosen. If a
   human-driven import path is intended, it needs an explicit principal argument
   rather than a widened default.
3. **`review()` stores a review record with no `lifecycle` field** (`r{N}` ids).
   It is an audit record rather than a memory note, so it is out of scope here,
   but a lifecycle-less record sitting in the note store is a latent modelling
   inconsistency.
4. **Not runtime-verified.** All claims above are code- and test-level.
5. **17 pre-existing failures and 14 collection errors remain on the baseline**
   and were deliberately left untouched, per the no-unrelated-fixes constraint.
6. **Markdown/Obsidian projections are not gated.** This work covers the Python
   runtime's storage paths only. Any process that edits front-matter in files
   directly still bypasses the authority by construction.

---

## 5. Constraints honored

- No merge to `main`.
- No benchmark modification.
- No documentation-only fixes — every finding is backed by code and tests.
- No retrieval, graph, or observability redesign; no branch cleanup.
- No compatibility escape hatch: there is no "allow by default" branch anywhere
  in the authority.
- Legacy error message text preserved verbatim so existing assertions keep
  matching; policy detail is appended as a `[policy: ...]` suffix.


## 🔗 Legături Sinaptice
- [[Governance_Repository_Spine_Specification|Governance]]
- [[00 Core Map]]
- [[14 Subagents Council Map]]
- [[Knowledge Graph Home]]
