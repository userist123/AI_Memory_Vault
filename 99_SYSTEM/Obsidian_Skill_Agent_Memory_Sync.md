---
title: Obsidian Skill-Agent-Memory Synchronization
status: canonical
layer: system
purpose: synchronization map between skills, agents, instructions, prompts, references and Memory Vault knowledge
---

# Obsidian — Skill / Agent / Memory Synchronization

This note is the canonical Obsidian synchronization map for the Memory Vault.

## Synchronization principle

```text
RAW EXTERNAL SKILLS
        ↓
INGESTION / VALIDATION
        ↓
SKILL REGISTRY
        ↓
AGENT CAPABILITIES
        ↓
MEMORY / KNOWLEDGE DOMAINS
        ↓
OBSIDIAN NAVIGATION + RELATION MAP
```

Raw external material remains untrusted until the Vault ingestion lifecycle accepts it. Obsidian is the navigation and human-auditable knowledge surface; it is not a second memory engine.

## Canonical Vault surfaces

- [[README]] — Vault entry point
- [[06_INBOX/README]] — ingestion boundary
- [[06_INBOX/RAW_IMPORTS/README]] — raw external imports
- [[01_KNOWLEDGE/README]] — canonical knowledge layer
- [[02_MEMORY/README]] — memory layer
- [[03_COGNITIVE_CORE/README]] — cognitive layer
- [[99_SYSTEM/README]] — system and governance layer

## Skill synchronization

Primary raw skill boundary:

`06_INBOX/RAW_IMPORTS/skills/`

Skill sources must retain provenance and must not silently become canonical knowledge.

Registry surfaces:

- [[06_INBOX/RAW_IMPORTS/skills/_REGISTRY.json]]
- [[06_INBOX/RAW_IMPORTS/skills/_SOURCE_REGISTRY.json]]
- [[06_INBOX/RAW_IMPORTS/skills/_DISCOVERY_GRAPH.json]]
- [[06_INBOX/RAW_IMPORTS/skills/_DEDUPLICATION.json]]
- [[06_INBOX/RAW_IMPORTS/skills/_LICENSES.json]]
- [[06_INBOX/RAW_IMPORTS/skills/_VALIDATION_REPORT.md]]

## Agent synchronization

Agents consume capabilities from skills and references. Agent definitions must not fork the canonical skill registry.

Use this relationship:

```text
Agent
 ├── invokes → Skill
 ├── constrained by → Instruction / Rule
 ├── uses → Reference
 ├── produces → Memory Candidate / Artifact
 └── governed by → System / Lifecycle
```

Agent definitions belong to the agent layer of the Vault. Skills remain reusable capability units.

## Memory synchronization

Use the following semantic direction:

```text
Skill / Reference
      ↓
Agent execution
      ↓
Observed result / candidate
      ↓
Verification
      ↓
Memory candidate
      ↓
Canonical memory / knowledge
```

Do not write raw external skill content directly into canonical memory.

## Obsidian linking rules

1. Prefer `[[Wikilinks]]` for internal Vault navigation.
2. Link agents to the skills they actually consume.
3. Link skills to their source/provenance records.
4. Link references to the knowledge domain they support.
5. Link memory concepts to the originating verified knowledge, not to an arbitrary external repository.
6. Never create a duplicate canonical note merely because an external source has the same topic.
7. Keep raw external material under `06_INBOX/RAW_IMPORTS/` until accepted by lifecycle/verification.

## Capability taxonomy

### Programming

Language, compiler, runtime, package manager, formatter, linter, language server and developer-tool skills/references.

### Backend

API, authentication, authorization, databases, ORM, caching, queues, testing, observability, architecture and deployment.

### Web

Accessibility, performance, SEO, UI engineering, frontend/backend integration and web quality.

### Design

UI/UX, design systems, visual hierarchy, interaction design and design-engineering skills.

### Agents

Task-specific agents that orchestrate skills and references.

### System

Lifecycle, provenance, validation, security, governance and memory architecture.

## Synchronization contract

The synchronization is considered valid when:

- every imported skill has provenance;
- every skill has a stable registry identity;
- agents reference skills by identity/path rather than copying skill instructions;
- references remain distinguishable from skills;
- memory candidates have a verification path;
- Obsidian provides navigation without becoming a competing source of truth;
- raw external sources remain traceable to their original repository and commit.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[11 Templates and System Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
