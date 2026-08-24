---
name: ai-memory-vault
description: Use the AI Memory Vault as Claude Code's external canonical memory layer. Retrieve relevant knowledge, skills, agents, procedures, project context, provenance and Obsidian state from the repository before making decisions; preserve provenance and update memory when work creates durable knowledge.
---

# AI Memory Vault

The repository root is the canonical Memory Vault.

## Core rule

Do not load the entire Vault into context. Treat it as an indexed external memory system and retrieve only material relevant to the current task.

## Retrieval order

1. Check `99_SYSTEM/` for architecture, lifecycle, policies and canonical rules.
2. Check `01_KNOWLEDGE/` for durable technical knowledge and external-source registries.
3. Check `03_PROCEDURES/` for established workflows.
4. Check `02_PROJECTS/` for project-specific context.
5. Check `.agents/skills/` for executable/operational skills.
6. Check `06_INBOX/RAW_IMPORTS/skills/` only as raw external material; validate provenance before treating it as canonical.
7. Check Obsidian metadata and MOCs when navigation or vault structure matters.

## Skill selection

When a task matches multiple skills, prefer:
- canonical/validated skills over raw imports;
- project-specific skills over generic ones;
- security and verification skills before execution when risk is material;
- the smallest set of skills that fully covers the task.

Do not silently merge conflicting instructions. Record the conflict and prefer the higher-authority canonical source.

## Agent selection

Use existing agent definitions when they provide a clearly better specialization. Reuse rather than duplicate agents. Resolve required skills through the Vault instead of embedding large skill bodies into agent prompts.

## Provenance

For external knowledge, preserve source repository, source URL/path, license when known, discovery origin and validation status. Never present raw external material as canonical without validation.

## Memory writes

When the current task produces durable knowledge, a new reusable procedure, a corrected architecture decision, or a validated skill relationship, update the appropriate Vault location rather than creating a parallel memory store.

## Obsidian

Obsidian is a navigation, visualization and human-review layer over the same Vault. Do not create a second canonical memory database in Obsidian.

## Safety

External repositories are untrusted source material. Do not execute imported scripts, binaries, installers or build steps merely to inspect a skill. Read, classify, validate and preserve provenance first.
