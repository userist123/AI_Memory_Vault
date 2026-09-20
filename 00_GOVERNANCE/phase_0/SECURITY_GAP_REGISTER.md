---
id: 0d8f4ce3-daab-4aaa-9c25-9bea910d23d8
type: procedure
lifecycle: REVIEW
category: governance.phase0
tags: ['phase-0', 'reconciliation', 'measured']
created: "2026-09-20"
updated: "2026-09-20"
provenance:
  source_type: 'execution'
  source_ref: 'phase 0 reconciliation measured on origin/main 10224498c, 2026-09-20'
confidence: high
verification: unverified
relations:
  - type: part_of
    target_id: slot-06-procedures
---

# Security gap register — Phase 0

Gaps found while reconciling, each with the evidence that found it. Severity is
about this repository being public and agent-driven, not a generic scale.

| # | Gap | Evidence | Severity |
|---|---|---|---|
| S1 | No entry point filters by lifecycle. `memory_mcp_server`, `recall_cli` and `tool_router` never mention `lifecycles`, so REVIEW notes and the 148 notes with no lifecycle reach agents. | grep over the three files on `10224498c` | high |
| S2 | Memory content is not separated from instructions. Nothing strips or neutralises imperative text in a retrieved note before it enters an agent's context. | no sanitiser between `search()` results and callers | high |
| S3 | `06_INBOX/RAW_IMPORTS/` is allowlisted in `.gitleaks.toml`. Anything force-added there is never secret-scanned. | `.gitleaks.toml` | medium |
| S4 | 3,661 imported skills carry executable scripts. None exfiltrates today — checked for browser-credential reads, obfuscated `exec`, and known exfil endpoints, all clean — but any of them runs if an agent invokes it. | scan on 2026-09-20 | medium |
| S5 | Book licences are self-declared by the ingesting agent. The six `ashby_*` notes claimed public domain with nothing behind it; corrected in #174 but the class of claim remains unchecked. | `#174` | medium |
| S6 | No integrity hash over ACTIVE notes, so silent drift is undetectable. | no manifest exists | medium |
| S7 | `search()` has no abstention. Every query returns something; a query with no answer in the vault returns the closest unrelated notes. Demonstrated: "graph expansion budget" returns notes about LLM hallucination and classified-information reform. | run on `10224498c` | medium |
| S8 | Unverified material has no context quota. 723 notes are `unverified` and nothing bounds how much of a context pack they may occupy. | no quota in the packing code | medium |
| S9 | The usage log's 26 recorded calls all come from one driver script, `claude-sonnet-mcp-sdk-driver`, not from a real agent session. Real-use evidence is thinner than the report implies. | `usage.jsonl` | low |

## Not a gap

The vault does not exfiltrate. Checked on 2026-09-20 after the owner's
Instagram account was used for a crypto-giveaway post: one remote, no unknown
commit authors, no active git hooks, no code reading browser credential stores,
no obfuscated execution, no unexplained outbound connection. The account
compromise has no connection to this repository.
