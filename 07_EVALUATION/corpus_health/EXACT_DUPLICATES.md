# Exact duplicates — classified, nothing deleted

Measured on `10224498c`: SHA-256 over whitespace-normalised lowercase content,
across the 957 indexed notes. **5 groups, 13 files.** None of them is a
duplicated knowledge note.

The mandate is explicit that nothing may be deleted on similarity alone, so
this classifies and recommends; no file is touched here.

| # | Files | Size | What it is | Class | Recommendation |
|---|---:|---:|---|---|---|
| 1 | 5 | 166 chars | Five per-agent `CURRENT.md` coordination stubs, identical because they still hold the template text | live state files, not duplicates | Keep all five. Exclude `00_GOVERNANCE/coordination/**` from duplicate metrics: these files are meant to diverge as each agent writes its own state. |
| 2 | 2 | 1,888 chars | `DEPLOYMENT.md` — one copy under `projects/imported/`, one under `projects/workspaces/` | import beside its working copy | Canonical is the `workspaces` copy. Keep the import as raw provenance; mark it so it stops counting as a note. |
| 3 | 2 | 3,157 chars | `SECURITY.md` — same import/workspace pair | import beside its working copy | Same as above. |
| 4 | 2 | 217,093 chars | `ELITE_QUANT_BOT_COMPLETE_PROMPT.md`, twice inside the imported bot tree | duplicate inside an import | Owner's call: it is imported material in a personal project. Not vault knowledge either way. |
| 5 | 2 | 12,132 chars | `ELITE_QUANT_ARCHITECT_SYSTEM_PROMPT.md` and `... (1).md` | a re-downloaded file | Same as above. The `(1)` suffix is the signature of a browser download, not of a decision. |

## The finding behind the finding

Every one of the 13 files carries `lifecycle: NONE` and `type: unknown`. They
are not knowledge notes that got duplicated — they are project files the index
picks up because it scans those trees.

That reframes the duplicate question. Of the 957 indexed notes, 55 come from
`02_PRODUCT` (46 of them with no lifecycle) and 30 sit under an `imported/`
path. The corpus does not have a duplication problem; the index has a
population problem, and it is the same one behind the 148 notes with no
lifecycle counted in the Phase 0 baseline.

**What this does not say:** nothing here is about near-duplicates. No MinHash
pass has been run, no threshold declared. That stays open, and the threshold
has to be justified on a labelled set before it is applied — not chosen after
seeing which notes it would remove.
