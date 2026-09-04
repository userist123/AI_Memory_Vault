---
project_id: AI_MEMORY_VAULT
application: AI Memory Vault / Memory Engine
repository: userist123/AI_Memory_Vault_CODEX_READY
last_updated_utc: 2026-09-04T19:25:00Z
current_main_sha: 1131892c267d61db244eb9efc048a67795e085be
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
  LUNA: may work when assigned; must work from main
  PERPLEXITY: research only when dispatched; outputs must be persisted on main
  CODEX: implementation deferred; future work starts from main
current_round: R001
active_work:
  - cognitive-memory target model and Planning Influence MVE on main
  - repository reality mapping and experiment readiness
  - migration to single-main sequential execution
recent_state:
  - Perplexity adversarial validation: ACCEPT WITH CHANGES / GO WITH MANDATORY MVE CHANGES
  - cognitive-memory V1/V2 and Planning Influence specifications are on main
  - main is the only canonical working branch for ongoing development
  - agent execution is sequential; the active agent must leave a resumable handoff for the next agent
open_requirements:
  - no feature-branch development for normal work
  - no parallel agent work on the same project task chain
  - every substantive session must persist state before handoff
  - every handoff must name task, main SHA, evidence refs, remaining work and exact next action
  - a receiving agent must not depend on chat history
  - legacy branches receive no new work and are administrative/archive references only
blockers:
  - remote branch deletion cannot be completed with the currently available GitHub connector because no branch-delete operation is exposed
next_actions:
  - continue all substantive work directly on main
  - selectively preserve any verified legacy branch evidence on main before administrative deletion
  - delete legacy remote branches through GitHub branch administration when available
  - keep all four agent CURRENT records aligned to main
