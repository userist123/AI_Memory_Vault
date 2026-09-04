---
project_id: AI_MEMORY_VAULT
application: AI Memory Vault / Memory Engine
repository: userist123/AI_Memory_Vault_CODEX_READY
last_updated_utc: 2026-09-04T20:20:00Z
current_main_sha: e2dfbd6837353e43c84a7b3917783334b230dbad
status: ACTIVE
working_branch_policy: MAIN_ONLY
agent_execution_policy: SEQUENTIAL_HANDOFF
current_round: R001
active_work:
  - cognitive-memory target model and Planning Influence MVE on main
  - deterministic MVE calibration and experiment readiness
  - single-main sequential execution
recent_state:
  - Perplexity adversarial validation: ACCEPT WITH CHANGES / GO WITH MANDATORY MVE CHANGES
  - main is the only canonical working branch for ongoing development
  - README was reconstructed as a current-state technical landing page and now documents architecture, MVE, uncertainty policy, persistent agent memory, CI, security, and known gaps
  - GAP-011 lifecycle classifier substring matching fixed with whole-word matching
  - deterministic Planning Influence MVE contains four arms and soft priors
  - oracle-leak flaw removed: treatment priors derive from independent frozen memory recommendations
  - MVE test corrected so stale/wrong memory is not assumed to succeed
  - MVE quality diagnostic added to separate correct and incorrect memory recommendations
  - applicability-aware influence added and routed through treatment using an oracle-independent frozen applicability sequence
  - local mechanics regression suite for the current harness passed 9/9 in reconstructed exact-source execution
  - local applicability-aware pilot: advisory 30 nodes / 0 fatal; treatment 54 nodes / 12 fatal; stale 30 nodes / 0 fatal
  - applicability distribution: 8 APPLICABLE, 8 APPLICABLE_WITH_VERIFICATION, 7 INSUFFICIENTLY_KNOWN, 7 NOT_APPLICABLE
  - treatment recommendation matched optimal in 7/30 cases; mismatch group used 47 treatment nodes and recorded 12 fatal visits
  - treatment_vs_advisory_node_reduction = -0.8000; improved versus prior 125-node treatment but still negative
  - no post-hoc parameter tuning used to force improvement
  - applicability-aware pilot result persisted in 07_EVALUATION/luna/PLANNING_INFLUENCE_APPLICABILITY_PILOT_LOCAL_20260904.md
  - pre-registered uncertainty policy persisted in 07_EVALUATION/luna/PLANNING_INFLUENCE_UNCERTAINTY_POLICY_V1.md
  - latest MVE CI run observed as queued; no CI runtime claim made
open_requirements:
  - verify latest applicability-aware MVE CI run and inspect artifact/stdout
  - verify Memory V6 CI and inspect test results
  - keep the uncertainty policy frozen before its first deterministic implementation run
  - implement the pre-registered verification route in the isolated MVE harness only
  - separate applicability, evidence strength, verification cost, planner influence and outcome
  - only then design the model-backed paired MVE
  - no feature-branch development
  - no parallel agent work on the same project task chain
  - preserve resumable handoff state every session
blockers:
  - GitHub Actions may remain queued; no CI runtime artifact is accepted until verified
  - local environment cannot clone GitHub directly
  - remote branch deletion requires explicit delete-ref capability and must not be fabricated
  - Codex unavailable this week
next_actions:
  - verify newest applicability-aware MVE CI run and inspect artifact/stdout
  - verify Memory V6 CI and inspect test results
  - implement the pre-registered verification route without changing production Vault contracts
  - run the deterministic pilot against the frozen uncertainty policy
  - proceed to model-backed paired MVE only after deterministic calibration is successful or explicitly falsified
