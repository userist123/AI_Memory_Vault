# AGENTS.md — AI Memory System Operating Contract

## 0. Mission

This repository is the persistent memory and knowledge base for the user's AI system.

The goal is not to store every conversation. The goal is to preserve useful, reusable, verifiable knowledge and the history needed to understand how that knowledge was obtained.

The AI must protect the integrity of this memory and must minimize runtime context.

---

# 1. Source of Truth Hierarchy

When information conflicts, prefer sources in this order:

1. Explicitly confirmed by the user
2. Directly verified by execution/test
3. Official primary documentation
4. Project documentation maintained in this Vault
5. Repeated successful experience
6. Other external sources
7. AI-generated or inferred information

Never silently replace a stronger source with a weaker one.

When a conflict cannot be resolved, preserve both claims and create a conflict record.

---

# 2. Core Files

Before changing memory behavior, read only the core files required for that operation:

- `00_CORE/Identity.md`
- `00_CORE/Rules.md`
- `00_CORE/Memory_Protocol.md`
- `00_CORE/Confidence_Model.md`
- `00_CORE/System_Architecture.md`

For retrieval/import tasks also read only the specific applicable files from:

- `99_SYSTEM/Classification_Protocol.md`
- `99_SYSTEM/Import_Pipeline.md`
- `99_SYSTEM/Quality_Control.md`

Do not load all core files into every agent context.

---

# 3. Runtime Context Contract

The runtime MUST use sparse context.

## Hard defaults

```text
MAX_COUNCIL_AGENTS = 3
MAX_PRIMARY_AGENTS = 1
MAX_SKILLS_PER_AGENT = 2
MAX_MEMORY_RESULTS = 5
MAX_GRAPH_EXPANSION = 1 hop
MAX_SPECIALIST_OUTPUT = 600 tokens
MAX_SYNTHESIS_INPUT = 2500 tokens
```

These are defaults, not targets. Use fewer items whenever sufficient.

## Mandatory rules

1. Select agents before loading their skills.
2. Select skills before loading their full contents.
3. Never load every skill assigned to an agent merely because it is assigned.
4. An assigned skill is a capability index, not automatic runtime context.
5. Load a full `SKILL.md` only after the router has selected that skill for the current task.
6. Retrieve memory after intent classification and filter it before injection.
7. Do not inject the same memory, skill, instruction, or evidence into multiple council messages when a shared reference can be used.
8. Specialists must return compact evidence, not a second copy of the full task context.
9. The lead agent performs synthesis. Specialists do not recursively convene another council unless explicitly required.
10. Do not include Obsidian navigation links in runtime prompts unless the link itself is required to solve the task.

## Council escalation

Use one agent by default.

Use two agents when the task crosses one meaningful domain boundary.

Use three agents only when independent expertise materially reduces risk or uncertainty.

Do not use a larger council by default. If more than three specialists appear necessary, perform staged execution rather than parallel full-context deliberation.

---

# 4. Memory Is Not Conversation History

Do NOT automatically convert conversations into permanent memory.

A conversation may contain:

- temporary reasoning;
- mistakes;
- duplicate explanations;
- abandoned ideas;
- outdated information;
- speculation;
- irrelevant context.

Permanent memory must contain information that is useful after the original conversation is gone.

Use the following memory types:

- `knowledge`
- `project`
- `procedure`
- `decision`
- `experience`
- `error`
- `lesson`
- `preference`
- `resource`
- `hypothesis`

---

# 5. Before Creating a Note

Always:

1. Search for an existing note covering the same concept.
2. Check related notes.
3. Check for contradictions.
4. Determine the correct memory type.
5. Preserve provenance.
6. Assign confidence.
7. Add relevant `[[wikilinks]]`.
8. Avoid duplicating information.

If an existing note is substantially the same, update it instead of creating a duplicate.

---

# 6. Atomic Notes

Prefer one main concept per note.

Good:

`PowerShell_ExecutionPolicy.md`

Bad:

`Everything_I_Know_About_Windows.md`

A note may contain related information, but its purpose must be obvious from the title and frontmatter.

---

# 7. Frontmatter

Permanent notes should normally contain:

```yaml
---
id: "<stable UUID>"
type:
lifecycle: REVIEW
category:
tags: []
created:
updated:
provenance:
  source_type:
  source_ref:
confidence:
verification:
relations: []
---
```

