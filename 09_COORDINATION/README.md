# 09_COORDINATION — Parallel Agent Hub

This directory is the coordination surface for the multi-agent Memory Vault workflow.

## Single entry point

Start every agent session with:

`AGENT_START_HERE.md`

It defines the persistent read order, current-SHA rule, lane ownership, evidence contract, security invariants and future-round continuation protocol.

## Read first

1. `AGENT_START_HERE.md`
2. `../00_CORE/AI_Memory_Vault_Multi_Agent_Execution_Protocol_V1.md`
3. `PARALLEL_EXECUTION_V1.md`
4. `ROUND_NEXT_WORK_V1.md`

## Agent prompts

| Lane | Prompt | Ownership |
|---|---|---|
| CODEX | `prompts/CODEX_CONTINUATION_V1.md` | implementation / runtime / tests |
| ANTIGRAVITY | `prompts/ANTIGRAVITY_CONTINUATION_V1.md` | observability / architecture |
| PERPLEXITY | `prompts/PERPLEXITY_CONTINUATION_V1.md` | external research / evidence |
| LUNA | `prompts/LUNA_CONTINUATION_V1.md` | independent verification / adversarial audit |

## Rule

All four lanes may start from the same current `main` baseline and work concurrently.

They do not edit one another's lanes.
They synchronize at evidence barriers rather than by serial task execution.

## Evidence directories

- `07_EVALUATION/codex/`
- `07_EVALUATION/antigravity/`
- `07_EVALUATION/perplexity/`
- `07_EVALUATION/luna/`

## Baseline note

The historical anchor below is retained for traceability only. It is not the current baseline:

`9a663213c52b971dee28d4eff729d1e93914fdce`

Every future round must resolve the actual current `main` SHA again.
