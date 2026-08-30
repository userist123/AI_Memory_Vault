---
name: global-skill-registry-router
description: Sparse router for discovering and loading only the minimum skill or tool required by the current task.
---

# Global Skill Registry Router

This skill provides capability discovery across local and global skill registries. It is a router, not a reason to load large registries or multiple skills into context.

## Progressive Disclosure Contract

The runtime MUST use:

```text
INDEX -> RANK -> SELECT -> LOAD -> EXECUTE
```

Never:

```text
INDEX -> LOAD EVERYTHING -> ASK MODEL TO CHOOSE
```

## Local Registry

The local Vault contains the active skills under `.agents/skills/`.

The registry is metadata first. A skill's existence, name, domain and short description may be indexed without loading its full `SKILL.md`.

## Global Registry

Global sources may be queried on demand. Do not import or inject a large external index into the model context.

Global registries are discovery sources only until a specific skill/tool is selected.

## Selection Rules

1. Classify the task first.
2. Select the smallest relevant domain.
3. Rank candidate skills by task relevance.
4. Select at most 2 skills per agent by default.
5. Load full skill content only for selected skills.
6. Stop when the selected skills are sufficient.
7. Never load unused skill references, demos, corpora or examples automatically.

## Council Interaction

The router MUST respect `99_SYSTEM/Council_Context_Budget.md`.

Assigned skills in an `agent.md` are capabilities, not automatic prompt content.

The presence of 198 local skills or 50,000+ discoverable global skills must never increase the default runtime context size.

## Output

Return a compact selection result:

```yaml
selected_skills: []
rejected_skills: []
reason: ""
confidence: 0.0
```

Do not return the full text of rejected skills.

## Safety

Never load secrets or credential material from skill registries.

Prefer local verified skills over dynamically downloaded skills when both satisfy the task.

Validate dynamically acquired skills before using them in consequential actions.
