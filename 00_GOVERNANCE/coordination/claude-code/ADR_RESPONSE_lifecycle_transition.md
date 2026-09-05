# ADR RESPONSE — Lifecycle Transition: REVIEW → ACTIVE vs REVIEW → VERIFIED → ACTIVE

**STATUS**: DECIDED
**RESPONDS TO**: `00_GOVERNANCE/coordination/antigravity/ADR_DRAFT_lifecycle_transition.md`
**DATE**: 2026-09-05
**DECIDED BY**: claude-code (runtime security / lifecycle integrity owner, per
handoff superseding the prior CODEX ownership referenced in the draft)
**TARGET MODULE**: `memory_controller/controller.py`

---

## Decision

**Option 1 (strict two-gate sequence) is adopted, unchanged from the code as
it already stood.** `MemoryController.promote()` keeps the existing check:

```python
if note.get('verification') != 'verified':
    raise ValueError('Only VERIFIED notes can be promoted to ACTIVE')
```

**Option 2 (auto-attestation inside `promote()`) is explicitly rejected.**
`promote()` must never set `verification` itself. Lifecycle and verification
remain two separate fields, changed by two separate, separately-authorized
operations (`attest()` and `promote()`), which is exactly what the code
already does — `promote()` only ever *reads* `verification`, and `attest()`
is the only method that writes it.

**Option 3 (revert to orthogonal axis, remove the check) is rejected.** It
reopens the exact hole Options 1/2 exist to close: an ACTIVE note could carry
`verification: unverified` indefinitely.

## Why Option 1's five failing tests were a test-suite bug, not a code bug

All five tests the ADR draft flagged
(`test_audit.py::test_audit_promote_success_and_fail`,
`test_authorization.py::test_human_promote_allowed`,
`test_authorization.py::test_admin_promote_allowed`,
`test_cache.py::test_mutation_invalidation_review_promote`,
`test_milestone3_empirical_challenge.py::test_concurrent_attest_and_update_race_sqlite`)
called `controller.promote()` on a note whose `verification` was left at the
default `unverified` (or never set), i.e. they assumed the legacy
`REVIEW → ACTIVE` contract. They have been updated, not the contract:

- `test_audit_promote_success_and_fail`, `test_human_promote_allowed`,
  `test_admin_promote_allowed`: these test PROMOTE *authorization*
  specifically (can HUMAN/ADMIN call `promote()` at all), not the `attest()`
  workflow. Fixture notes are now seeded with `verification: "verified"`
  directly via `storage.set()`, which is honest for what these tests
  actually check. A new test,
  `test_authorization.py::test_human_promote_rejected_without_verification`,
  was added alongside them to close the gap the ADR draft actually
  identified: confirm `promote()` on an *unverified* REVIEW note is rejected
  even for an authorized HUMAN principal.
- `test_mutation_invalidation_review_promote`,
  `test_concurrent_attest_and_update_race_sqlite`: these exercise the real
  propose → review → (now: attest) → promote pipeline end-to-end, so a real
  `controller.attest(...)` call was inserted before `promote()`, keeping
  them realistic instead of bypassing the gate.

No test was weakened, deleted, or changed to assert a different (looser)
outcome than before. All five now exercise the *documented* contract instead
of the *undocumented* one.

## Interaction with I-001..I-012 and P0-001..P0-015 (re-confirmed empirically)

`memory_controller/tests/test_adversarial_p0_p15_invariants.py`,
`test_security_hardening.py`, and `cognitive_core/tests/test_tool_router_security.py`
(32 tests total, covering all 15 P0 contracts — see
`P0_P15_COVERAGE_MATRIX.md` in this directory) all pass under Option 1.
Several of those tests already called `attest()` before `promote()` even
before this response — they were written correctly against the intended
contract from the start; only the five *legacy* tests above had drifted.

## Interaction with ToolRouter (BRAIN-13)

