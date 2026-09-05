# Lifecycle Mutation Inventory — Complete Runtime Audit (2026-09-05)

Scope: every place in the LIVE runtime (`memory_controller/**`, `cognitive_core/**`
excluding the P1.2/P2.1/plasticity experimental front, which never touches
`lifecycle`) where a note's `lifecycle` field can change, or a new note can
be created outside `MemoryController.propose()`. Excluded as out-of-scope,
noted for completeness:

- `AI_Memory_Vault_OBSIDIAN/**` — a full point-in-time mirror of this
  repository, excluded from `pytest.ini`'s `norecursedirs`; not live runtime.
- `02_PRODUCT/projects/workspaces/jarvis_cognitive_brain/**` — a separate
  product workspace with its OWN `Lifecycle` enum and storage engine, not
  importing from `memory_controller.controller`. A different application.
- `20_TESTS/supplemental/**`, `.agents/*/verify_*.py`, `.agents/*/probe_*.py` —
  legacy/one-off test and audit scripts, not part of `pytest.ini`'s
  `testpaths`, not invoked by any production code path.
- Test files under `memory_controller/tests/`, `cognitive_core/tests/` that
  construct fixture notes via `storage.set(...)` directly — these are test
  fixtures, listed once in the summary table, not itemized individually
  (there are dozens; they exist specifically to bypass validation for
  fast, isolated unit setup, which is the normal and correct use of a
  fixture, not a runtime concern).

---

## A. `memory_controller/controller.py` — `MemoryController` (the intended single entry point)

