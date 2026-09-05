# Lifecycle Single Source of Truth — Design Proposal (NOT implemented)

**Status of this document: proposal + evidence only.** Per instruction, no
architectural refactor is implemented in this pass. Where a decision would
require an architecture-level judgment call, this document says so
explicitly and stops rather than guessing.

---

## 1. Is there more than one source of truth? Yes — demonstrated, not assumed.

### 1.1 The declared canon is itself incomplete

`_validate_note()`'s transition table (`controller.py`):

```python
allowed = {
    Lifecycle.RAW: [Lifecycle.CLASSIFIED],
    Lifecycle.CLASSIFIED: [Lifecycle.NORMALIZED],
    Lifecycle.NORMALIZED: [Lifecycle.REVIEW],
    Lifecycle.REVIEW: [Lifecycle.VERIFIED],
    Lifecycle.VERIFIED: [Lifecycle.ACTIVE],
    Lifecycle.ACTIVE: [Lifecycle.SUPERSEDED, Lifecycle.ARCHIVED],
}
```

The `Lifecycle` enum itself has **9** members: `RAW, CLASSIFIED, NORMALIZED,
REVIEW, VERIFIED, ACTIVE, RECONSOLIDATING, SUPERSEDED, ARCHIVED`. This table
covers 6 of them as keys and never mentions `RECONSOLIDATING` at all — not as
a key, not as a value. If `Consolidator.challenge()`/`resolve_challenge()`
(section D of the inventory) were fixed to route through `_validate_note()`
tomorrow, the table as written would reject `ACTIVE -> RECONSOLIDATING`
(not present in `Lifecycle.ACTIVE`'s allowed list) and would have **no
allowed exit from `RECONSOLIDATING` at all** (`allowed.get(RECONSOLIDATING,
[])` returns `[]`). The declared canon does not actually describe the state
space the `Lifecycle` enum defines.

### 1.2 `VERIFIED` is declared as a lifecycle state but is never assigned by anything

Nothing in `memory_controller/**` or `cognitive_core/**` (excluding the
unrelated `MutationGate.ReviewState.VERIFIED`, a different enum for a
different concern — see inventory section C) ever executes `note['lifecycle']
= Lifecycle.VERIFIED`. The actual, running contract is: `lifecycle` skips
straight from `REVIEW` to `ACTIVE`, gated by the separate `verification`
FIELD equalling the string `"verified"` — not by the note's `lifecycle`
FIELD ever equalling `Lifecycle.VERIFIED`. The declared table's `REVIEW ->
VERIFIED -> ACTIVE` two-hop lifecycle path is aspirational text that no code
path implements. `promote()` implements a *different, correct-in-practice*
rule (`lifecycle==REVIEW AND verification=='verified' -> lifecycle=ACTIVE`)
that happens to achieve the same security property without ever using the
`VERIFIED` lifecycle value. This is not a contradiction in *behavior*, but
it is two different things both claiming to be "the" REVIEW-to-ACTIVE gate,
described in two incompatible vocabularies (a lifecycle value nothing sets,
vs. a verification field promote() actually checks).

### 1.3 Four of six mutating methods bypass `_validate_note()` entirely, each with hand-written, non-identical logic

| Method | Own logic | Would `_validate_note()`'s table agree? |
|---|---|---|
| `review()` | accepts `{RAW, CLASSIFIED, NORMALIZED, REVIEW} -> REVIEW` | **No.** The table only allows `NORMALIZED -> REVIEW`; it would reject `RAW -> REVIEW` and `CLASSIFIED -> REVIEW` outright, and has no entry allowing `REVIEW -> REVIEW` (idempotent re-review) at all. |
| `promote()` | accepts `REVIEW -> ACTIVE` directly (conditioned on `verification`) | **No.** The table requires an intermediate `REVIEW -> VERIFIED` hop that never happens (see 1.2). |
| `archive()` (this pass) | accepts `{ACTIVE, REVIEW} -> ARCHIVED` | **Partially.** The table only allows `ACTIVE -> ARCHIVED`; it has no entry for `REVIEW -> ARCHIVED` at all (REVIEW isn't even a key with ARCHIVED as a value). |
| `supersede()` | old note `-> SUPERSEDED` from whatever lifecycle it was in (no restriction of its own beyond `SupersessionEnforcer`'s already-SUPERSEDED check) | **Unclear/No.** The table only allows `ACTIVE -> SUPERSEDED`; `SupersessionEnforcer` does not check the predecessor's lifecycle at all before allowing supersession (it checks `verification`/`provenance` for the human-verified-by-AI rule, and lifecycle only to reject an already-`SUPERSEDED` predecessor) — so today a `REVIEW` note (never yet promoted) could be superseded, which the table would also reject. |

**Conclusion: not merely "unenforced elsewhere" — where it WOULD be
enforced, the declared table actively disagrees with three of the four
methods' actual, currently-shipping, intentional behavior.** Mechanically
routing all four through the existing table as-is would break `review()`
(most real proposals start at RAW, not NORMALIZED), `promote()` (nothing
ever produces a `VERIFIED`-lifecycle note to promote from), and would narrow
`archive()`/`supersede()` in ways not requested by anyone in this repo's
history.

### 1.4 Two informal, unauthenticated creation paths never consult any of the above

`FinancialQueryEngine.ingest_financial_note()` and
`FinancialIngestionPipeline._persist_note()` (inventory sections E, F) both
call `storage.set()` directly to CREATE notes, with their own schema
(`FINANCIAL_NOTE_SCHEMA`, not the canonical one) and no lifecycle-transition
concept at all (there is no "old state" — these are creation, not
transition — but they are still a second, parallel *entry point* into the
same lifecycle space `MemoryController.propose()` governs, with weaker
guarantees: no `Principal`, no audit event, and in F's case a
caller-influenced initial lifecycle value).

### 1.5 One confirmed unauthenticated transition bypass

`Consolidator.challenge()`/`resolve_challenge()` (inventory section D)
mutate `lifecycle` via `storage.get()`/`storage.set()` directly, consulting
none of the five rule sets, with no authorization check of any kind. This is
the clearest possible demonstration that "the" lifecycle state machine is
not actually a single gate today — it's a gate that happens to sit in front
of most, but not all, of the ways lifecycle can change.

---

## 2. Empirical check of the disputed transitions (brief section 4)

| Transition | Empirically required by any current code/test? | Evidence |
|---|---|---|
| `REVIEW -> ARCHIVED` | **Yes.** | `memory_controller/tests/test_tool_router_reconciliation_boundary.py::test_admin_cannot_archive_verified_review_memory` archives (attempts to, via ToolRouter, correctly blocked there) a note that is explicitly still in `REVIEW`. `memory_controller/tests/test_archive_state_machine.py::test_archive_allows_review` (this pass) exercises the direct-controller path. Real-world case: a human reviews a submission and decides to reject/archive it without ever promoting it. |
| `ACTIVE -> ARCHIVED` | **Yes** — the primary, uncontested case. | Original (pre-hardening) `archive()` behavior; `test_archive_state_machine.py::test_archive_allows_active_unverified_by_human`. |
| `VERIFIED -> ARCHIVED` | **Not applicable.** | No code path ever produces a note with `lifecycle == VERIFIED` (see 1.2) — there is nothing to archive *from* that state. If `VERIFIED` is ever wired up as a real lifecycle value in the future, this transition should be revisited, not assumed. |
| `SUPERSEDED -> ARCHIVED` | **No requirement found.** | Nothing in the test suite or any production caller archives an already-superseded note. `archive()` (this pass) rejects it; no test or caller expects otherwise. Recommend leaving this rejected unless a concrete need surfaces. |
| Any "revert"/backward transition (e.g. `ACTIVE -> REVIEW`, `ARCHIVED -> ACTIVE`) | **No controller-level requirement found**, but see below. | `Consolidator.resolve_challenge()` implements `RECONSOLIDATING -> REVIEW` (a de facto downgrade of a formerly-ACTIVE note) entirely outside the controller (section D bypass) — this is the one place in the codebase that *does* need a backward-ish transition, and it currently gets it by skipping validation entirely rather than by the canon explicitly allowing it. This is itself evidence that `RECONSOLIDATING` needs to be a first-class part of any single source of truth, not an oversight. |

**No new transitions have been invented for this document.** Everything
above is either observed in currently-shipping code/tests or explicitly
marked "no requirement found."

---

## 3. Verification vs. lifecycle — confirmed distinct, not touched

`verification` (`unverified` / `partially_verified` / `verified` /
`inferred`) and `lifecycle` (the 9-value enum above) are confirmed, by
reading every mutation site in the inventory, to remain two independent
fields today:

- `promote()` reads `verification`, writes `lifecycle`. Never writes
  `verification`.
- `attest()` reads and writes `verification`. Never writes `lifecycle`.
- No method in `memory_controller/controller.py` writes both fields in the
  same call.

This document does not propose merging them into one enum, and does not
propose auto-attestation inside `promote()` — both explicitly out of scope
per this task's own instructions, and both already rejected in
`ADR_RESPONSE_lifecycle_transition.md` from the prior pass.

---

## 4. Archive decision — three variants analyzed, none silently chosen

The prior pass introduced `ACTIVE + verified -> archive requires ADMIN` as a
judgment call, flagged for review rather than treated as settled. Analysis
of the three options this task asks for, so the decision (by whoever reviews
this) is informed rather than assumed:

### Option A — HUMAN and ADMIN can both archive anything in `{ACTIVE, REVIEW}`, no extra gate

- **Security impact**: lowest bar. A single compromised or careless HUMAN
  session could remove a human-verified, actively-relied-upon memory from
  circulation with no additional checkpoint. Symmetric with `promote()`
  (which also allows plain HUMAN), so at least internally consistent with
  the rest of the authorization matrix.
- **Operational impact**: simplest; no new failure mode ("why can't I
  archive this, I'm HUMAN") for reviewers to hit.
- **Backwards compatibility**: fully compatible with the pre-this-pass
  behavior (which had no gate at all, a superset of this).
- **Test impact**: `test_archive_of_verified_active_note_requires_admin`
  would need to be deleted or inverted; no other test in the current suite
  depends on the ADMIN requirement.

### Option B — only ADMIN can archive anything `VERIFIED`/`ACTIVE` (current implementation, applies today only to `verification=='verified'` ACTIVE notes)

- **Security impact**: highest bar for this specific action. Matches the
  general pattern that *removing* trust from circulation deserves at least
  the same rigor as *granting* it (`attest()` already requires HUMAN-or-ADMIN,
  never plain-AI; this extends "verified means something extra" to the
  archive direction too).
- **Operational impact**: a plain HUMAN reviewer who legitimately needs to
  retire a verified note must escalate to an ADMIN session — a real friction
  point if ADMIN is a scarce/separate role in actual usage, which this
  repository's own tests don't establish either way (no test exercises a
  HUMAN-vs-ADMIN staffing model beyond the enum).
- **Backwards compatibility**: **breaking** relative to pre-this-pass
  behavior (intentionally — that behavior was the F-02 finding). Not
  breaking relative to the current (post-this-pass) code, since it IS the
  current code.
- **Test impact**: covered by `test_archive_state_machine.py` (13 tests,
  all passing). No other existing test asserted a HUMAN-can-archive-verified
  expectation that this broke (verified via the full-suite 0-failed run).

### Option C — policy varies by lifecycle AND verification (e.g.: HUMAN can archive REVIEW notes regardless of verification; only ADMIN can archive ACTIVE notes regardless of verification; only ADMIN can archive anything verified regardless of lifecycle)

- **Security impact**: potentially the most precise, but combinatorial — 2
  lifecycles (`ACTIVE`, `REVIEW`) x 2 verification states (`verified`,
  not) = 4 cells, each needing its own justification. Current code already
  implements 3 of the 4 cells identically to what a "HUMAN can archive
  REVIEW, ADMIN-only for verified ACTIVE" version of Option C would say;
  the 4th cell (verified REVIEW) is currently un-gated (plain HUMAN can
  archive a verified-but-not-yet-promoted REVIEW note — see prior pass's
  ADR response, section "Archive Decision", which reasoned this is
  acceptable editorial behavior, not yet "in active circulation").
- **Operational impact**: hardest to reason about and explain to an
  operator ("why does archiving depend on both fields") without a written
  policy table like this one.
- **Backwards compatibility**: same breaking profile as B for the ACTIVE
  cells; additionally breaking (relative to the CURRENT code) if the
  verified-REVIEW cell is tightened to also require ADMIN.
- **Test impact**: would require a 4th test class
  (`test_archive_verified_review_requires_admin` or similar) not present
  today; `test_archive_state_machine.py::test_archive_allows_review` (which
  archives an *unverified* REVIEW note as HUMAN) would remain valid either
  way.

**This document does not choose between B (current) and C (a stricter,
fully-specified 4-cell version).** The current code implements a *specific
instance* of Option C in practice (3 of 4 cells match; the verified-REVIEW
cell is un-gated) without ever having stated that explicitly as "Option C
was chosen." That gap — an implicit, partial C dressed as B — is itself
worth a reviewer's explicit sign-off: either (a) formally adopt B and
loosen nothing further, (b) formally adopt the 4-cell C and add the missing
test+gate for verified-REVIEW, or (c) revert to A. **BLOCKED on this
specific sub-decision** pending review; no code change made in this pass.

---

## 5. Cross-storage equivalence (brief section 8)

All three storage engines were probed with the identical matrix (create at
RAW via a raw dict, `propose`+`review`+`attest`+`promote` via the same
`MemoryController` instance, `archive`, and a rejected transition) —
see `SECURITY_LIFECYCLE_PROPERTY_TESTS.md` for the actual test file and
results. **Confirmed equivalent**: `StorageEngine`, `FileStorageEngine`, and
`SQLiteStorageEngine` all enforce identical `lifecycle` semantics for every
`MemoryController` method, because none of the three engines has any
lifecycle-awareness of their own beyond (a) `query()`'s independent-but-
identical RAW exclusion and (b) `SQLiteStorageEngine`'s schema `CHECK`
constraint restricting `lifecycle` to the 8 non-`RECONSOLIDATING` values in
its `CREATE TABLE` statement (`RAW, CLASSIFIED, NORMALIZED, REVIEW,
VERIFIED, ACTIVE, SUPERSEDED, ARCHIVED` — **`RECONSOLIDATING` is missing from
the SQLite CHECK constraint too**, a sixth independent place the same
incompleteness from section 1.1 shows up; `Consolidator.challenge()` would
raise a raw `sqlite3.IntegrityError` rather than a clean `ValueError` if it
ever ran against `SQLiteStorageEngine`, which is itself further evidence the
bypass was never exercised against every backend). All controller-level
authorization/verification/audit behavior is identical regardless of which
engine backs it, since none of that logic lives in the engines.

---

## 6. Proposed `is_transition_allowed()` shape (design only, NOT implemented)

```python
def is_transition_allowed(
    old_state: Lifecycle | None,
    new_state: Lifecycle,
    principal: Principal,
    note: dict,
) -> tuple[bool, str | None]:
    """Single source of truth for every lifecycle transition in the system.
    Returns (allowed, reason_if_denied). Pure function -- no I/O, no
    mutation, no audit side effect (callers remain responsible for auditing
    their own call, exactly as today)."""
```

To replace the five existing rule sets, this function would need to absorb:

1. `_validate_note()`'s table — corrected to include `RECONSOLIDATING` and
   to match what `review()`/`promote()`/`archive()` actually do today (or a
   deliberately-chosen, narrower canon that some of those methods would then
   need to be tightened to match — a behavior change, not a pure refactor).
2. `review()`'s acceptance set.
3. `promote()`'s `verification`-conditioned check (this makes the function's
   signature necessarily take `note` as a whole, not just the two lifecycle
   values, since the gate depends on a field outside `lifecycle` itself).
4. `archive()`'s acceptance set plus the still-open Option B/C question from
   section 4.
5. `SupersessionEnforcer`'s rules — which are keyed on `(old_id, new_id)`
   pairs and graph-cycle detection, not just `(old_state, new_state)`; folding
   this in would require either widening the function's signature further
   (accepting the full `SupersessionEnforcer` context) or leaving
   supersession as a deliberately separate, adjacent function that
   `is_transition_allowed()` delegates to for the `-> SUPERSEDED` case only.

**This is a real architecture decision, not a mechanical extraction**:
section 1.3 already showed that a naive "just point everyone at the existing
table" would break three of four methods' current, intentional behavior.
Building the corrected single table requires the review sign-off from
section 4 (archive policy) first, plus an explicit decision on whether
`RECONSOLIDATING` becomes a real, controller-mediated state (fixing finding
D) or is removed from the `Lifecycle` enum entirely (if `Consolidator`'s
reconsolidation feature is considered out of scope / to be redesigned
separately).

**Verdict for this task: BLOCKED — ARCHITECTURE DECISION** on implementing
`is_transition_allowed()` itself, for exactly the reasons the task
anticipated. The inventory, duplication/contradiction evidence, empirical
transition requirements, cross-storage equivalence, and property tests
(all of which do NOT require the architecture decision) are complete and
`READY FOR REVIEW`.
