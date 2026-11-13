# todo-claude-analist-juridic

- Task: owner-authored skill `analist-juridic` (legal analyst, RO/EU law) added to `.agents/skills/`.
- Branch: feat/analist-juridic-skill
- Done (2026-11-13): SKILL.md, references/system_prompt.md, module_legale/ (_INDEX, _SABLON_modul); fara module de speta (cerinta proprietarului: doar skill-ul). Tests: skill_trust, personal_data_guard(+formats), navigation_links, untrusted_content_guard, claude_contract, agent_docs_memory_interfaces -> 155 passed.
- Next: owner review; listing in `skills.reviewed` (04_CONFIG/vault_domains.yaml) is a human decision, not done.
- Blockers: none. The skill stays trust=unverified until reviewed.