| Method | Old state -> New state | Authorization | Verification requirement | Validation path | Audit path |
|---|---|---|---|---|---|
| `propose()` | *(none)* -> `RAW` / `CLASSIFIED` / `NORMALIZED` / `REVIEW` (AI_AGENT restricted to these 4; HUMAN/ADMIN unrestricted at creation) | `Operation.PROPOSE` (HUMAN, AI_AGENT, ADMIN) | `verification` forced to `unverified` by default; explicit `verified` at creation is rejected for everyone | `_validate_note()` -> `validate_frontmatter()` (jsonschema) + `validate_provenance()` | `audit_event('propose', ...)` |
| `review()` | `{RAW, CLASSIFIED, NORMALIZED, REVIEW}` -> `REVIEW` | `Operation.REVIEW` (HUMAN, ADMIN) | none | **Does NOT call `_validate_note()`.** Hand-written lifecycle-set check only (`if note['lifecycle'] not in {...}`) | `audit_event('review', ...)` incl. `decision` |
| `attest()` | *(no lifecycle change — `verification` field only)* | `Operation.ATTEST` (HUMAN, ADMIN only; AI_AGENT structurally impossible) | requires non-empty `reason`+`evidence`; `verification_state` now whitelist-checked (this pass's F-03 fix) | `_validate_note()` (schema) + local whitelist | `audit_event('attest', ...)` incl. previous/new state, actor, evidence, reason |
| `promote()` | `REVIEW` -> `ACTIVE`, **iff** `verification == 'verified'` | `Operation.PROMOTE` (HUMAN, ADMIN) | **hard-gated**: rejects if not `verified` | **Does NOT call `_validate_note()`.** Hand-written 2-condition check only | `audit_event('promote', ...)` |
| `update()` | *(lifecycle is explicitly immutable — rejected if present in `updates` and different from current)* | `Operation.UPDATE` (HUMAN, ADMIN, AI_AGENT) | blocks `verification: 'verified'` escalation | `_validate_note()` (schema + the same transition table, but unreachable for `lifecycle` since it's immutable here) | `audit_event('update', ...)` (or `valid_until_update`) |
| `archive()` | `{ACTIVE, REVIEW}` -> `ARCHIVED` (this pass's F-02 fix; previously: unrestricted) | `Operation.ARCHIVE` (HUMAN, ADMIN); archiving a verified ACTIVE note additionally requires ADMIN | requires non-empty `reason` | **Does NOT call `_validate_note()`.** New hand-written lifecycle-set check (this pass) | `audit_event('archive', ...)` incl. `previous_lifecycle`/`new_lifecycle` (this pass) |
| `supersede()` | old: current lifecycle -> `SUPERSEDED`; new: unchanged (its own current lifecycle, typically `REVIEW`/`ACTIVE`, is NOT itself required to become `ACTIVE` by this method) | `Operation.SUPERSEDE` (HUMAN, ADMIN, AI_AGENT) | `SupersessionEnforcer.validate_supersession()`: rejects self-supersession, cycles, already-SUPERSEDED predecessor, and AI_AGENT superseding a human-verified predecessor | **Does NOT call `_validate_note()`.** Delegates state-machine-adjacent checks entirely to `SupersessionEnforcer` (a fourth, separate rule set) | `audit_event('supersede', ...)` + `audit_event('archive_superseded', ...)` |
| `_validate_note()` (helper) | declares a full `RAW->CLASSIFIED->NORMALIZED->REVIEW->VERIFIED->ACTIVE->{SUPERSEDED,ARCHIVED}` transition table | n/a (internal helper) | n/a | is itself the validation | n/a |

**Key finding**: `_validate_note()`'s transition table is called by exactly
two of seven mutating methods (`propose()`, `update()`) and, of those,
`update()` makes `lifecycle` immutable before the table could ever fire, and
`propose()` only has an `old_note` to compare against when re-proposing an
existing id (rare). **`review()`, `promote()`, `archive()`, `supersede()` —
four of the seven — never call it at all.** Each has hand-rolled logic
instead. See `LIFECYCLE_SINGLE_SOURCE_OF_TRUTH.md` for the full duplication/
contradiction analysis.

## B. `cognitive_core/tool_router.py` — `ToolRouter`

No independent lifecycle policy. Every action (`update`, `archive`,
`supersede`, `propose`, `read`, `search`) is a direct passthrough to the
corresponding `MemoryController` method after two additional, ToolRouter-only
gates: (1) `RiskLevel` (`delete_canonical`/`modify_raw_imports` always
`HIGH` -> blocked outright, never reach the controller), (2)
`_check_knowledge_reconciliation_boundary()` (blocks `update`/`archive`/
`supersede` on any note with `verification == 'verified'`, for ANY
principal, reading `controller.storage.get()` directly rather than
`controller.read()`). Confirmed empirically
(`test_tool_router_reconciliation_boundary.py`,
`test_adversarial_p0_p15_invariants.py`): ToolRouter can only be **more**
restrictive than `MemoryController`, never less — see section 10 analysis
below.

## C. `memory_controller/mutation_gate.py` — `MutationGate`

A separate, correctly-designed authorization layer for CONFLICT-RESOLUTION
verdicts (from `AuthorizedVerdict`/`Verdict`, an unrelated concern to a
note's own lifecycle). Its own `ReviewState` enum (`OPEN` ->
`EVIDENCE_PENDING` -> `VERIFIED` -> `DECISION_PENDING` -> `APPROVED`/
`REJECTED` -> `CLOSED`) is a **workflow-tracking state machine, not the note
lifecycle** — do not confuse the two. `MutationGate.apply()` never touches
`note['lifecycle']` or `storage.set()` directly; it exclusively delegates to
`controller.supersede()`, `controller.attest()`, `controller.archive()`.
**Not a bypass** — correctly routed through the single controller entry
point.

## D. `cognitive_core/consolidation.py` — `Consolidator` — **HIGH: production bypass**

| Method | Old state -> New state | Authorization | Verification requirement | Validation path | Audit path |
|---|---|---|---|---|---|
| `challenge()` | `{ACTIVE, VERIFIED, "CANONICAL"}` -> `RECONSOLIDATING` | **NONE.** `principal` param is optional, defaults to `AI_AGENT`, and is never passed to any authorization check | none | **NONE.** Reads via `self.controller.storage.get()`, mutates the dict in place, writes via `self.controller.storage.set()` directly | `audit_event('reconsolidation_challenge', ...)` — audited, but the audit log records an action that was never authorized or validated in the first place |
| `resolve_challenge()` | `RECONSOLIDATING` -> `ACTIVE` (if `resolved_node` provided, **also overwrites `content` and `relations` with caller-supplied values**) or `RECONSOLIDATING` -> `REVIEW` | **NONE**, same as above | **NONE** — unlike `promote()`, this ACTIVE-bound transition never checks `verification == 'verified'` | **NONE** | `audit_event('reconsolidation_resolved', ...)` |
| `consolidate_lessons()` | *(new node)* -> `REVIEW`; old `lesson` nodes -> `ARCHIVED` | Correctly routed: `self.router.execute(principal, "propose", ...)` and `self.router.execute(principal, "archive", ...)` | Inherits `propose()`/`archive()`'s normal enforcement | Inherits `MemoryController`'s normal validation | Inherits `MemoryController`'s normal audit | **Not a bypass.** |

**`challenge()`/`resolve_challenge()` together are a complete, unauthenticated
path to rewrite an ACTIVE note's `content` and push it back to `ACTIVE`,
skipping `propose()`'s provenance/lifecycle gates, `review()`, `attest()`'s
verification gate, and `promote()`'s verification requirement entirely.**
This is exactly the kind of "production bypass" flagged as `HIGH` by the
prior pass's own criterion (section 9). It was not in scope of that pass
(which covered `memory_controller/**` and `cognitive_core/tool_router.py`
specifically) and is not fixed in this pass either — see
`LIFECYCLE_SINGLE_SOURCE_OF_TRUTH.md` for why fixing it requires an
architecture decision (what authorization/evidence should
challenge/resolve actually require?) rather than a mechanical patch.

## E. `memory_controller/financial_query.py` — `FinancialQueryEngine.ingest_financial_note()` — MEDIUM: parallel creation path

- **Old state -> New state**: *(none)* -> `REVIEW` (hardcoded literal, not
  caller-controlled).
- **Authorization**: **NONE.** No `Principal` parameter exists on this
  method at all.
- **Verification requirement**: none enforced; caller-supplied
  `verification` field is trusted as-is (defaults to `unverified`).
- **Validation path**: `jsonschema.validate(note, FINANCIAL_NOTE_SCHEMA)` — a
  **different schema** from the canonical `validation/schema.py` one used by
  `MemoryController`. `lifecycle` and `provenance.source_type` ("execution")
  are hardcoded literals in this method, not read from caller input, which
  limits the blast radius (a caller cannot inject an arbitrary lifecycle or
  provenance here) — but the note still enters storage without ever passing
  through `MemoryController._check_auth()` or `_validate_note()`.
- **Audit path**: **NONE.** No `audit_event()` call anywhere in this method.

## F. `memory_controller/financial_ingestion.py` — `_persist_note()` — HIGH: parallel creation path, caller-influenced lifecycle

- **Old state -> New state**: *(none)* -> `fm.get("lifecycle", "REVIEW")` —
  **unlike E, this trusts the caller/extraction-pipeline-supplied
  `lifecycle` value if present**, only defaulting to `REVIEW` when absent.
  `verification` similarly defaults to `"partially_verified"` (not
  `"unverified"`) if the source frontmatter doesn't specify one.
- **Authorization**: **NONE.** No `Principal` parameter, no auth check.
- **Verification requirement**: none.
- **Validation path**: none visible in `_persist_note()` itself (schema
  validation, if any, would need to happen upstream in whatever produces
  `fm`/`note["frontmatter"]` before this method is called — not confirmed
  present in this file).
- **Audit path**: **NONE.**
- This is the **highest-blast-radius** of the two financial paths: if the
  upstream extraction step (financial document ingestion, plausibly
  LLM-assisted per the module's purpose) can be influenced to produce a
  frontmatter dict with `lifecycle: "ACTIVE"`, this method would persist it
  as ACTIVE with zero controller-level gate, zero attestation, zero audit
  trail.

## G. Storage engines (`StorageEngine`, `FileStorageEngine`, `SQLiteStorageEngine`) — read-side RAW filtering only

None of the three storage engines mutate `lifecycle` themselves. Their
`query()` methods each independently hardcode `lifecycle != RAW` exclusion
(three separate implementations of the same rule — see cross-storage
equivalence section in `LIFECYCLE_SINGLE_SOURCE_OF_TRUTH.md`). `set()`/`get()`
are pure storage primitives with no lifecycle-awareness of their own (by
design — the controller is supposed to be the only thing deciding what
lifecycle value is valid to write).

## H. Read-only references (confirmed NOT mutation sites)

`cognitive_core/working_memory.py:168`, `memory_controller/temporal_controller.py:72,88`,
`cognitive_core/learning.py:67` — all only ever *read* `note.get("lifecycle")`
as a filter/comparison condition, never assign it. `learning.py`'s
`LearningEngine.promote_memories()` mutates `confidence`/`verification`
(never `lifecycle`) exclusively through `self.router.execute(principal,
"update", ...)` — correctly routed, not a bypass.

## I. CLI surfaces

`cognitive_core/recall_cli.py` is the only CLI file referencing `lifecycle`,
and only as a read/filter parameter passed through to
`MemoryController.search()` (consistent with the `I-RETRIEVAL` invariant —
see `07_EVALUATION/security_invariant_nomenclature_2026-09.md`). No CLI in
this repository writes to `lifecycle`.

## J. Test fixtures (documented, not itemized)

Dozens of tests across `memory_controller/tests/` and `cognitive_core/tests/`
construct notes with an arbitrary `lifecycle` via `storage.set(...)` or a
plain dict literal, deliberately bypassing `MemoryController` to set up an
isolated starting state for a unit test (e.g.
`test_authorization.py::test_human_promote_allowed` seeds `verification:
"verified"` directly rather than calling `attest()`, exactly as this session's
own prior work did in F-07). **This is the correct and expected use of a test
fixture** — it is not evidence of a production bypass. It is listed here only
so that a future search for `storage.set(` does not need to be re-triaged
from scratch: everything under `*/tests/*.py` in this table's scope was
individually reviewed and confirmed to be fixture setup, with the single
class of exception being the direct calls documented in sections A-F above,
which are non-test production code.

---

## Summary: how many independent sources of lifecycle truth exist?

**At least five, empirically:**

1. `_validate_note()`'s declared transition table (barely reachable, see A).
2. `review()`'s own hand-written acceptance set.
3. `promote()`'s own hand-written 2-condition check (lifecycle==REVIEW AND
   verification==verified).
4. `archive()`'s own hand-written acceptance set (this pass's addition).
5. `SupersessionEnforcer.validate_supersession()`'s own rule set (self/cycle/
   already-superseded/AI-vs-human-verified) — logically adjacent to but
   textually separate from the other four.

Plus **two informal, unauthenticated creation-time policies** (E, F) that
never consult any of the five above, and **one confirmed unauthenticated
transition bypass** (D) that ignores all five entirely.

Full analysis of what's duplicated, what's contradictory, and what a single
`is_transition_allowed(old_state, new_state, principal, note)` function
would need to reconcile: `LIFECYCLE_SINGLE_SOURCE_OF_TRUTH.md`.
