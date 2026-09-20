# Audit task: the typed relations that are actually in the vault's graph

You are the independent evaluator. You did not write these notes, the proposer
that suggests relations, or the script that built this sample. That is the
point: whoever repairs a mechanism must not grade the repair.

## What you are looking at

`audit_sample_declared_50.json` holds 50 relations drawn from the 114 typed
relations present in the live graph, stratified by relation type with seed 42.
Every one of them is *declared* — written into a note's frontmatter by whoever
wrote the note, agent or human — and none has ever been audited.

Each row gives you: the relation type, both notes' title, path, lifecycle and
the first 1,200 characters of their text.

## What to decide, per row

**ACCEPT** only if the relation type is true for the content of both notes, in
the direction stated, and the text supports it.

**REJECT** otherwise, with one of these categories:

| Category | Meaning |
|---|---|
| `wrong_type` | there is a relationship, but not this one |
| `wrong_direction` | the relation is true the other way round |
| `unrelated` | no relationship of meaning between the notes |
| `shared_terms_only` | words in common, nothing more |
| `duplicate_content` | the two notes say the same thing |
| `unsupported` | the text does not let you tell either way |

`part_of` means containment: the target contains the source, or the source is a
component of the target. Being about the same topic is not containment.

`depends_on` means the source cannot stand without the target. Mentioning it is
not depending on it.

`applies_to` means the source is a lesson or rule that governs the target.

Note the lifecycle: several source notes are ARCHIVED, some of them archived
because they were written without source quotes. A relation declared by such a
note is not automatically wrong, but say so in your reasoning when it matters.

## What to deliver

One JSON file:

```json
{
  "schema": "declared-edge-audit-verdicts.v1",
  "evaluator": "<your name>",
  "sample_sha256": "815d00d131297670533ae2a6ac207b78f45333415c13612909ef271136ba1c1b",
  "verdicts": [
    {"index": 1, "verdict": "ACCEPT|REJECT", "category": null, "rationale": "one or two sentences, citing what in the text decides it"}
  ]
}
```

Plus a short summary: accepted per relation type, the commonest rejection
reason, and any row you were unsure about.

Do not tune your judgement toward any target precision. A result of 50/50 or
0/50 is a result.
