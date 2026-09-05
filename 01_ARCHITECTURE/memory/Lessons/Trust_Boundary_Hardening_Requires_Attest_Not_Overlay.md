---
id: "a81adbaa-68bf-4d8a-a18a-26af82a3d375"
type: lesson
lifecycle: REVIEW
category: memory_controller.security
tags: [security, trust-boundary, memory-controller, p0, attestation, self-improvement-loop]
created: 2026-08-15
updated: 2026-08-15
provenance:
  source_type: ai
  source_ref: "Phase 4.3 P0 security hardening session"
confidence: high
verification: unverified
relations: []
---

# Lesson: Trust Boundary Hardening Requires a Dedicated Attest Path, Not Field Overlay Sanitization

## Context

During Phase 4.3 P0 security hardening of `MemoryController`, three related vulnerabilities were found and fixed:

- OMEGA-001: `AI_AGENT` could self-set `verification="verified"`.
- OMEGA-002: `AI_AGENT` could self-claim privileged `provenance.source_type` (e.g. `"official"`, `"user"`).
- OMEGA-006: any caller could inject a privileged `lifecycle` state (e.g. `"ACTIVE"`) directly at note creation.

Root cause in all three: `propose()`/`update()` merged the caller's raw payload into the note (`note.update(note_data)`, `prov.update(note_data.get('provenance', {}))`) with no field-level, principal-aware gating -- only operation-level authorization ("can this principal call propose() at all") existed, which does not protect individual sensitive fields.

## Pattern (Generalizable)

1. **Operation-level authorization is not field-level authorization.** A principal being allowed to call a write method does not mean every field in that method's payload is safe to accept verbatim.
2. **Trust-escalating fields need their own dedicated write path**, separate from general create/update. Here: `attest()`, restricted to `HUMAN`/`ADMIN`, is the *only* legitimate way to reach `verification="verified"` -- not a sanitization filter inside `propose()`/`update()`.
3. **Distinguish caller-explicit values from system-generated defaults** before merging. Checking the caller's raw payload (`note_data.get('verification')`) for a forbidden value must happen *before* defaults are merged in, otherwise a legitimate default can be confused with a malicious explicit claim (or vice versa).
4. **Principal-scoped allowlists, not blanket restrictions.** The fix for OMEGA-006 was originally too broad (blocking all principals from setting privileged lifecycle at creation) until a real regression (`test_supersession_phase43.py`'s ADMIN-authored ACTIVE notes) proved that trusted principals legitimately need this. The correct scope was AI_AGENT only.
5. **Regression tests must assert on real caller usage patterns**, not just the new adversarial case -- the ADMIN-at-ACTIVE regression was caught only by running an existing test, not by reasoning about the fix in isolation.

## Evidence

- `Phase43_P0_Implementation_Contract.md` -- full implementation contract.
- `Phase43_Forensic_Validation.md` -- original vulnerability confirmation.
- `memory_controller/tests/test_security_hardening.py` -- adversarial tests (P0-001 through P0-015).

## When To Reapply This Pattern

Any time a new write path is added to `MemoryController` (or an equivalent trust boundary elsewhere in this project) that accepts a caller-supplied payload dict: check whether any field in that payload can escalate trust (verification, provenance tier, lifecycle, authority-affecting fields), and if so, gate that field explicitly and test the exact caller populations (AI_AGENT, HUMAN, ADMIN) against both the attack and the legitimate-use regression.

## Related
- [[Memory - Lessons Map]]
- [[Rules.md]]

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[08 Memory Subsystems Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
