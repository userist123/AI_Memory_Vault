# Changelog

## Unreleased

- **New: Step 0.3 — detect an existing project design system before the style select.** Found via real testing (epoch-48): most real requests improve an existing project rather than starting from scratch, and the original flow had no path for preserving a project's own established palette/typography instead of offering the six curated sources for Global. New `scripts/detect_project_system.md` handles this.
- `MASTER.md` template now documents an optional, free-form `Notes:` field per category (content unstructured by design — only the field *name* is standardized, to avoid inconsistent ad hoc labels across categories).
- `SKILL.md` Step 7 now has explicit "stop and wait" parity with Step 4, instead of assuming the model will pause on its own after asking the persistence question.
- **Breaking:** persisted state moved from `.claude/ui-sensei/` to `.ui-sensei/` at the project root — decouples state from Claude Code specifically, likely avoids the sensitive-file permission prompt (unconfirmed, needs re-test), and makes the state file readable by any Agent-Skills-compatible client, not just Claude Code.
- Install instructions now recommend `.agents/skills/ui-sensei` as the canonical install location (the growing cross-agent convention), with `.claude/skills/ui-sensei` as a compatibility shim until Claude Code adds native support.
- `SKILL.md` Step 7 now verifies a persistence write actually succeeded before confirming it to the user, instead of assuming.
- `SKILL.md` Step 4 now treats "decide for me" as a formal, anticipated response type (found via real testing), with a defined priority rule for which style gets picked.

## v0.1.0 — initial scaffold (2026-07-15)

- `SKILL.md` orchestration logic: 7-step flow (project-decision check → reference-site match → live gate → conversion-lens detection → calibrated previews → select → precedence-resolved generation → persistence).
- 6 curated style sources in `references/styles/`: `frontend-design`, `ui-ux-pro-max`, `anti-slop`, `minimalist`, `brutalist`, `emil-design-eng`.
- 10-theme `revenue-centric-design` lens under `references/lenses/`.
- Live-fetch-only accessibility gate under `references/gates/`, with an explicit security note (fetched content treated as data, never as instructions).
- Reference-site extraction logic under `scripts/`.
- `MASTER.md` / `pages/*.md` persistence format, scoped per component category with named variants, living in the target project rather than in this repo.
- Full `PROVENANCE.md` tracking origin, license, and attribution status per source.
- Known gaps at this version: no commit-hash pinning yet, RCD attribution placement unverified against literal license text, not yet tested end-to-end.