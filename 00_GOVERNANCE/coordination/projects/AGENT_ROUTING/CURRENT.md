---
project_id: AGENT_ROUTING
application: Central agent router, cross-agent dispatcher and secure agent bridge
repository: userist123/AI_Memory_Vault_CODEX_READY
workspace: 03_IMPLEMENTATION/packages/routing, 03_IMPLEMENTATION/packages/agent_bridge
last_updated_utc: 2026-10-07T00:00:00Z
status: ACTIVE
working_branch: claude/pr211-security-fixes (PR #213, stacked on PR #211 chore/claude-workflow-dfir-contract)
claimed_by: claude-code — 2026-10-07T00:00:00Z
active_work:
  - make PR #211 mergeable; PR #213 holds the review fixes; #213 is not merged into #211 (owner/orchestrator decision)
done_on_branch:
  - dispatch used the registry's logical `adapter_ref` (claude_code, antigravity) as a program name; the program is now the registry field `executable` (validated at load), used by both `routing.route_cli` and the dispatcher
  - route.json and result.json now land in one run directory (the dispatcher passes its run dir to adapters)
  - CLAUDE.md regained every section of main that the routing rewrite had dropped (Protected Core list, Production-Consumer rule, provenance/safety, ingestion pipeline, Obsidian, retrieval priority); the contract test pins them
  - agent_bridge enforces `minimum_ttl_seconds`; AntigravitySession no longer shares a conversation between tasks
  - VAULT_STATE.md section 3 now records both packages as NOT wired into production
verification:
  - 20_TESTS/test_agent_dispatch_real_config.py (real config + fake executables on PATH; no real agent invoked)
  - 20_TESTS/test_agent_bridge_hardening.py, 20_TESTS/test_claude_contract.py, 20_TESTS/test_vault_state_accuracy.py
  - the real claude / codex / agy / ollama binaries were NOT exercised, so dispatch to them is UNVERIFIED
non_obvious_findings:
  - the earlier dispatcher tests passed program names as `adapter_ref` and mocked `shutil.which` to always succeed, which hid the broken real-config path
  - agent_bridge still has no transport (the named pipe server does not exist) and no production consumer; `routing` is reachable only via the manual CLI
  - the verifier a route selects is never dispatched, so PENDING_VERIFICATION is terminal
next:
  - owner decision on whether to merge #213 into #211, then squash #211 into main
  - owner decision on whether `agent_bridge` gets a transport and a production consumer, or stays a library
  - follow-ups left open are the verifier dispatch loop, `FeedbackStore` reload, `authority_gate` implementation, codex `final.txt` and `final_message` persistence versus the goal-never-on-disk rule
---

# AGENT_ROUTING — coordination note

State card entry: `00_GOVERNANCE/VAULT_STATE.md` section 3 (routing and agent_bridge rows).
Protocol: `00_GOVERNANCE/protocols/AI_Memory_Vault_Agent_Routing_Protocol_V1.md`.
