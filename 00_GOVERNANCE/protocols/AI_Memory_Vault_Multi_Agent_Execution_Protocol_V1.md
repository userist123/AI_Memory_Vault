---
type: core
category: governance
status: active
version: 1.0.0
id: "multi-agent-execution-protocol-v1"
document_kind: policy
document_status: active
provenance_status: maintained
policy_scope: vault-governance
relations:
  - "00_GOVERNANCE/rules/Rules.md"
  - "00_GOVERNANCE/protocols/Memory_Protocol.md"
  - "00_GOVERNANCE/skills/ai-memory-vault/SKILL.md"
---

# AI Memory Vault — Multi-Agent Execution Protocol V1

## Repository

`userist123/AI_Memory_Vault_CODEX_READY`

## SOURCE OF TRUTH

GitHub `main` + committed source + real test output + CI have precedence over:

- agent reports;
- README claims;
- previous summaries;
- planning documents;
- screenshots;
- generated analysis.

Never treat `DECLARED` as `OBSERVED`.
Never treat `OBSERVED` as `USED`.
Never treat `USED` as `CAUSALLY_EFFECTIVE`.

### Evidence levels

- `DOCUMENT_VERIFIED` — document exists and explicitly says X.
- `CODE_VERIFIED` — source code was inspected and proves X.
- `TEST_VERIFIED` — deterministic test proves X.
- `RUNTIME_VERIFIED` — real execution proves X.
- `CI_VERIFIED` — CI independently executed and passed.
- `CLAIMED_ONLY` — assertion without sufficient evidence.
- `UNVERIFIED` — evidence is currently insufficient.

## NON-NEGOTIABLE RULES

1. Never fabricate test execution.
2. Never report a local test as passed without actual stdout/stderr.
3. Never claim a Git commit unless the commit exists remotely.
4. Verify `main` before modifying anything.
5. After modifications, verify the resulting remote commit.
6. Do not weaken existing security invariants to make tests pass.
7. Do not bypass Defender/security controls.
8. Do not promote `REVIEW` knowledge to `ACTIVE` merely to make retrieval benchmarks pass.
9. Preserve provenance.
10. Preserve lifecycle semantics.
11. Separate diagnosis from implementation.
12. Do not silently change benchmark definitions.
13. Do not modify historical test expectations merely to hide regressions.
14. If an expected capability does not exist, report it as missing instead of simulating it.
15. Every substantive change must be committed.
16. Every completed task must leave reproducible evidence.

## CURRENT ARCHITECTURAL PRINCIPLE

The intended memory flow is:

```text
RESEARCH
→ EVIDENCE
→ EVALUATION
→ KNOWLEDGE
→ MEMORY/SKILL/PROCEDURE
→ RETRIEVAL
→ AGENT EXECUTION
→ TRACE
→ OUTCOME
→ EVIDENCE
```

For review-gated knowledge:

```text
READY_FOR_HUMAN_REVIEW
→ REVIEW-SAFE RETRIEVAL
→ `_cognitive_unverified = true`
→ agent may inspect/evaluate
→ HUMAN APPROVAL
→ ACTIVE
```

Retrieval must never implicitly promote `REVIEW` knowledge.

## CURRENT PRIORITIES

### P0

- establish repository ground truth;
- determine whether retrieval is actually semantic or lexical;
- prove resistance to malicious instructions embedded in retrieved memory;
- preserve lifecycle/security boundaries.

### P1

- ranking discrimination;
- confidence calibration;
- learning/outcome loop closure;
- associative retrieval;
- temporal validity;
- provenance-aware retrieval;
- knowledge-quality differentiation.

## DELEGATION MODEL

### CODEX

Implementation + real execution + tests + commits + CI evidence.

### ANTIGRAVITY

Visual/debug/architecture inspection + developer observability; no unilateral security/core-memory changes.

### PERPLEXITY

External research and evidence synthesis; no repository claims unless independently grounded.

### LUNA / GPT-5.6

Independent verification, adversarial challenge, cross-agent reconciliation, scoring, and final acceptance/rejection.

## INDEPENDENCE RULE

No agent is allowed to mark its own work as independently verified.

## EXECUTION REQUIREMENT

For every task performed against the Vault, the responsible agent must:

1. establish the current `main` state before changes;
2. distinguish documented claims from observed implementation behavior;
3. identify which evidence level supports each substantive conclusion;
4. keep review-gated knowledge inside its lifecycle boundary;
5. execute applicable tests or runtime checks rather than inferring success;
6. record failures and missing capabilities instead of masking them;
7. commit substantive changes;
8. verify the resulting remote commit and, where applicable, CI evidence.

## CHANGE DISCIPLINE

This protocol is itself part of the canonical Vault. Updates to it must preserve the same evidence, provenance and commit requirements defined above.

When this protocol conflicts with a weaker agent-local instruction, the repository policy takes precedence.
