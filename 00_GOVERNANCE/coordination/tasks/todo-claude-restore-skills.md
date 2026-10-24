# todo-claude-restore-skills

- **Task (owner decision 2026-10-24):** the capability registry named 18 skills absent from `.agents/skills/`.
- **Branch:** `claude/restore-lost-skills`
- **Finding (mapped to the individual steps of #196 `ea93ec3d2`, by vault-reviewer):**
  - 16 design skills: `9db4aaa93` removed only their `skillsweb/` duplicates; `f801fa385` ("remove 38 base64 demo pages", policy) then removed the root copies on purpose. A first draft of this PR called them "lost by mistake" — wrong; corrected before merge.
  - `ansible-automation`: removed by `82a3b44a3` as an unused connector, not for security.
  - `penetration-testing-playbook`: removed on purpose by `d699e9f52` (offensive-skill purge).
- **Owner decision:** do not restore the 16 design skills. Done: `ansible-automation` restored unchanged from `ea93ec3d2^` (scan SAFE); the 16 and the pentest skill dropped from the registry lists (132 ids remain, all present); registry note, VAULT_STATE and a test (`test_every_skill_the_capability_registry_names_exists`) updated.
- **Leftover outside this change:** `01_ARCHITECTURE/knowledge/EXTERNAL_SKILLS/_REGISTRY/SKILL_REGISTRY.json` still references the pentest skill.
- **Key files:** `.agents/skills/ansible-automation/`, `00_GOVERNANCE/protocols/Agent_Capability_Registry.md`, `20_TESTS/test_agent_docs_memory_interfaces.py`.
