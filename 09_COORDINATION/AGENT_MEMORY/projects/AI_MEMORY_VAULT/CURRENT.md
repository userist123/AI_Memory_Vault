---
project_id: AI_MEMORY_VAULT
application: AI Memory Vault / Memory Engine
repository: userist123/AI_Memory_Vault_CODEX_READY
last_updated_utc: 2026-09-04T19:00:00Z
current_main_sha: 0a799e1b0fa7d3ce5c225801247558dd596c8a83
status: ACTIVE
working_branch_policy: MAIN_ONLY
agent_execution_policy: SEQUENTIAL_HANDOFF
primary_folders:
  - cognitive_core/
  - memory_controller/
  - 01_KNOWLEDGE/
  - 07_EVALUATION/
  - 09_COORDINATION/
active_agents:
  ANTIGRAVITY: may work when assigned; must work from main
  LUNA: architecture, verification, adversarial reconciliation; must work from main
  PERPLEXITY: external research/evidence synthesis when dispatched; outputs land on main
  CODEX: implementation deferred until token availability; future work starts from main
current_round: R001
active_work:
  - cognitive-memory target model and Planning Influence MVE on main
  - repository reality mapping and experiment readiness
  - branch consolidation and single-main workflow
recent_state:
  - Perplexity adversarial validation: ACCEPT WITH CHANGES / GO WITH MANDATORY MVE CHANGES
  - cognitive-memory V1/V2 and Planning Influence specifications are on main
  - main is the only canonical working branch
  - agent work is sequential, not parallel; any next agent must be able to resume from persistent Vault state
open_requirements:
  - no feature-branch development for normal work
  - every substantive session must persist state before handoff
  - every handoff must name task, main SHA, evidence refs, remaining work and exact next action
  - a receiving agent must not depend on chat history when the Vault contains the handoff
  - stale/legacy branches are archival only and must not receive new work
blockers: []
next_actions:
  - consolidate or archive all legacy branch work without losing verified evidence
  - delete legacy remote branches when GitHub branch-delete capability is available
  - continue all substantive work from main
  - keep the project CURRENT and agent CURRENT records synchronized
