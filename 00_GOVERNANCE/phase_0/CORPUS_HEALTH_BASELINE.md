---
id: cb0155f4-d99c-4fd0-b4c7-2326604c70f4
type: procedure
lifecycle: REVIEW
category: governance.phase0
tags: ['phase-0', 'reconciliation', 'measured']
created: "2026-09-20"
updated: "2026-09-20"
provenance:
  source_type: 'execution'
  source_ref: 'phase 0 reconciliation measured on origin/main 10224498c, 2026-09-20'
confidence: high
verification: unverified
relations:
  - type: part_of
    target_id: slot-06-procedures
---

# Corpus health baseline — Phase 0

Measured on `10224498c` with `VaultIndex.load(include_raw=True,
include_archived=True)` and `SynapseStore.from_index`.

## Notes

| Measure | Value |
|---|---:|
| Notes in the index | 957 |
| Notes visible to `FileStorageEngine` | 841 |
| Notes from tests or fixtures inside the index | **0** |

The two populations differ by design: they scan overlapping but distinct roots,
and storage requires a frontmatter `id`. They are not the same set and must
never be reported as one.

### Lifecycle

| Lifecycle | Notes |
|---|---:|
| ARCHIVED | 569 |
| REVIEW | 175 |
| (none) | 148 |
| ACTIVE | 57 |
| NORMALIZED | 4 |
| RAW | 4 |

59% of the corpus is archived, mostly the 552 template lessons. Only 57 notes
are ACTIVE. 148 carry no lifecycle at all — that is the population Wave A has
to decide about, because a note with no lifecycle passes no lifecycle filter.

### Verification, read from frontmatter over 1,084 notes

| Value | Notes |
|---|---:|
| `unverified` | 723 |
| missing | 230 |
| `verified` | 82 |
| `verified_source` | 26 |
| `inferred` | 20 |
| `partially_verified` | 2 |
| `not_applicable` | 1 |

`verified` and `verified_source` are two spellings of one idea, which a schema
guard should collapse. 230 notes with no verification field is a gap:
attestation is the owner's alone, so a missing value must not read as verified.

**UNVERIFIED:** the index itself exposes no `verification` attribute — every
note reports `None` through `note.metadata`. The table above is read from the
files. Whether `search()` can filter on verification at all is a Wave A
question.

## Duplicates

5 groups of exact duplicates, covering 13 notes, by SHA-256 over whitespace-
normalised lowercase content. Small enough to resolve by hand; none deleted
here, because Phase 0 only measures.

Near-duplicates: **UNVERIFIED**. No MinHash/LSH pass has been run, and any
threshold has to be declared and justified on a labelled set first.

## Graph

| Measure | Value |
|---|---:|
| Edges | 513 |
| — declared / inferred / wikilink | 220 / 218 / 75 |
| Edges pointing at a note that does not exist | **0** |
| Edges from fixtures | 0 (no fixture note is in the index) |

Precision, from the 50-edge audit in PR #172: **3/25 for strong relations**
(`depends_on`, `supersedes`) and 16/25 for weak ones (`related_to`, `part_of`).
By type: `supersedes` 0/5, `depends_on` 3/20, `part_of` 4/10, `related_to`
12/15. Only `related_to` is trustworthy. Measured on the sample, not the whole
set, so it is not an estimate of the population.
