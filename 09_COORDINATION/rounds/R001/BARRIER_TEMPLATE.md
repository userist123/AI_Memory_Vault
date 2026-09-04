# R001 — Evidence Barrier Template

## Round

- Round ID: `R001`
- Actual main SHA at barrier: `<TO_BE_RESOLVED>`
- Barrier timestamp: `<UTC_TIMESTAMP>`

## Lane status

| Lane | Status | Evidence artifact | Commit | CI |
|---|---|---|---|---|
| CODEX | READY / PARTIAL / BLOCKED / FAILED / NO_CHANGE | `<link>` | `<sha>` | `<result>` |
| ANTIGRAVITY | READY / PARTIAL / BLOCKED / FAILED / NO_CHANGE | `<link>` | `<sha>` | `<result>` |
| PERPLEXITY | READY / PARTIAL / BLOCKED / FAILED / NO_CHANGE | `<link>` | n/a | n/a |
| LUNA | READY / PARTIAL / BLOCKED / FAILED / NO_CHANGE | `<link>` | `<sha>` | `<result>` |

## Evidence classification

Only use:

- `DOCUMENT_VERIFIED`
- `CODE_VERIFIED`
- `TEST_VERIFIED`
- `RUNTIME_VERIFIED`
- `CI_VERIFIED`
- `CLAIMED_ONLY`
- `UNVERIFIED`

## Findings

### Confirmed

### Partially confirmed

### Contradicted

### Unverified

### Blocked

### Requires new test

## Conflicts

Record contradictions between lanes without averaging them away.

## Security gate

- [ ] no security invariant weakened
- [ ] no REVIEW -> ACTIVE promotion for benchmark purposes
- [ ] provenance preserved
- [ ] lifecycle semantics preserved
- [ ] no fabricated test/runtime/CI evidence

## Integration decisions

For each proposed implementation change:

| Change | Owner | Reproduced? | Security-safe? | Tests | CI | Luna decision |
|---|---|---|---|---|---|---|

## Next round candidates

1.
2.
3.

## Barrier rule

This document records evidence and decisions. It does not itself merge code and does not convert claims into facts without independent support.
