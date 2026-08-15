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

## Finding 1 — Existing graph was severely under-linked

The Vault contains substantially more structured metadata than actual Obsidian wikilinks. Most notes were therefore graph orphans even though they contain useful frontmatter, categories, lifecycle, provenance and relations.

This is a navigation problem, not evidence that the notes are semantically unrelated.

## Finding 2 — Existing Graph MOCs contained broken link names

Several existing MOCs used underscore-normalized targets such as `[[00_Core_Map]]` while the actual files are named with spaces, for example `00 Core Map.md`.

This pass corrects those targets to the actual note names/paths.

## Finding 3 — Memory is the dominant corpus

`04_MEMORY` contains **578** Markdown notes, dominated by lessons. It now has dedicated memory entry points for the major memory classes.

## Finding 4 — Agent evidence is a separate graph domain

`.agents` contains **236** Markdown artifacts. Agent evidence is indexed separately and is not promoted to canonical knowledge merely by being linked.

## Finding 5 — RAW_IMPORTS remains evidence, not canonical memory

`06_INBOX/RAW_IMPORTS` is linked through a dedicated source map. Imported material remains evidence/source material and is not silently promoted to canonical memory.

## Changes made

1. Corrected the seven existing Graph MOC link sets.
2. Added Knowledge Domains, Memory Subsystems, Agent Evidence, Imports/Sources, Templates/System, Projects/Procedures and Root/Control maps.
3. Added focused maps for Lessons, Decisions, Errors, Experiences and Preferences.
4. Added `.obsidian/graph.json` with folder-based color groups.
5. Added this health report.

## Coverage of the new maps

- `07 Knowledge Domains Map` covers the canonical domain folders present in the snapshot: Core, Knowledge, Projects, Procedures, Resources and System.
- `08 Memory Subsystems Map` currently provides the memory-class hubs and representative nodes; the 578-note corpus is intentionally not duplicated into a single giant MOC yet.
- `09 Agent Evidence Map` indexes representative primary artifacts from the agent runs; the full `.agents` corpus remains available through its folder and can be expanded by the graph generator.
- `10 Imports and Sources Map` indexes the principal Claude/Perplexity import sources; it does not alter RAW_IMPORTS.
- `11 Templates and System Map` covers the principal template and system governance notes.

## Important limitation

MOC links provide navigation edges; they do not claim that every linked note is semantically equivalent or directly related.

Semantic relations should continue to be represented by canonical frontmatter `relations` and deliberate wikilinks. We should not create artificial note-to-note relationships merely to make the graph look dense.

## External integrations

The supplied local snapshot did not contain imported implementations for `obsidian-local-llm-hub`, `obsidian-mcp` or `pmb`. They remain separate integration candidates until their actual contents are reviewed and mapped to this architecture.

## Next health target

After pulling this commit into the local Obsidian vault, inspect orphan count, broken links and cluster separation. The next useful pass is to generate targeted backlinks from canonical notes to their relevant MOCs based on folder/type/frontmatter, rather than blindly linking every note to every hub.
