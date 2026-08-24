---
type: system
category: classification
status: active
version: 1.0.0
id: "eb05eda0-6632-4038-ba93-b45492b093cc"
document_kind: policy
document_status: active
provenance_status: incomplete
relations: []
policy_scope: vault-governance
---

# Classification Protocol

## Goal

Transform raw conversations into atomic, reusable memory.

## Categories

### Knowledge

Stable fact or concept.

### Project

Current work with state, tasks and decisions.

### Procedure

Repeatable method.

### Decision

Choice plus rationale.

### Experience

What happened.

### Error

Failure with cause and resolution.

### Lesson

Generalizable learning extracted from experience/error.

### Preference

Stable user preference explicitly supported by evidence.

### Hypothesis

Plausible but unverified idea.

### Resource

Source material, not extracted knowledge.

## Extraction Rules

1. Remove greetings and conversational filler.
2. Remove duplicate explanations.
3. Preserve important context.
4. Preserve source and date.
5. Separate facts from opinions.
6. Split unrelated concepts.
7. Link related notes.
8. Assign confidence.
9. Flag contradictions.
10. Never infer personal facts without evidence.

11. Preserve the raw original in `06_INBOX/RAW_IMPORTS/` and record it in a derivative's provenance.`r`n12. Do not promote a candidate to `ACTIVE` before the review gate in [[Promotion and Human Review]].`r`n`r`n## Classification Priority

If an item fits multiple classes, create the smallest number of notes that preserve meaning.

Example:

`Error -> Lesson -> Procedure`

can create three linked notes when each is independently reusable.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[11 Templates and System Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
