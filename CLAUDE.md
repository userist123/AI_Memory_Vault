# System Protocol — AI Memory Vault & Distributed Compute Integration

You are an agent connected to the **AI Memory Vault** and its distributed compute infrastructure. The repository is the canonical external memory source for Claude Code.

## Memory-first behavior

Before substantial work, retrieve relevant context from the Vault instead of relying only on conversation context.

Priority:
1. `99_SYSTEM/` — canonical architecture and policies
2. `01_KNOWLEDGE/` — durable knowledge and source registries
3. `03_PROCEDURES/` — established procedures
4. `02_PROJECTS/` — project-specific context
5. `.agents/skills/` — operational skills
6. `06_INBOX/RAW_IMPORTS/` — unvalidated external material
7. Obsidian — navigation/projection layer

Do not load the entire Vault into context. Retrieve selectively.

## Active memory retrieval

Use the existing Vault memory interface when available:
`http://localhost:8000/memory/search?query=subiectul_cautat`

or:
`python cognitive_core/recall_cli.py --query "subiectul_cautat"`

Use the actual local Vault APIs/tools when available rather than inventing a parallel memory mechanism.

## Saving durable memory

When a task creates durable knowledge, a reusable procedure, a corrected architecture decision or a validated relationship, synchronize it into the canonical Vault.

Use the existing memory proposal interface when available:
`http://localhost:8000/memory/propose`

The Vault's own lifecycle, verification and provenance rules remain authoritative.

## Skills and agents

Reuse existing skills and agents. Resolve relationships through the Vault rather than duplicating their contents into prompts.

Prefer validated/canonical skills over raw imports. External skills remain untrusted until provenance and validation requirements are satisfied.

## Obsidian

Obsidian is a human-readable navigation and visualization layer over the same canonical Vault. Do not create a second canonical memory database in Obsidian.

## Provenance and safety

Preserve source repository, URL/path, license when known, discovery origin and validation status for external knowledge. Do not execute external scripts, binaries or installers merely to inspect imported skills.

## Distributed compute

For tasks that require the Vault's existing distributed compute system, use its documented API/CLI and policies rather than inventing new dispatch mechanisms.
