---
id: "7c017d07-9aa2-4c6b-8daa-9e47dc43992d"
type: system
lifecycle: REVIEW
category: implementation-contract
tags: [phase-4-3, p0-security, implementation-contract, unverified]
created: "2026-08-10"
updated: "2026-08-10"
provenance:
  source_type: ai
  source_ref: "perplexity-implementation-contract-session-2026-08-10"
confidence: medium
verification: unverified
relations:
  - relation: related_to
    target: system
    target_id: "bf75bcc6-1de0-4be0-9d13-db06f93df7b8"
---

# Phase 4.3 P0 Security Hardening — Implementation Contract

## Status of this document

This is an AI-authored IMPLEMENTATION CONTRACT, not an implementation. No production code, test file, `Continuity_Handoff.md`, `Memory_Protocol.md`, or `Rules.md` was modified to produce this document. This contract is derived from direct source reading at HEAD `87f9c70c47fbaf74cb69e70ea6caaf32a275f970` (confirmed unchanged from all prior sessions' reads via matching blob SHAs). No pytest execution is claimed. No runtime verification is claimed. This document exists so that an execution-capable agent (e.g. Antigravity) can implement the P0 fix without re-deriving the analysis from scratch.

## PART 1 — Current Code Path (traced from actual source)

```
CALLER (any Principal)
  |
  v
MemoryController.propose(principal, note_data)      [memory_controller/controller.py]
  |
  |-- self._check_auth(principal, Operation.PROPOSE)
  |     -> DefaultAuthorizer.is_allowed()             [memory_controller/authorizer.py]
  |     -> checks ONLY: "is this principal allowed to call propose() at all?"
  |     -> {HUMAN, AI_AGENT, ADMIN} are ALL allowed for PROPOSE
  |     SECURITY EFFECT: coarse-grained, operation-level only. No field-level check.
  |
  |-- check_path_traversal(note_id)                   [memory_controller/security/utils.py]
  |     SECURITY EFFECT: prevents note_id path escape. Unrelated to trust fields.
  |
  |-- note construction:
  |     defaults = {..., 'verification': 'unverified', 'lifecycle': RAW, 'provenance': {'source_type':'user',...}, ...}
  |     note = defaults.copy()
  |     note.update(note_data)                         <-- ROOT CAUSE, see Part 2
  |     prov = defaults['provenance'].copy()
  |     prov.update(note_data.get('provenance', {}))    <-- ROOT CAUSE (provenance-specific)
  |     note['provenance'] = prov
  |     note['id'] = note_id
  |     SECURITY EFFECT: NONE — this is exactly where the vulnerability lives.
  |
  |-- self._validate_note(validation_note)
  |     -> validate_frontmatter(validation_note)        [memory_controller/validation/schema.py]
  |          -> Draft7Validator against _CANONICAL_SCHEMA
  |          -> checks ONLY that 'lifecycle' in enum[...], 'verification' in enum[...],
  |             'provenance.source_type' in enum[...] -- i.e. VALUE IS A VALID TYPE, not
  |             VALUE IS PERMITTED FOR THIS PRINCIPAL.
  |     -> validate_provenance(validation_note['provenance']) [memory_controller/validation/provenance.py]
  |          -> checks ONLY that 'source_type' and 'source_ref' KEYS are present.
  |     -> transition check: old_note = storage.get(note_id); if old_note: ... (skipped when None)
  |          SECURITY EFFECT: for NEW notes, lifecycle transition table NEVER RUNS.
  |
  |-- self.storage.set(note_id, note)                  [memory_controller/storage/file_engine.py OR StorageEngine]
  |     PERSISTENCE EFFECT: canonical write. FileStorageEngine.set() IS atomic (tempfile+os.replace).
  |     StorageEngine.set() (in-memory dict test double) is trivially atomic.
  |     Neither performs ANY additional security check.
  |
  |-- self.cache.invalidate_by_event('memory_updated')
  |
  |-- audit_event('propose', principal, note_id, success=True)  [memory_controller/audit/logger.py]
  |     PERSISTENCE EFFECT: appends one JSONL line. Correctly records the TRUE calling principal
  |     (principal.value, not caller-suppliable) -- this field is NOT part of the vulnerability.
  |
  v
RETURN note_id
```

Equivalent trace exists for `update()`, differing only in that it operates on an existing note and applies an `immutable = {'id', 'lifecycle'}` guard (which does NOT include `verification` or `provenance`) before calling the same `note.update(updates)` / `_validate_note()` / `storage.set()` sequence.

`archive()` and `supersede()` do not exhibit this vulnerability: `archive()` only ever sets `lifecycle=ARCHIVED` and `archive_reason` from its own explicit parameters, never from an arbitrary payload dict; `supersede()` only ever sets `lifecycle`, `supersedes`/`superseded_by`, and `relations` fields it constructs itself, never copying `verification`/`provenance` between notes (confirmed by direct read, and independently re-confirmed in the prior forensic validation's threat-model row "Supersession trust transfer" -> classified as safe).

## PART 2 — Root Cause Analysis

**The exact mechanism**: `note = defaults.copy(); note.update(note_data)` performs an unconditional, unfiltered dict merge. Any key present in `note_data` (the caller's payload) silently overwrites the corresponding default, with zero regard for which `principal` is calling. The identical pattern repeats for `provenance`: `prov = defaults['provenance'].copy(); prov.update(note_data.get('provenance', {}))`.

**Trusted fields** (should never be caller-suppliable, verified by direct read of what SHOULD be server-derived per the schema's own intent): `created`, `updated` (timestamps — schema requires them but doesn't say who sets them; current code sets sane defaults but then lets `note.update()` overwrite them if present in payload), `lifecycle` (state-machine controlled), `verification` (trust-attestation controlled).

**Untrusted fields** (legitimately caller-suppliable, no change needed): `content`, `category`, `tags`, `type`, `confidence` (a self-assessed opinion field, not itself a trust-escalation vector since `authority.py` never reads `confidence`), `relations`, `valid_from`/`valid_until`, `version_range`, `applies_to`.

**Fields currently accepted from caller with zero gating** (confirmed by source read, this session and prior): `verification`, `provenance.source_type`, `provenance.source_ref`, `lifecycle`, `created`, `updated`.

**Where the trust boundary is violated**: inside `MemoryController.propose()` and `MemoryController.update()`, at the exact two lines quoted above. This is the ONLY location; `_check_auth()` runs before this merge and is operation-level (not field-level), and `_validate_note()` runs after this merge and is type-level (not principal-level) — neither compensates for the merge itself.

**Does the same problem exist in `archive()`/`supersede()`/hypothetical `attest()`?**
- `archive()`: NO — confirmed, it never merges an arbitrary caller dict into the note; it sets exactly two fields (`lifecycle`, `archive_reason`) from its own named parameters.
- `supersede()`: NO — confirmed, same reasoning; it constructs its own field updates, never a raw merge of caller-supplied fields.
- `attest()`: DOES NOT EXIST in the current codebase. Confirmed by `search_code` in a prior session: no `def attest` anywhere in `memory_controller/controller.py` or elsewhere. This is Part 5's specification target, not an existing vulnerable path.

## PART 3 — Field Ownership Model

| FIELD | CALLER MAY SUPPLY | SERVER CONTROLS | PRINCIPAL SCOPED | MUTABLE (post-creation) | SECURITY SENSITIVE | VALIDATION (current) |
|---|---|---|---|---|---|---|
| `id` | YES (at creation only) | Frozen after creation | No | NO (`immutable` set in `update()`) | Low (identity, not trust) | schema `format: uuid` |
| `content` | YES | No | No | YES | No | none (stripped before schema validation) |
| `type` | YES | No | No | YES (not in `immutable` set — a latent gap, out of P0 scope) | Low | schema enum |
| `category` | YES | No | No | YES | No | schema `type: string` |
| `tags` | YES | No | No | YES | No | schema array of strings |
| `created` | **Currently YES (bug)** | **Should be YES, always** | No | **Should be NO** | **Medium (audit-adjacent)** | schema `format: date` only |
| `updated` | **Currently YES (bug)** | **Should be YES, always** | No | **Should be NO for caller; server sets on every write** | **Medium** | schema `format: date` only |
| `lifecycle` | **Currently YES at creation (bug — this is OMEGA-006)** | **Should be YES at creation (always RAW)** | No | NO already (in `immutable` set for `update()`) | **HIGH** | schema enum only; transition table skipped for new notes |
| `verification` | **Currently YES (bug — this is OMEGA-001)** | **Should be YES for the "verified" value specifically** | No (but should gate WHO can request "verified") | **Currently YES via update() (bug)** | **HIGH** | schema enum only |
| `provenance.source_type` | **Currently YES (bug — this is OMEGA-002)** | **Should be principal-gated** | **YES (should be)** | **Currently YES via update() (bug)** | **HIGH** | schema enum only |
| `provenance.source_ref` | YES | No | No | YES | Low (a citation string, not a trust claim) | schema `type: string` |
| authority (not a stored field) | N/A | N/A (computed at read-time from `provenance.source_type`) | N/A | N/A | Derived, not directly attackable — attack surface is entirely upstream at `source_type` | `authority.py::get_authority_score`, pure function |
| `relations` | YES | No | No | YES (append-style in practice) | Low-Medium (structural, not itself a trust field) | schema array-of-object shape only |

This table is built strictly from the actual `_CANONICAL_SCHEMA` in `memory_controller/validation/schema.py` and the actual field-handling code in `controller.py` — no fields were invented.

## PART 4 — Principal / Provenance Matrix

Actual supported `provenance.source_type` enum values, verified directly from `_CANONICAL_SCHEMA['properties']['provenance']['properties']['source_type']['enum']`:

```
["user", "official", "execution", "experience", "ai", "inference", "import", "unknown"]
```

Actual current AI-authored production usage of these values, verified by direct read of every AI_AGENT-driven write path in `cognitive_core/`:
- `reflection.py` (`_learn_from_error`, `_learn_from_blocked`): `provenance: {"source_type": "inference"}`
- `consolidation.py` (`consolidate_lessons`): `provenance: {"source_type": "inference", "source_refs": [...]}`
- `deduplication.py` (`scan_for_duplicates`): `provenance: {"source_type": "inference", "source_ref": "deduplicator"}`

No AI-driven production code path currently uses `"ai"` or `"execution"` as a literal string, but both are valid schema enum members and are the semantically appropriate tiers for, respectively, a direct first-person AI claim and a tool-execution-observed result.

| source_type | AI_AGENT | HUMAN | ADMIN | Notes |
|---|---|---|---|---|
| `user` | **DENY** | ALLOW | ALLOW | Currently un-gated (bug: AI_AGENT can claim this today) |
| `official` | **DENY** | CONDITIONAL (per Rules.md's human-sourcing intent, plausible but not code-enforced today) | ALLOW | Currently un-gated (bug: AI_AGENT can claim this today) |
| `execution` | ALLOW | CONDITIONAL | ALLOW | Legitimate for AI reporting a genuinely observed tool-execution result |
| `experience` | **DENY** | ALLOW | CONDITIONAL | First-person human experience; not an AI concept in current schema usage |
| `ai` | ALLOW | **DENY** | CONDITIONAL | Reserved for genuine AI-authored claims |
| `inference` | ALLOW | UNKNOWN (not currently used by HUMAN paths) | UNKNOWN | Confirmed actively used by `reflection.py`/`consolidation.py`/`deduplication.py` |
| `import` | **DENY** | UNKNOWN | ALLOW | Reserved for the (currently unmodeled) import pipeline; excluded from AI_AGENT scope |
| `unknown` | ALLOW (fallback) | ALLOW (fallback) | ALLOW (fallback) | Schema default when `source_type` absent; `authority.py` treats this as the lowest tier (0.2) |

**What AI_AGENT must be prevented from claiming**: `user`, `official`, `experience`, `import` — none of these currently have any code-level gate; all four are OMEGA-002-exploitable today.

## PART 5 — Verification Model

Actual current verification states, verified directly from `_CANONICAL_SCHEMA['properties']['verification']['enum']`:

```
["verified", "partially_verified", "unverified", "inferred"]
```

- **Initial state** at `propose()`: `"unverified"` (the default; currently overwritable by caller — this is OMEGA-001).
- **`partially_verified`**: currently produced ONLY by `cognitive_core/learning.py::LearningEngine.promote_memories`, via `self.router.execute(principal, "update", {"note_id": node["id"], **updates})` where `updates["verification"] = "partially_verified"` under specific graph-density conditions (`confidence == "medium" and len(relations) >= self.promotion_threshold * 2`). This path is legitimate and MUST continue working.
- **`verified`**: currently reachable by ANY principal via `propose()` or `update()` with zero gating (OMEGA-001). No dedicated attestation mechanism exists anywhere in the codebase (confirmed absent by `search_code` for `def attest` in a prior session).
- **`inferred`**: a valid schema value; not currently written by any production code path found in this repository (available for future use, not part of P0 scope).
- **Is verification mutable?** YES, unconditionally, via `update()` today (not in the `immutable` set) — this is the `update()`-path variant of OMEGA-001.
- **Can `update()` escalate verification?** YES today, confirmed — this must be closed as part of the same fix.

**Minimum safe model (specification only, not implemented here)**: Because no attestation mechanism exists in the current repository, one minimal new method (`attest()`) must be added to `MemoryController` — this is not a duplicate of anything; it is the first and only such mechanism. `propose()`/`update()` must reject `verification="verified"` unconditionally for ALL principals (not just AI_AGENT — per the human-approved decision recorded in this engagement's prior session, even ADMIN must go through `attest()`, to keep a clean, auditable separation between "content creation" and "trust assertion" as distinct events in time). `attest()` itself must be restricted to `{HUMAN, ADMIN}` via a new `Operation.ATTEST` entry in `authorizer.py`'s policy dict (a one-line, additive change — it does not touch any existing operation's policy).

## PART 6 — Lifecycle Model

Actual current lifecycle FSM, verified directly from `Lifecycle(str, enum.Enum)` in `controller.py` and the `allowed = {...}` transition table inside `_validate_note`:

```
RAW -> CLASSIFIED -> NORMALIZED -> REVIEW -> VERIFIED -> ACTIVE -> {SUPERSEDED, ARCHIVED}
```

- **States**: exactly the 8 enum members above.
- **Transitions**: enforced ONLY when `old_note` exists (i.e., only on `update()` of a pre-existing note). Owners of each transition are not separately gated by principal in the transition table itself — `review()` and `promote()` ARE separately principal-gated via `Operation.REVIEW`/`Operation.PROMOTE` (`{HUMAN, ADMIN}` only, confirmed in `authorizer.py`), but this gating is IRRELEVANT to the vulnerability because `propose()` can skip the entire pipeline by setting `lifecycle="ACTIVE"` directly on a brand-new note, where `old_note is None` and the transition table never runs at all.
- **Persistence**: `storage.set()`, identical for every lifecycle value; no special-casing.

**Minimum correction (specification only)**: `propose()` must unconditionally force `lifecycle = Lifecycle.RAW.value` on every new note, discarding any caller-supplied `lifecycle` value at creation time, before `_validate_note()` runs. This does not touch `review()`, `promote()`, `update()`'s existing lifecycle-immutability guard, or the transition table itself — it only removes the ability to inject an initial state other than RAW.

## PART 7 — Authority Model

`memory_controller/authority.py` in full (re-confirmed unchanged this session via SHA match):

```python
_SOURCE_AUTHORITY_MAP = {
    "user": 0.5, "official": 0.9, "execution": 0.7, "experience": 0.6,
    "ai": 0.4, "inference": 0.3, "import": 0.8, "unknown": 0.2,
}
def get_authority_score(note: dict) -> float:
    provenance = note.get('provenance', {})
    source_type = provenance.get('source_type', 'unknown')
    return _SOURCE_AUTHORITY_MAP.get(source_type, _SOURCE_AUTHORITY_MAP['unknown'])
```

**Verdict: `authority.py` itself is NOT broken.** It is a pure, deterministic function correctly computing a score FROM `provenance.source_type`. The vulnerability is entirely upstream: since OMEGA-002 allows any principal to write `source_type="official"`, `get_authority_score()` will correctly (and therefore dangerously) return `0.9` for a note that has no legitimate claim to that tier. **`authority.py` must NOT be modified.** The fix belongs exclusively at the point where `source_type` is written (Part 2/5's target), not at the point where it is read.

## PART 8 — Update Security Matrix

| Operation | AI_AGENT | HUMAN | ADMIN | Evidence |
|---|---|---|---|---|
| create (`propose`) | ALLOW | ALLOW | ALLOW | `authorizer.py` `_policy[PROPOSE]` |
| update content | ALLOW (if lifecycle in {RAW,CLASSIFIED,NORMALIZED}) | ALLOW (if ACTIVE) | ALLOW | `controller.py::update`'s lifecycle-gate `if`/`else` |
| update verification | **CURRENTLY ALLOW for all three (BUG)** | **CURRENTLY ALLOW (BUG)** | **CURRENTLY ALLOW (BUG)** | `verification` absent from `immutable` set |
| update provenance | **CURRENTLY ALLOW for all three (BUG)** | **CURRENTLY ALLOW (BUG)** | **CURRENTLY ALLOW (BUG)** | `provenance` absent from `immutable` set |
| update lifecycle | DENY (already correctly blocked) | DENY (already correctly blocked) | DENY (already correctly blocked) | `lifecycle` IS in the `immutable` set already — this one field is already safe |
| attest | N/A (does not exist) | N/A (does not exist) | N/A (does not exist) | confirmed absent |
| archive | DENY (not in `_policy[ARCHIVE]`) | ALLOW | ALLOW | `authorizer.py` `_policy[ARCHIVE] = {HUMAN, ADMIN}` |
| supersede | ALLOW (unless target is human-verified) | ALLOW | ALLOW | `authorizer.py` `_policy[SUPERSEDE]`; `SupersessionEnforcer` for the verified-target exception |

No policy was invented here; every cell traces to an actual line of code cited above.

## PART 9 — Direct Controller Attack Model

Assuming an attacker calls `MemoryController.propose()` directly, bypassing `Executive`/`ToolRouter` entirely (this is the correct threat model per Instruction — the security boundary must not depend on those layers):

| Attack | Currently succeeds? |
|---|---|
| Verification escalation (`verification="verified"`) | **YES — succeeds today** |
| Provenance escalation (`source_type="official"`/`"user"`) | **YES — succeeds today** |
| Lifecycle escalation (`lifecycle="ACTIVE"`) | **YES — succeeds today** |
| Authority escalation (indirect, via provenance) | **YES — succeeds today, as a consequence of provenance escalation** |
| Timestamp forgery (`created`/`updated`) | **YES — succeeds today** (same unconditional overlay) |
| Relation manipulation (arbitrary `relations` array at creation) | Not classified as a P0 finding; relations are structural, not trust-bearing, and are explicitly OUT OF SCOPE for this contract |

All five in-scope attacks must be blocked as part of this fix; none require touching `Executive` or `ToolRouter` — the fix belongs entirely inside `MemoryController`.

## PART 10 — Storage / Persistence Requirement

Required guarantee: **validation/rejection must occur BEFORE any call to `storage.set()`.** Currently, `_validate_note()` (schema/provenance/transition checks) already runs before `storage.set()` in both `propose()` and `update()` — so the correct INSERTION POINT for the new security checks is immediately after note construction (the `note.update(note_data)` merge) and immediately before `self._validate_note(validation_note)` / `self.storage.set(note_id, note)`. This preserves the existing all-or-nothing structure: if the new checks raise, execution falls into the existing `except Exception as e:` block, which calls `audit_event(..., success=False, ...)` and re-raises — `storage.set()` is never reached, so **no partial persistence is possible** by construction, provided the new checks are placed strictly before the existing `self.storage.set(note_id, note)` line. No change to `StorageEngine`, `FileStorageEngine`, or `serializer.py` is required or permitted under this contract.

## PART 11 — Adversarial Test Contract

| Test ID | File | Setup | Attack | Expected Exception | Expected Storage State | Expected Audit Event | Pass Condition |
|---|---|---|---|---|---|---|---|
| P0-001 | `memory_controller/tests/test_security_hardening.py` (new file) | Fresh `StorageEngine`/`MemoryController` | `propose(AI_AGENT, {..., "verification":"verified"})` | A `ValueError` subtype (raised before persistence) | `storage.get(note_id) is None` | `propose` event with `outcome="error"` | Exception raised AND storage empty for that id |
| P0-002 | same | same | `propose(AI_AGENT, {..., "provenance":{"source_type":"official",...}})` | same | same | same | same pattern |
| P0-003 | same | same | `propose(AI_AGENT, {..., "provenance":{"source_type":"user",...}})` | same | same | same | same pattern |
| P0-004 | same | same | `propose(AI_AGENT, {..., "lifecycle":"ACTIVE"})` | Either rejected OR silently forced to RAW (implementer's choice per Part 6 — if forced, not rejected, assert `storage.get(note_id)["lifecycle"] == "RAW"` instead of an exception) | Note exists but with `lifecycle == "RAW"` | `propose` event with `outcome="success"` (if force-and-continue) | `lifecycle` is `RAW`, never `ACTIVE`, regardless of exception vs. force strategy |
| P0-005 | same | Pre-existing RAW note owned by AI_AGENT | `update(AI_AGENT, note_id, {"verification":"verified"})` | `ValueError`/`SecurityViolationError` | Note's `verification` unchanged from before the call | `update` event with `outcome="error"` | Exception raised AND field unchanged |
| P0-006 | same | same | `update(AI_AGENT, note_id, {"provenance":{"source_type":"official"}})` | same | Note's `provenance.source_type` unchanged | same | same |
| P0-007 | same | same | `update(AI_AGENT, note_id, {"lifecycle":"ACTIVE"})` | `ValueError` (already correctly blocked today via the `immutable` set — this is a REGRESSION test, not a new fix) | unchanged | `update` event with `outcome="error"` | Confirms pre-existing protection still works after the patch |
| P0-008 | same | same as P0-001 | Call `MemoryController.propose()` directly, with no `Executive`/`ToolRouter` in the call stack at all | same as P0-001 | same as P0-001 | same | Confirms the fix lives in `MemoryController`, not a wrapper |
| P0-009 | `cognitive_core/tests/test_tool_router_security.py` (new file, or extend existing) | Real `ToolRouter` wrapping a real `MemoryController` | `router.execute(AI_AGENT, "propose", {"note_data": {..., "verification":"verified"}})` | same as P0-001, propagated through `ToolRouter.execute` | same | same | Confirms the fix is not bypassable via the Cognitive Core's normal call path either |
| P0-010 | same as P0-001 | Pre-existing REVIEW-lifecycle note | `attest(HUMAN, note_id, verification_reason="manual review", evidence_reference="doc-123")` | None (should succeed) | `storage.get(note_id)["verification"] == "verified"` | New `attest` event type with `attested_by`, `reason`, `evidence_reference`, `previous_verification_state`, `new_verification_state` | Attestation succeeds and is durably persisted |
| P0-011 | same | same | `attest(ADMIN, note_id, ...)` | None (should succeed) | same | same | Same as P0-010 for ADMIN |
| P0-012 | `cognitive_core/tests/test_learning.py` (existing file — extend, do not replace) | Existing `LearningEngine.promote_memories` test setup | Trigger the existing `medium -> high confidence, verification=partially_verified` promotion path | None | Note's `verification == "partially_verified"` | `update` event, `outcome="success"` | Confirms the LEGITIMATE existing path is NOT broken by the P0 fix |
| P0-013 | same as P0-001 | same | Any of P0-001/002/003/005/006 | (as above) | **Explicit assertion**: `len(storage.store) == 0` (or count unchanged from before the call for the update-variants) — not merely "an exception was raised" | (as above) | Storage state explicitly inspected, per the instruction's emphasis on testing actual persisted state, not just exception presence |
| P0-014 | new, using `FileStorageEngine` with a real temp directory (mirrors the pattern already used in `test_supersession_phase43.py`) | Create a note via `attest()`-backed legitimate flow, then instantiate a SECOND `FileStorageEngine`/`MemoryController` pair pointed at the same temp directory (same pattern as the existing `test_supersession_atomicity_and_persistence`'s "restart verification" section) | Re-read the note via the second controller instance | None | `verification == "verified"` and `provenance.source_type` unchanged after reload | N/A (read-only) | Confirms legitimately-attested trust survives a simulated restart; NOTE: this remains a same-process re-instantiation, consistent with the existing repository's testing pattern — it does NOT constitute a genuine multi-process test, and must not be described as one |
| P0-015 | same as P0-001 | Two notes, one attested `verified`, superseded by a fresh RAW/unverified note | `supersede(ADMIN, old_id, new_id)` | None | New note's `verification` remains whatever it was BEFORE supersession (i.e., still `unverified`, NOT inherited as `verified`) | `supersede` + `archive_superseded` events | Explicit assertion that trust is not transferred, closing the gap the prior forensic validation flagged as "tested but not explicitly asserted" |

## PART 12 — Regression Protection

Explicitly required to remain unbroken, verified against actual current source:

- `cognitive_core/reflection.py`: `_learn_from_error`/`_learn_from_blocked` propose notes with `provenance: {"source_type": "inference"}`, `verification: "unverified"`, `lifecycle: Lifecycle.REVIEW.value`. **This is a genuine pre-existing edge case the implementer must handle: `reflection.py` currently proposes NEW notes directly at `lifecycle="REVIEW"`, skipping RAW/CLASSIFIED/NORMALIZED.** If Part 6's fix unconditionally forces `lifecycle=RAW` on ALL new proposals with no exception, this would break `reflection.py`'s legitimate behavior. **This must be flagged explicitly to the implementer: either (a) the RAW-forcing rule needs a narrow, explicit exception for system-internal reflection/consolidation/deduplication proposals (which already only ever request RAW-or-REVIEW, never ACTIVE), or (b) the fix should specifically block ONLY `lifecycle="ACTIVE"`/`"VERIFIED"`/`"SUPERSEDED"`/`"ARCHIVED"` at creation, while still permitting `RAW`, `CLASSIFIED`, `NORMALIZED`, and `REVIEW` as legitimate non-escalated starting points.** Option (b) is the smaller, safer patch and is RECOMMENDED — it precisely targets OMEGA-006 (reaching ACTIVE, the state that grants read-visibility and update eligibility for HUMAN/ADMIN) without breaking the existing REVIEW-at-creation pattern already used by `reflection.py`, `consolidation.py`, and `deduplication.py`.
- `cognitive_core/consolidation.py`: proposes at `lifecycle: Lifecycle.REVIEW.value`, `provenance: {"source_type": "inference", ...}` — same consideration as above.
- `cognitive_core/deduplication.py`: proposes at `lifecycle: Lifecycle.REVIEW.value`, `provenance: {"source_type": "inference", "source_ref": "deduplicator"}` — same consideration.
- `cognitive_core/learning.py`: `LearningEngine.promote_memories` calls `update()` with `updates["verification"] = "partially_verified"` — **MUST continue to work**; the fix must allow `"partially_verified"` through `update()` while blocking only `"verified"`.
- `cognitive_core/recall.py`: reads `provenance.source_type` via `get_authority_score()` — read-only, unaffected by this fix.
- Supersession (`memory_controller/validation/supersession.py`): reads `verification`/`provenance.source_type` for its human-verified-protection check — unaffected by this fix's write-side changes; becomes MORE trustworthy as a direct consequence of this fix (P0-015 explicitly tests this).

## PART 13 — Exact Patch Plan

| File | Function | Current Behavior | Problem | Change Required | Why | Regression Risk | Tests Affected |
|---|---|---|---|---|---|---|---|
| `memory_controller/controller.py` | `MemoryController.propose()` | Unconditional `note.update(note_data)` + unconditional `prov.update(...)` | OMEGA-001, OMEGA-002, OMEGA-006, timestamp forgery | After note construction, before `_validate_note()`: (1) force `created`/`updated` to server timestamp, discarding caller values; (2) reject `verification == "verified"` unconditionally (all principals); (3) validate `provenance.source_type` against a principal-scoped allowlist, rejecting non-conforming values; (4) reject `lifecycle` values other than `{RAW, CLASSIFIED, NORMALIZED, REVIEW}` at creation (per Part 12's recommended narrow fix, NOT a blanket force-to-RAW, to preserve `reflection.py`/`consolidation.py`/`deduplication.py`'s existing REVIEW-at-creation pattern) | Closes all three P0 findings at their single root cause | Medium — must not break the three AI-maintenance modules' existing REVIEW-at-creation usage; mitigated by the narrow (not blanket) lifecycle restriction | P0-001 through P0-004, P0-013 |
| `memory_controller/controller.py` | `MemoryController.update()` | `immutable = {'id', 'lifecycle'}`; no gating on `verification`/`provenance` | OMEGA-001 (update variant), OMEGA-002 (update variant) | Add explicit checks: reject `updates["verification"] == "verified"` unconditionally; reject any change to `updates["provenance"]["source_type"]` if it differs from the note's existing value (make `source_type` immutable post-creation for ALL principals); force `updates["updated"]` to server timestamp | Closes the `update()`-path variants of the same root cause | Low — `partially_verified` and non-`source_type` provenance edits (e.g. `source_ref`) remain permitted | P0-005, P0-006, P0-007 (regression), P0-012 |
| `memory_controller/controller.py` | NEW: `MemoryController.attest()` | Does not exist | No legitimate path to `verified` exists | Add a new method: authorize via new `Operation.ATTEST`; require non-empty `verification_reason` and `evidence_reference`; require current `verification != target state` (no-op guard); set `verification`, `verification_source`, `last_verified`, `updated`; persist via existing `storage.set()`; emit a new `attest` audit event with the five required sub-fields | Provides the sole legitimate escalation path, per Part 5 | Low — purely additive, no existing behavior touched | P0-010, P0-011 |
| `memory_controller/authorizer.py` | `Operation` enum, `DefaultAuthorizer._policy` | No `ATTEST` operation | `attest()` needs an authorization hook | Add `Operation.ATTEST = "attest"`; add `_policy[Operation.ATTEST] = {Principal.HUMAN, Principal.ADMIN}` | Enables `attest()`'s authorization check | Zero — purely additive enum/dict entry, does not alter any existing operation's policy | P0-010, P0-011 |
| `memory_controller/authority.py` | `get_authority_score()` | Reads `provenance.source_type`, pure function | None — this file is correct | **NO CHANGE** | Confirmed in Part 7: the bug is entirely upstream | Zero | None |
| `memory_controller/validation/schema.py` | `_CANONICAL_SCHEMA`, `validate_frontmatter()` | Type/enum validation only | None specific to P0 — schema correctly lists valid enum members | **NO CHANGE** | The gap is principal-awareness, which schemas cannot express; this belongs in `controller.py` | Zero | None |
| `memory_controller/validation/provenance.py` | `validate_provenance()` | Checks required keys present | None specific to P0 | **NO CHANGE** | Same reasoning | Zero | None |
| `memory_controller/audit/logger.py` | `audit_event()`, `AuditLogger.log()` | Generic operation/outcome/metadata logging | None specific to P0 (hash-chaining is explicitly deferred, P1) | **NO CHANGE except that `attest()` will call the EXISTING `audit_event()` function with a new `operation="attest"` string and a metadata dict containing the five required sub-fields — this requires zero modification to `logger.py` itself**, since `audit_event()` already accepts arbitrary `details` dicts | N/A | Zero | P0-010, P0-011 |

No other file requires modification. `cognitive_core/*.py` files require ZERO changes — their existing usage patterns (documented in Part 12) are already compliant with the target model once the narrow lifecycle restriction (RAW/CLASSIFIED/NORMALIZED/REVIEW permitted, ACTIVE/VERIFIED/SUPERSEDED/ARCHIVED blocked at creation) is implemented as specified.

## PART 14 — Security Invariants After Patch

| ID | Invariant | Source File | Function | Test ID |
|---|---|---|---|---|
| I-001 | AI cannot self-verify | `controller.py` | `propose()`, `update()` | P0-001, P0-005 |
| I-002 | AI cannot claim privileged provenance | `controller.py` | `propose()` | P0-002, P0-003 |
| I-003 | AI cannot inject an escalated lifecycle at creation | `controller.py` | `propose()` | P0-004 |
| I-004 | Verification escalation requires authorized attestation | `controller.py`, `authorizer.py` | `attest()`, `Operation.ATTEST` | P0-010, P0-011 |
| I-005 | Provenance `source_type` is principal-scoped at creation and immutable thereafter | `controller.py` | `propose()`, `update()` | P0-002, P0-003, P0-006 |
| I-006 | Security fields (`verification`, `provenance.source_type`, `lifecycle`, `created`, `updated`) cannot be arbitrarily overwritten by caller payload | `controller.py` | `propose()`, `update()` | P0-001 through P0-007 |
| I-007 | Rejected payloads never persist (no partial writes) | `controller.py` | `propose()`, `update()` (existing try/except structure, checks placed before `storage.set()`) | P0-013 |
| I-008 | `MemoryController` remains the final security boundary, independent of `Executive`/`ToolRouter` | `controller.py` | `propose()`, `update()` | P0-008, P0-009 |
| I-009 | Legitimate AI provenance (`inference`, `ai`, `execution`) continues working | `controller.py`, `reflection.py`, `consolidation.py`, `deduplication.py` | `propose()` (allowlist), existing Cognitive Core call sites | P0-012 (analogous), full existing suite |
| I-010 | `LearningEngine` cannot manufacture `verified`, only `partially_verified` | `controller.py::update()` | `LearningEngine.promote_memories` | P0-012 |
| I-011 | Supersession does not transfer verification/provenance trust | `controller.py::supersede()` (unchanged, already correct) | `supersede()` | P0-015 |
| I-012 | Restart (same-process re-instantiation, per existing repository test conventions) preserves attested trust state | `storage/file_engine.py` (unchanged), `controller.py::attest()` | `FileStorageEngine.get()`/`set()` | P0-014 |

## PART 15 — Acceptance Criteria

**Code correctness** (verifiable by reading the diff, no execution required): all 14 invariants above are implemented per the Part 13 patch plan; no file outside the six-row patch plan is touched; `authority.py`, `schema.py`, `provenance.py`, `audit/logger.py` remain byte-identical.

**Runtime verification** (requires an execution-capable environment; explicitly NOT claimed by this contract):
1. `test_ai_cannot_self_verify` and P0-001 through P0-015 all pass.
2. Full existing suite (`python -m pytest -q` against the actual repository checkout) passes with no new failures.
3. No unrelated file is modified (`git diff --stat` shows only the six files in Part 13 plus new/extended test files).
4. `git status --short` is clean of scratch files, credentials, or machine-specific paths before commit.

**This contract explicitly distinguishes CODE CORRECTNESS (assessable now, by inspection) from RUNTIME VERIFICATION (requires actual `pytest` execution, which has not occurred and is not claimed here).**

## PART 16 — Deferred Findings (P1/P2/P3 — explicitly OUT OF SCOPE for this contract)

| Finding | Why deferred | Dependency | Risk if deferred | Future phase |
|---|---|---|---|---|
| OMEGA-003 (no multi-agent locking) | Out of scope until a second concurrent agent is actually introduced | None on P0 | Low at current single-author scale | Future "multi-agent coordination" phase |
| OMEGA-004 (checkpoint atomicity, `wm.json`/`plan.json`) | Affects agent-continuity robustness, not Phase 4.3's core trust-boundary scope | None on P0 | Medium — crash mid-write corrupts checkpoint | Agent continuity hardening phase |
| OMEGA-005 (audit tamper resistance / hash chaining) | Explicitly excluded by this task's instructions | None on P0 | Medium — no forensic tamper-detection | Dedicated audit-integrity phase |
| OMEGA-007 (RecallEngine direct storage read bypass) | Read-only, does not affect write-side trust integrity fixed here | None on P0 | Low-Medium — authorization/lifecycle-gate bypass for reads only | Storage-abstraction cleanup phase |
| OMEGA-008 (version algebra inconsistency, Deduplicator vs RecallEngine) | Unrelated to trust boundary; a correctness/ranking issue | None on P0 | Low — functional but semantically inconsistent | Version-algebra unification phase |
| OMEGA-009 (temporal semantics, no as-of queries) | Unrelated to trust boundary | None on P0 | Low — recall ranking only | Temporal model phase (explicitly NOT SQLite/bitemporal per this task's exclusions) |
| OMEGA-010 (multi-process restart unverified) | Testing-infrastructure gap, not a code defect | None on P0 | Low — the underlying checkpoint mechanism IS tested, just not cross-process | Test-infrastructure improvement phase |
| OMEGA-011 (`_run_maintenance` never invoked) | Feature-completeness gap, not security/correctness | None on P0 | Low — Consolidator/Deduplicator/LearningEngine remain correctly callable manually/via tests | Cognitive-loop wiring phase |
| OMEGA-012 (`GitIntegration` dead code) | Pure cleanup, zero functional risk either way | None on P0 | None | Cleanup phase (or AG-CONT-01, if repurposed) |
| OMEGA-013 (`Continuity_Handoff.md` category error) | Explicitly scoped to AG-CONT-01, itself gated behind this contract | Depends on THIS contract's completion | Medium — affects future continuity trustworthiness | AG-CONT-01 (see Part 17) |

## PART 17 — AG-CONT-01 Gate

**AG-CONT-01 MUST remain BLOCKED** until all of the following become true:
1. This P0 contract is fully implemented (Part 13) and independently pytest-verified (Part 15's runtime criteria) in an execution-capable environment.
2. A human or independent agent has re-confirmed I-001 through I-012 hold against the actual updated codebase (not merely against this contract's prose).
3. `Continuity_Handoff.md` is updated (in a SEPARATE, later task, per this task's explicit exclusion) to reflect the true `CODE_COMMIT`/`TEST_COMMIT` distinction, so that AG-CONT-01's own eventual handoff manifest does not inherit the same "claims commit X but was actually tested at commit Y" staleness pattern already found in the current handoff.

Until then, any continuity/handoff manifest built on `MemoryController` would inherit the exact trust-fabrication risk this contract closes — building AG-CONT-01 first would simply propagate the same flaw into a second subsystem. This reasoning is carried forward unchanged from the prior forensic validation session and is not re-derived here.

## PART 18 — Execution Prompt for Antigravity

```
ANTIGRAVITY — EXECUTE PHASE 4.3 P0 SECURITY HARDENING

You have direct repository execution access (git clone, pytest, file write).
Perplexity has produced an implementation contract at:
99_SYSTEM/Phase43_P0_Implementation_Contract.md

Your task:

1. INSPECT the current HEAD of:
   - memory_controller/controller.py
   - memory_controller/authorizer.py
   - memory_controller/authority.py (read-only, confirm no change needed)
   - cognitive_core/reflection.py, consolidation.py, deduplication.py, learning.py (read-only, regression check)

2. RUN the baseline suite FIRST:
   python -m pytest -q
   Record the exact output verbatim before changing anything.

3. IMPLEMENT exactly the six-file patch plan in Part 13 of the contract:
   - memory_controller/controller.py: harden propose() and update(); add attest()
   - memory_controller/authorizer.py: add Operation.ATTEST and its policy entry
   - Do NOT modify authority.py, schema.py, provenance.py, audit/logger.py
   - Do NOT modify any cognitive_core/*.py file

4. WRITE the adversarial tests specified in Part 11 (P0-001 through P0-015) in
   memory_controller/tests/test_security_hardening.py and, where noted,
   cognitive_core/tests/test_tool_router_security.py.

5. RUN the full suite again:
   python -m pytest -q
   Record the exact output verbatim. Compare against the baseline from step 2.

6. VERIFY the regression protections in Part 12: LearningEngine's
   partially_verified path, and reflection/consolidation/deduplication's
   REVIEW-at-creation pattern, must still pass unchanged.

7. VERIFY no unrelated file changed:
   git status --short
   git diff --stat
   Confirm 06_INBOX/RAW_IMPORTS/ is untouched. Confirm no .gemini/, scratch/,
   cache/, credentials, tokens, or keys are staged.

8. If and ONLY if all tests pass and the diff is clean, commit:
   git add memory_controller/controller.py memory_controller/authorizer.py \
           memory_controller/tests/test_security_hardening.py \
           cognitive_core/tests/test_tool_router_security.py
   git commit -m "feat: harden Phase 4.3 memory trust boundary"
   git push origin main

9. Record the exact commit hash and verify it via:
   git log --oneline -5

10. DO NOT modify Continuity_Handoff.md, Memory_Protocol.md, or Rules.md.
    DO NOT implement AG-CONT-01.
    DO NOT implement any P1/P2/P3 finding from Part 16.

CRITICAL RULE:
Do not claim test success unless pytest was actually executed against the
repository checkout. If any test fails, STOP, report the exact failure,
and do not commit.

Return: baseline pytest output, final pytest output, exact diff, commit hash,
push confirmation, and an explicit PASS/FAIL for each of I-001 through I-012.
```

## PART 19 — Git Persistence

This document itself will be created at `99_SYSTEM/Phase43_P0_Implementation_Contract.md` via the GitHub API, as the sole file change for this task, per the explicit scope restriction.

---

*This contract does not implement, execute, or verify anything. It specifies. Implementation and verification are explicitly deferred to an execution-capable agent, per Part 18.*

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[11 Templates and System Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