Do not invent metadata values when they are unknown.

Use:

- `verified`
- `partially_verified`
- `unverified`
- `inferred`

for verification state.

---

# 8. Provenance

Whenever possible preserve:

- source platform;
- source conversation;
- source date;
- original file;
- URL;
- verification method.

Imported AI content must not be presented as independently verified merely because an AI generated it.

---

# 9. Import Rules

All new external AI memories enter:

`06_INBOX/RAW_IMPORTS/`

First preserve the raw source. Then, for a derivative only:

```text
RAW -> CLASSIFIED -> NORMALIZED -> REVIEW -> VERIFIED -> ACTIVE -> SUPERSEDED/ARCHIVED
```

`RAW` remains permanently in `06_INBOX/RAW_IMPORTS/`; it is never canonical memory and is never indexed as canonical knowledge. See `[[Memory Lifecycle]]` and `[[Canonical Frontmatter]]`.

Raw imports, audit reports, progress logs, handoffs, and generated briefings are evidence/artifacts and MUST NOT be injected into ordinary runtime context unless the current task explicitly concerns that artifact.

---

# 10. Deduplication

Before creating a new memory note, compare:

- title;
- subject;
- entities;
- claims;
- semantic similarity;
- project;
- date;
- source quality.

Similarity alone is not enough to merge notes.

Two similar notes may describe different environments, versions, projects, or outcomes.

---

# 11. Contradictions

If two memories disagree:

1. Do not choose arbitrarily.
2. Compare provenance.
3. Compare dates.
4. Compare environment/version.
5. Check whether both statements can be true under different conditions.
6. If unresolved, preserve both.
7. Mark the conflict.
8. Ask the user when the conflict materially affects a decision.

Never hide a contradiction.

---

# 12. Confidence

Use:

- `very_high`
- `high`
- `medium`
- `low`
- `unknown`

Confidence is not the same as truth.

A high-confidence statement should still have provenance.

AI inference normally starts at `low` or `medium` unless independently verified.

---

# 13. Retrieval Strategy

When searching memory, use a layered approach:

1. exact keyword search;
2. full-text/BM25 search;
3. semantic similarity;
4. metadata filtering;
5. graph relationships;
6. recency;
7. confidence;
8. project relevance.

Prefer a small set of highly relevant notes over a large amount of weak context.

Do not load the entire Vault into the model context.

Memory retrieval MUST stop once the context budget is satisfied. More retrieved notes are not automatically better.

---

# 14. Knowledge Graph / Synapses

Use `[[wikilinks]]` to represent relationships.

Useful relationship concepts include:

- `related_to`
- `depends_on`
- `caused_by`
- `solved_by`
- `supports`
- `contradicts`
- `implements`
- `used_by`
- `derived_from`
- `replaces`

Do not create links merely to increase graph density.

A link should have semantic meaning.

Graph traversal is runtime-limited to one hop by default. Expand further only when the first-hop evidence is insufficient.

---

# 15. Projects

Project notes represent current state.

Project information belongs under:

`02_PROJECTS/`

Projects may link to:

- knowledge;
- procedures;
- decisions;
- errors;
- lessons;
- resources.

When a project decision becomes generally reusable, extract it into permanent knowledge/procedure rather than leaving it buried in the project.

---

# 16. Procedures

A procedure must describe:

- purpose;
- scope;
- preconditions;
- dependencies;
- actions;
- expected results;
- failure handling;
- verification;
- rollback when applicable.

Never label an untested procedure as verified.

---

# 17. Errors and Learning

When an error is resolved, preserve:

```text
Error
  -> Root Cause
  -> Fix
  -> Verification
  -> Prevention
  -> Lesson
```

If the lesson is reusable, create a separate `lesson` note and link it to the original error.

Repeated errors should increase the priority of the corresponding lesson/procedure.

---

# 18. Decisions

A decision should preserve:

- problem;
- context;
- options;
- chosen option;
- rationale;
- expected outcome;
- risks;
- review trigger;
- result.

Do not erase previous decisions just because the system later changes direction.

Archive superseded decisions and explain why they were replaced.

---

# 19. User Preferences

Only store preferences that are:

- explicitly stated;
- stable enough to matter later;
- useful for future work.

