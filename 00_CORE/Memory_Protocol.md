---
type: core
category: memory
status: active
version: 1.0.0
---

# Memory Protocol

## Memory Classes

| Type | Meaning |
|---|---|
| knowledge | fapt / concept reutilizabil |
| project | stare si context de proiect |
| procedure | pasi verificati |
| decision | alegere si rationale |
| experience | eveniment sau experienta |
| error | esec analizat |
| lesson | regula invatata din experienta |
| preference | preferinta stabila |
| resource | sursa externa |
| hypothesis | idee neconfirmata |

## Write Rules

Create a new note when the information is:

- reusable;
- distinct;
- stable enough;
- relevant to future work.

Update an existing note when:

- the same concept exists;
- the new information improves accuracy;
- the old version should remain as history.

Do not store when:

- it is trivial;
- it is duplicated;
- it is purely conversational noise;
- it contains secrets;
- it is obsolete without historical value.

## Memory Lifecycle

```text
RAW -> CLASSIFIED -> NORMALIZED -> REVIEW -> VERIFIED -> ACTIVE -> SUPERSEDED/ARCHIVED
```

`RAW` is permanent source evidence in `06_INBOX/RAW_IMPORTS/`. Only a derivative can be classified, normalized, reviewed, verified, and promoted. Raw evidence is never rewritten, deleted, or indexed as canonical memory.

## Provenance

Every imported memory should retain, when possible:

- source platform;
- source conversation;
- source date;
- extraction date;
- confidence;
- verification state.`r`n`r`nUse the canonical schema in [[Canonical Frontmatter]]. Any normalized or redacted derivative must reference its original raw path. Promotion to `ACTIVE` follows [[Promotion and Human Review]].
