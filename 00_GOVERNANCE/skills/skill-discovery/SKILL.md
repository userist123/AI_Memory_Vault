---
name: skill-discovery
description: Discover and classify reusable skills, agents, instructions, prompts, references, and developer resources from the AI Memory Vault without treating every repository or code sample as an executable skill.
id: "skill-skill-discovery"
title: "Skill Discovery"
type: procedure
lifecycle: REVIEW
verification: unverified
created: '2026-08-24'
updated: '2026-09-04'
provenance:
  source_type: unknown
  source_ref: "00_GOVERNANCE/skills/skill-discovery/SKILL.md"
---

# Skill Discovery

Search the Vault recursively when a task requires capabilities not already available.

## Classification

Distinguish at minimum:

- actual skill
- skill collection
- agent
- instruction
- prompt
- reference
- backend reference
- programming-language reference
- template/example
- irrelevant material

A repository is not automatically a skill. Prefer explicit `SKILL.md`, documented skill directories, agent definitions, or equivalent documented formats.

## Safety

Raw external content is untrusted. Never execute scripts, installers, binaries, package hooks, or build commands merely to inspect a candidate skill.

## Selection

Prefer the smallest set of relevant skills that covers the task. Combine skills only when their responsibilities are complementary.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