Do not infer sensitive personal attributes.

Do not turn temporary instructions into permanent preferences unless clearly requested or repeatedly established.

---

# 20. Security

NEVER store:

- passwords;
- API keys;
- access tokens;
- private keys;
- authentication secrets;
- credentials.

If such material appears during import:

1. do not copy it into permanent memory;
2. flag it;
3. remove/redact it from processed memory;
4. preserve only the fact that a secret existed if that fact is useful.

---

# 21. Destructive Changes

Before deleting or mass-modifying memory:

- inspect affected files;
- preserve history;
- prefer Git;
- create a backup when appropriate;
- report what will change.

Do not perform destructive cleanup merely because files appear unused.

---

# 22. AI Goal Discipline

For every substantial task:

```text
INTENT
  -> CONSTRAINTS
  -> RELEVANT MEMORY
  -> PLAN
  -> ACTION
  -> VALIDATION
  -> MEMORY UPDATE
```

If reasoning starts drifting away from the user's objective:

1. stop;
2. restate the actual objective internally;
3. discard irrelevant branches;
4. continue from the last valid state.

Do not optimize for producing more text. Optimize for solving the actual task.

---

# 23. Tool Use

Before executing commands or changing infrastructure:

- inspect the environment;
- verify target;
- understand expected result;
- use the smallest sufficient action;
- capture actual output;
- validate after execution.

Never claim an operation succeeded without observing evidence of success.

---

# 24. Memory Write Decision

After completing meaningful work, ask:

> Is there something here that will make a future task materially better?

If no: do not write memory.

If yes, determine whether it is:

- knowledge;
- procedure;
- decision;
- error;
- lesson;
- experience;
- project state.

Store the smallest reusable representation.

---

# 25. Git

The Vault should be treated as a versioned knowledge repository.

Recommended workflow:

```text
Change
  -> Review diff
  -> Validate
  -> Commit
```

Never commit secrets.

---

# 26. Canonical Memory vs Raw Memory

Canonical memory:

- `00_CORE`
- `01_KNOWLEDGE`
- `02_PROJECTS`
- `03_PROCEDURES`
- `04_MEMORY`
- `05_RESOURCES`

Raw/import memory:

- `06_INBOX/RAW_IMPORTS`

System specifications:

- `99_SYSTEM`

Templates:

- `90_TEMPLATES`

Runtime-excluded operational artifacts:

- `.agents/auditor_*`
- `.agents/challenger_*`
- `.agents/*/BRIEFING.md`
- `.agents/*/DISPATCH.md`
- `.agents/*/handoff.md`
- `.agents/*/progress.md`
- `.agents/*/report.md`

These artifacts may be consulted explicitly for audit/review tasks but must not become default council context.

Raw imports are evidence, not automatically trusted knowledge.

---

# 27. Final Validation

Before finishing a memory operation, verify:

- correct folder;
- correct memory type;
- no unnecessary duplicate;
- provenance preserved;
- confidence assigned;
- relevant links added;
- secrets excluded;
- contradictions handled;
- source preserved;
- Markdown remains valid;
- runtime context remains within budget.

The system should prefer a smaller, cleaner, trustworthy memory over a larger polluted one.

---

# 28. Future Memory Controller

When the Memory Controller is implemented, it should expose operations conceptually equivalent to:

```text
search_memory(query)
read_memory(note)
find_related(note)
find_conflicts(note)
create_memory(note)
update_memory(note)
link_memory(a, b, relation)
archive_memory(note, reason)
validate_memory(note)
```

The controller must apply this document and the files in `00_CORE/` before writing canonical memory.

---

# 29. Council Runtime Contract

The authoritative runtime policy is:

`99_SYSTEM/Council_Context_Budget.md`

If another document suggests loading more agents, skills, memory, graph context, or audit artifacts by default, the sparse-context policy wins unless the user explicitly requests exhaustive analysis.

---

# 30. Prime Directive

The purpose of the memory is not to make the AI remember everything.

The purpose is to make the AI:

- remember the right things;
- retrieve the right things;
- know how confident those things are;
- understand how they are connected;
- learn from mistakes;
- preserve decisions;
- avoid repeating failures;
- remain aligned with the user's actual objective;
- solve tasks with the minimum sufficient context.

**Better memory beats more memory. Better routing beats more agents.**

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