The ADR draft correctly predicted that under Option 1, every ACTIVE note is
guaranteed `verification: verified`, so
`cognitive_core/tool_router.py`'s reconciliation boundary
(`if node.get("verification") == "verified": raise ApprovalRequiredError`)
now effectively gates *every* autonomous mutation of *every* ACTIVE note
behind explicit approval, for any principal routed through `ToolRouter`,
regardless of role. This is treated as correct, not as a regression:
`ToolRouter` represents the *automated/agentic* execution path; a human
acting with genuine explicit intent calls `MemoryController` directly
(`controller.archive(...)`, `controller.update(...)`, etc.), which is a
*different* call path than `ToolRouter.execute(...)`. This distinction is
now also applied consistently to `archive()` (see below), which previously
had no lifecycle gate of its own and relied entirely on `ToolRouter`'s
boundary — a single point of enforcement, which section 6/9 of the runtime
security brief explicitly calls insufficient ("not enough for a single path
to be safe").

## Related decision: `archive()` now enforces its own lifecycle gate

Out of scope for the original ADR draft, but discovered while implementing
this decision: `MemoryController.archive()` had **no lifecycle restriction
whatsoever** — a RAW, CLASSIFIED, or already-ARCHIVED note could be
"archived" again with an empty `reason`. Per the same "canon must be a single
coherent state machine" principle as the promote() decision above:

- **Archivable lifecycles**: `ACTIVE`, `REVIEW`. Not `RAW`/`CLASSIFIED`/
  `NORMALIZED` (still in-flight, not yet reviewed at all), not
  `SUPERSEDED`/`ARCHIVED` (terminal states; re-archiving is rejected, not a
  silent no-op, so a caller cannot use `archive()` to overwrite the
  `archive_reason` trail on an already-archived note).
- **Evidence**: `reason` must be non-empty (previously accepted and stored
  any value, including `""`).
- **Who**: unchanged at the authorizer level (HUMAN, ADMIN — never
  AI_AGENT), plus a new rule: archiving an ACTIVE note whose
  `verification == "verified"` additionally requires ADMIN, not plain HUMAN
  — a slightly stricter bar than promote()/attest() for *removing* trusted
  knowledge from active circulation, consistent with the ADR's own
  recommendation to maximize trust-boundary rigor.
- Every transition is now audited with `previous_lifecycle` and
  `new_lifecycle` explicitly, matching the same evidence standard already
  required of `attest()`.

Full details and adversarial tests: `memory_controller/tests/test_archive_state_machine.py`.

## What was deliberately left unchanged

`review()`'s acceptance of `{RAW, CLASSIFIED, NORMALIZED, REVIEW} → REVIEW`
was **not** tightened to match the literal `_validate_note()` transition
table (which — separately noted as a finding below — is not actually
enforced on this path at all). Rationale: REVIEW is not a privileged state;
nothing about entering it from any pre-review lifecycle bypasses
verification or authorization, and MemoryController does not itself
implement the classify/normalize pipeline stages (no code path exists that
produces a `CLASSIFIED` or `NORMALIZED` note in this codebase today), so
requiring literal `RAW → CLASSIFIED → NORMALIZED → REVIEW` stepping inside
`review()` would only break working functionality without closing any actual
attack surface. If a classify/normalize pipeline is added later, this should
be revisited.

## Known gap flagged, not fixed (out of scope for this pass)

`_validate_note()`'s declared lifecycle transition table (`controller.py`,
originally around line 106-124) is effectively **dead code** for
`review()`, `promote()`, `archive()`, and `supersede()` — none of them call
it; each has its own hand-written, ad-hoc lifecycle check instead (now
audited and, where needed, hardened individually — see `FINDINGS.md`). It is
still exercised by `propose()`/`update()`, where it is *reachable but
functionally inert* today because `update()` marks `lifecycle` immutable and
`propose()` mutating an existing id is rare. Recommend either deleting this
table (if the per-method checks are considered the sole source of truth) or
consolidating all five methods onto a single shared state-machine module —
this is an architecture decision with a larger blast radius than this pass's
budget, flagged for a follow-up, not silently left ambiguous.
