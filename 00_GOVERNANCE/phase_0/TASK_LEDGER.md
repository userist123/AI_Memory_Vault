---
id: b777e588-a5ac-47da-83f2-260c7e476a69
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

# Task ledger — Phase 0

Mandate IDs mapped onto files that exist on `10224498c`. Owner = who builds,
reviewer = who falsifies. They are never the same agent.

| ID | Task | Files owned | Owner | Reviewer | Depends | Accept | Stop | Status |
|---|---|---|---|---|---|---|---|---|
| BASE-001 | Fast-forward; fix the red `main` | `cognitive_core/__init__.py` or `memory/controller.py:1444` | CLAUDE | LUNA | — | `pytest 20_TESTS` green; CLI runs with no `PYTHONPATH` | any other test turns red | READY |
| BASE-002 | Module matrix | this document set | CLAUDE | LUNA | BASE-001 | every module has a status and evidence | — | ACCEPTED |
| LIFE-001 | One transition policy, pure and exhaustive | `lifecycle/policy.py` | CLAUDE | LUNA | D2, D3 | state × principal × operation covered; no bypass path | a second mutation path appears | BLOCKED on D2/D3 |
| LIFE-002 | Reconcile enum, schema, migration | `lifecycle/`, SQLite schema | CODEX | LUNA | LIFE-001 | enum = schema = code | — | BLOCKED |
| SEC-001 | Lifecycle floor at every entry point | `memory_mcp_server.py`, `recall_cli.py`, `tool_router.py` | CLAUDE | LUNA | D3 | an `AI_AGENT` call cannot receive REVIEW without asking | recall on the v3 benchmark drops | BLOCKED on D3 |
| SEC-002 | Prompt-injection suite | `20_TESTS/security/` | LUNA | CLAUDE | SEC-001 | attack visible in trace; action blocked even when retrieved | — | BLOCKED |
| RET-001 | Map the real path | — | CLAUDE | — | — | done in `RETRIEVAL_PATH_CURRENT.md` | — | ACCEPTED |
| RET-002 | `reasoning` / `executive`: make them real or take them out | `memory/controller.py` | CODEX | LUNA | RET-001 | either the output is consumed and measured, or the hook is gone | a hook stays that only writes a trace | READY |
| OBS-001 | Versioned `RetrievalTrace` | `memory/controller.py`, new schema | ANTIGRAVITY | CLAUDE | RET-001 | every exclusion carries a reason code; no raw query logged | — | READY |
| CORP-001 | 5 duplicate groups, 13 notes | `01_ARCHITECTURE/knowledge/` | ANTIGRAVITY | LUNA | — | each group classified; nothing deleted automatically | a deletion based on similarity alone | READY |
| CORP-002 | Near-duplicates | new script | ANTIGRAVITY | LUNA | CORP-001 | threshold declared and justified on a labelled set first | threshold tuned after seeing results | BLOCKED |
| GRAPH-002 | Proposer: strong relations need an explicit citation | `edge_proposer.py` | ANTIGRAVITY | LUNA | — | entity overlap alone can no longer produce `depends_on` | — | READY |
| GRAPH-003 | Independent re-audit, then purge via `plasticity` | `graph/plasticity.py` | ANTIGRAVITY | ChatGPT / Perplexity label | GRAPH-002 | rollback restores the graph byte for byte | Antigravity labels its own audit | BLOCKED |
| EVAL-002 | Held-out set | `07_EVALUATION/` | LUNA | CLAUDE | RET-002 | labels never seen by whoever builds | — | BLOCKED |
| CI-004 | Schema guard | `.github/workflows/` | CLAUDE | LUNA | LIFE-002 | enum drift fails the build | — | BLOCKED |
| TRANS-001 | Finish translating the Chinese skills | `.agents/skills/`, `30_SCRIPTS/skills/translate_skills.py` | CLAUDE | — | — | no Chinese left outside code blocks; manifest records every original hash | a rejected file is silently kept | IN_PROGRESS |

Two tasks never share a file. Where two would, the later one waits.
