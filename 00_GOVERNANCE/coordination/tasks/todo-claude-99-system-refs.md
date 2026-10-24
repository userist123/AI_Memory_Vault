# todo-claude-99-system-refs

- **Task:** VAULT_STATE §5 open defect — the 21 agent profiles and two skills cite `99_SYSTEM/*` files that do not exist.
- **Branch:** `claude/fix-99-system-refs`
- **Finding:** `dbcfbc502` deleted `99_SYSTEM/` as "redundant scaffold". Not redundant for two files: 20 of 21 profiles have no capability list of their own and pointed at `Agent_Capability_Registry.md` (the only agent → skill-id mapping; `Agents_Skill_Matrix.md` lists domains), and the council limits copied into profiles existed only in `Council_Context_Budget.md`.
- **Done:** both restored from `dbcfbc502^` to `00_GOVERNANCE/protocols/` with a provenance note; budget note says the limits are not enforced by code (`council_budget_controller.py` decides a tier only); registry note lists the 18 of 149 skills not in `.agents/skills/`. 21 profiles + 2 skills + `AGENTS.md` point there. `test_agent_docs_memory_interfaces.py` now checks every path each profile cites. VAULT_STATE §5 updated.
- **Not touched:** dated reports, exports and archives that mention `99_SYSTEM/` (07_EVALUATION, 99_META, 80_ARCHIVE, 50_ARTIFACTS, Obsidian artifacts): historical evidence.
- **Open (owner):** whether the council limits should be enforced in code, and whether the 18 missing skills are dropped from the registry or re-imported.
- **Key files:** `00_GOVERNANCE/protocols/Agent_Capability_Registry.md`, `00_GOVERNANCE/protocols/Council_Context_Budget.md`, `.agents/agents/*/agent.md`, `AGENTS.md`.
