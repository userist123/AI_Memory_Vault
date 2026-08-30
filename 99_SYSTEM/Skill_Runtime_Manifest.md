# Skill Runtime Manifest

Purpose: keep skill discovery cheap and make full skill loading explicitly on-demand.

## Runtime rule

A skill directory may be discovered from its manifest, but `SKILL.md` MUST NOT be loaded until the router selects that skill for the current task.

## Required metadata

Each skill should expose, where applicable:

```yaml
id: <stable skill id>
domain: <primary domain>
topics: [<1-8 keywords>]
load_policy: on_demand
cost_class: low|medium|high
conflicts: []
prerequisites: []
```

## Context rules

- Discovery uses `id`, `domain`, and `topics` only.
- `conflicts` and `prerequisites` are metadata, not full context.
- Full skill instructions are loaded only after selection.
- A skill must not require loading the global registry, council map, or knowledge graph home note.
- Navigation links are documentation metadata and are excluded from runtime prompts.
- If two selected skills substantially overlap, keep the smallest sufficient skill and reject the duplicate.

## Selection budget

```text
MAX_SKILLS_PER_AGENT = 2
MAX_TOTAL_SELECTED_SKILLS = 4
```

If more skills appear necessary, stage the task instead of increasing the default context.

## Runtime payload

The preferred payload is:

```yaml
skill_id:
why_selected:
required_inputs: []
full_content: <loaded only after selection>
```

The router should pass `why_selected` and required inputs, not the entire skill catalog.
