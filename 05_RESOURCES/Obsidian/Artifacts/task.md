---
id: "art-9e844f0f"
type: artifact
lifecycle: ACTIVE
category: conversation-artifact
tags: [artifact, obsidian-sync, conversation-evidence]
created: 2026-08-24T21:30:00Z
updated: 2026-08-24T18:31:36.389103+00:00
provenance:
  source_type: execution
  source_ref: "task.md"
confidence: high
verification: verified
relations: []
---

# Artifact: task

# Task Checklist: Import Legacy Vaults

- [x] Write Python migration script `import_legacy_vaults.py`
  - [x] Support for global agents (Antigravity, Claude, Gemini)
  - [x] Support for `claude_original` files (prefix cleanup, collision handling)
  - [x] Support for `perplexity_original` files (mapping to appropriate folders, collision handling)
  - [x] Frontmatter injection (`provenance`, `confidence: medium`, `lifecycle: ARCHIVED` for legacy, `REVIEW` for global agents)
  - [x] Update `REVIEW_QUEUE.md`
- [x] Run the migration script
- [x] Validate results and generated files
- [x] Git commit and push the results
- [x] Create walkthrough artifact

