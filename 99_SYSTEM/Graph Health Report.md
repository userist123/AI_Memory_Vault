---
type: system
group: graph
status: active
tags: [obsidian, graph, health, audit]
---
# Graph Health Report

## Source audit

This report was generated from the local `AI_Memory_Vault_CODEX_READY` snapshot supplied for graph review.

- Markdown files analyzed: **981**
- Existing source nodes with outgoing wikilinks: **45**
- Existing wikilink edges detected: **140**
- Notes with no incoming or outgoing wikilinks before this graph pass: **910**
- Frontmatter `relations` fields detected: **671**
- Non-empty frontmatter relation sets: **31**
- Notes typed as `lesson`: **564**
- Notes typed as `knowledge`: **35**
- Notes typed as `project`: **12**
- Agent Markdown artifacts under `.agents`: **236**
- RAW/source inbox Markdown files under `06_INBOX`: **45**

## Finding 1 — Existing graph was under-linked

The Vault contains substantially more structured metadata than actual Obsidian wikilinks. Most notes were therefore graph orphans even though they contain useful frontmatter, categories, lifecycle, provenance and relations.

This is a navigation problem, not evidence that the notes are semantically unrelated.

## Finding 2 — Existing Graph MOCs contained broken link names

Several existing MOCs used underscore-normalized targets such as `[[00_Core_Map]]` while the actual files are named with spaces, for example `00 Core Map.md`.

This graph pass corrects those targets to the actual note names/paths.

## Finding 3 — Memory is the dominant corpus

`04_MEMORY` contains **578** Markdown notes, dominated by lessons. It now has a dedicated memory subsystem entry point and separate maps for Lessons, Decisions, Errors, Experiences and Preferences.

## Finding 4 — Agent evidence is a separate graph domain

`.agents` contains **236** Markdown artifacts. These are indexed through `09 Agent Evidence Map` rather than being treated as canonical knowledge.

## Finding 5 — RAW_IMPORTS remains evidence, not canonical memory

`06_INBOX/RAW_IMPORTS` is linked through `10 Imports and Sources Map`. The map explicitly preserves the distinction between source material and canonical notes.

## Changes made

1. Corrected the existing Graph MOC link targets.
2. Added Knowledge Domains, Memory Subsystems, Agent Evidence, Imports/Sources, Templates/System, Projects/Procedures and Root/Control maps.
3. Added focused maps for Lessons, Decisions, Errors, Experiences and Preferences.
4. Added this health report.
5. Added `.obsidian/graph.json` with folder-based color groups.

## Important limitation

MOC links provide navigation edges; they do not claim that every linked note is semantically equivalent or directly related.

Semantic relations should continue to be represented by canonical frontmatter `relations` and deliberate wikilinks.

## External integrations

The supplied local snapshot did not contain imported implementations for `obsidian-local-llm-hub`, `obsidian-mcp` or `pmb`. They should remain separate integration candidates until their actual contents are reviewed and mapped to this architecture.

## Next health target

After pulling this commit into the local Obsidian vault, inspect orphan count, broken links and cluster separation. Graph density alone is not a semantic-quality metric.
