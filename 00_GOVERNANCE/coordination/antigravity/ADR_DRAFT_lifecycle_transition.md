# ADR DRAFT — Lifecycle Transition: REVIEW → ACTIVE vs REVIEW → VERIFIED → ACTIVE

**STATUS**: PROPOSAL — DECISION OWNER: CODEX  
**DATE**: 2026-09-05  
**AUTHOR**: ANTIGRAVITY (P0.2 Test Suite Stabilization Analysis)  
**TARGET MODULE**: memory_controller/controller.py  
**REFERENCE COMMITS**: 54e85f20a, 4fbc35dd9  

---

## 1. Context & Problem Statement

In recent security hardening commits (54e85f20a, 4fbc35dd9), a check was introduced into MemoryController.promote() (memory_controller/controller.py:379):

`python
if note.get('verification') != 'verified':
    raise ValueError('Only VERIFIED notes can be promoted to ACTIVE')
`

This change creates a architectural divergence between two lifecycle models:
* **Model A (Orthogonal State)**: lifecycle and erification are separate axes. A note in REVIEW can be promoted to ACTIVE by an authorized human/admin while retaining erification: "unverified".
* **Model B (Strict Two-Gate Sequence)**: erification: "verified" is a mandatory prerequisite for promotion to ACTIVE. A note in REVIEW must first undergo explicit attestation (controller.attest()) before controller.promote() can be called.

Because memory_controller/** is frozen under the exclusive ownership of **CODEX**, this document provides a forensic, read-only impact analysis and submits options for Codex to decide.

---

## 2. Empirical Impact on Test Suite

Enforcement of Model B causes 5 test failures in memory_controller/tests/:

1. memory_controller/tests/test_audit.py::test_audit_promote_success_and_fail
   * **Cause**: Test proposes a note and directly calls controller.promote(Principal.HUMAN, nid).
   * **Failure**: ValueError: Only VERIFIED notes can be promoted to ACTIVE

2. memory_controller/tests/test_authorization.py::test_human_promote_allowed
   * **Cause**: Test sets note {"id": "p2", "lifecycle": Lifecycle.REVIEW} and calls controller.promote(Principal.HUMAN, "p2").
   * **Failure**: ValueError: Only VERIFIED notes can be promoted to ACTIVE

3. memory_controller/tests/test_authorization.py::test_admin_promote_allowed
   * **Cause**: Test sets note {"id": "p3", "lifecycle": Lifecycle.REVIEW} and calls controller.promote(Principal.ADMIN, "p3").
   * **Failure**: ValueError: Only VERIFIED notes can be promoted to ACTIVE

4. memory_controller/tests/test_cache.py::test_mutation_invalidation_review_promote
   * **Cause**: Proposes note, reviews note, then calls controller.promote(Principal.ADMIN, nid) without attesting.
   * **Failure**: ValueError: Only VERIFIED notes can be promoted to ACTIVE

5. memory_controller/tests/test_milestone3_empirical_challenge.py::test_concurrent_attest_and_update_race_sqlite
   * **Cause**: Note created with erification="unverified", promoted via controller.promote(Principal.HUMAN, note_id) to set up race condition.
   * **Failure**: ValueError: Only VERIFIED notes can be promoted to ACTIVE

---

## 3. Interaction with Security Invariants (I-001..I-012)

* **I-001 (AI Self-Verification Gated)**: Principal.AI_AGENT cannot set erification = "verified".
  * *Impact*: Satisfied under both models.
* **I-003 (Creation Lifecycle Restricted)**: Principal.AI_AGENT cannot create directly into ACTIVE.
  * *Impact*: Under Model A, human/admin can promote an unverified note to ACTIVE. Under Model B, even human/admin cannot promote to ACTIVE without a prior ttest() step.
* **I-004 (Attestation Authorization)**: Only Principal.HUMAN and Principal.ADMIN can invoke controller.attest().
  * *Impact*: Under Model B, ttest() becomes an indispensable gate for any note entering ACTIVE.
* **I-005 (Provenance Immutability)**: provenance.source_type cannot be mutated after creation.
  * *Impact*: Unaffected by either model.
* **P0 Adversarial Tests (P0-001..P0-015)**:
  * *Impact*: All 15 adversarial tests in 	est_adversarial_p0_p15_invariants.py pass under both models because they test unauthorized escalation by Principal.AI_AGENT.

---

## 4. Interaction with Cognitive Core (ToolRouter & Cache)

* **ToolRouter Reconciliation Boundary (cognitive_core/tool_router.py:68)**:
  `python
  if node and node.get("verification") == "verified":
      raise ApprovalRequiredError(...)
  `
  * Under Model A, ACTIVE notes can have erification: "unverified". In that case, ToolRouter permits autonomous AI updates/archives on those notes.
  * Under Model B, **all** ACTIVE notes are guaranteed to be erification: "verified". Consequently, **any** mutation on **any** ACTIVE note is intercepted by ToolRouter and requires explicit human approval.
* **Cache Invalidation**:
  * controller.promote() flushes cache queries. Model B does not alter cache behavior; it simply restricts when promote() can succeed.

---

## 5. Decision Options for Codex

### Option 1: Adopt Strict Model B (REVIEW → VERIFIED → ACTIVE)
* **Mechanism**: Retain if note.get('verification') != 'verified': raise ValueError(...) in controller.py:379.
* **Action Required by Codex**: Update the 5 legacy tests in memory_controller/tests/ to either:
  1. Call controller.attest(principal, note_id) prior to controller.promote(), OR
  2. Instantiate test fixture notes with erification: "verified".
* **Pros**: Guarantees that zero unverified memories ever exist in ACTIVE state. Maximizes trust boundary rigor.
* **Cons**: Breaks the conceptual orthogonality between lifecycle status and verification grade.

### Option 2: Human/Admin Auto-Attestation on Promotion
* **Mechanism**: In controller.promote(), if an authorized Human or Admin promotes a note whose verification is unverified, promote() automatically sets erification = "verified" (recording the human/admin in the audit log).
* **Action Required by Codex**: Modify controller.promote() to set 
ote['verification'] = 'verified' upon authorized promotion.
* **Pros**: Preserves backward compatibility of all existing tests while ensuring that promoted notes are verified.
* **Cons**: Merges two distinct actions (review/promotion and verification/attestation) into one.

### Option 3: Revert to Model A (Orthogonal Axis)
* **Mechanism**: Remove the verification requirement from controller.promote().
* **Action Required by Codex**: Delete line 378-379 of memory_controller/controller.py.
* **Pros**: 100% backward compatible with legacy test suite.
* **Cons**: Leaves open the possibility of ACTIVE memories with erification: "unverified".

---

## 6. Recommendation

Antigravity recommends **Option 1** or **Option 2** for maximum security posture consistency with the Prime Directive, but defers the formal decision and code update to **CODEX** as the canonical runtime owner.


## 🔗 Legături Sinaptice
- [[00_GOVERNANCE/README|Governance]]
- [[00 Core Map]]
- [[14 Subagents Council Map]]
- [[Knowledge Graph Home]]
