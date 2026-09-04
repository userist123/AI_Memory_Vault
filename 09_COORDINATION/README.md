# 09_COORDINATION — Parallel Agent Hub

This directory is the coordination surface for the multi-agent Memory Vault workflow.

## Read first

1. `../00_CORE/AI_Memory_Vault_Multi_Agent_Execution_Protocol_V1.md`
2. `PARALLEL_EXECUTION_V1.md`
3. `ROUND_NEXT_WORK_V1.md`

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

## Current known evidence anchor

At the time this coordination pack was created, `main` was anchored at:

`9a663213c52b971dee28d4eff729d1e93914fdce`

Future rounds must resolve the actual current `main` SHA again.
