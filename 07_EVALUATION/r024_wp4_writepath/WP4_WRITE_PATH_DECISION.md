# WP-4 — write-path decision document

`storage/path_resolver.py` maps a note type to the legacy tree (`knowledge`
→ `01_KNOWLEDGE`, `procedure` → `03_PROCEDURES`, etc.). The corpus lives in
`01_ARCHITECTURE` and the other numbered content roots. This document
enumerates three options with their numbers. **No migration was performed.
`path_resolver.py` was not touched.** This is an architecture decision for
the vault owner, not resolved here.

Numbers below are produced by `gather_numbers.py` (committed alongside this
document) against the real vault; see `writepath_numbers.json` for the raw
output.

## The number that reframes the question

**0 of 850 tracked notes currently live in any legacy write root.**
`01_KNOWLEDGE/` exists as an empty tracked directory; `00_CORE`,
`02_PROJECTS`, `03_PROCEDURES`, `04_MEMORY`, `05_RESOURCES`, `99_SYSTEM` do
not exist at all in this checkout. Every note ever committed to this vault
lives in a content root (`01_ARCHITECTURE`: 734, `02_PRODUCT`: 55,
`10_DOCUMENTATION`: 22, `00_GOVERNANCE`: 39).

This means the legacy write path has never actually produced a note that
made it into the tracked vault. It has only ever been exercised by local,
uncommitted writes (the `test_00000000.md` / `unknown_A.md` / `unknown_B.md`
files found under a contributor's untracked `01_KNOWLEDGE/` — see r024
WP-0's finding and r005's `unknown_A.md` finding).

**A currently live, concrete consequence, not a hypothetical one:**
`retrieval.vault_index.VaultIndex.DEFAULT_ROOTS` — the root list the graph
(`SynapseStore.from_index()`), the benchmark harness, and every VaultIndex-
based tool scan — is exactly the four content roots. It does **not**
include any legacy write root. So a note proposed today through the real
production path (`MemoryController.propose()` → `FileStorageEngine.set()`
→ `path_resolver.resolve_path()` → a legacy root) becomes searchable via
`MemoryController.search()` (which scans `CANONICAL_FOLDERS` = content +
legacy roots) but **invisible to the graph, to `VaultIndex`, and to every
benchmark or analysis tool built on `VaultIndex`** — until someone notices
and moves it. This is not a risk the split creates for the future; it is
what already happens to any note proposed today.

## Cross-reference numbers

| | value |
|---|---:|
| declared relations (frontmatter `relations:`) resolving to another note | 113 |
| — crossing the content/legacy boundary | **0** |
| Obsidian wikilinks resolving to another note | 485 |
| — crossing the content/legacy boundary | **0** |

Zero cross-boundary references in either direction, for the same reason as
above: there is nothing in the legacy roots for a content-root note to
reference, or vice versa. A migration in either direction breaks no
*existing* cross-references — the risk is entirely about tooling that scans
by root name, not about link integrity between notes.

## Option A — migrate the corpus to the legacy taxonomy

Move all 850 tracked notes from the four content roots into the seven
legacy roots, by `type` (the mapping `path_resolver.py` already encodes).

- **Notes that move:** 850.
- **Inbound references that move:** 0 break (no cross-boundary references
  exist to break), but every internal tool that hardcodes `CONTENT_ROOTS` or
  `DEFAULT_ROOTS` as the scan target must be updated: `retrieval/vault_index.py`'s
  `DEFAULT_ROOTS`, `memory/storage/file_engine.py`'s `CONTENT_ROOTS`/
  `CANONICAL_FOLDERS`, `20_TESTS/test_storage_canonical_roots.py` (asserts
  `CONTENT_ROOTS == DEFAULT_ROOTS`, would need rewriting, not just re-pointing),
  and `VAULT_STATE.md`'s own component-reality table.
- **What breaks during the move:** the content roots are currently organised
  *by subject* (`01_ARCHITECTURE/knowledge/legal/primary/`,
  `01_ARCHITECTURE/memory/Lessons/`, `02_PRODUCT/projects/imported/...`),
  not by `type`. The legacy taxonomy is organised *by type only*
  (`01_KNOWLEDGE`, `03_PROCEDURES`, one flat level). Moving 850 notes into
  it either flattens the existing subject structure (legal/, Lessons/,
  imported/ subdirectories disappear) or requires `path_resolver.py` to grow
  a second dimension (type + subject) it does not have today — this option
  is a reorganisation, not a relocation. Frozen benchmark sets
  (`heldout.json`/`dev.json`) key gold notes by id, not path, so they
  survive a path-only move; they do not survive silently if ids are
  regenerated during a reorganisation.
- **Recovery path:** a pure `git mv` (no reorganisation) is a single revert.
  A reorganisation that also changes ids or merges directories is not
  cleanly revertible by `git revert` alone and would need its own explicit
  recovery plan before starting — not proven here, and by this document's
  own forbidden list, not attempted here.
- **Cost of leaving alone another month:** none beyond what Option B/C also
  carry — this option does not close the DEFAULT_ROOTS gap any differently
  than doing nothing, since it is the higher-cost, higher-risk direction to
  actually execute.

## Option B — migrate the write path to the content roots

Change `path_resolver.py`'s mapping so a new note of type `knowledge` lands
under `01_ARCHITECTURE/knowledge/` (where 01_ARCHITECTURE's actual knowledge
notes already live) instead of `01_KNOWLEDGE/`, and similarly for the other
six types.

- **Notes that move:** 0. Nothing has ever been durably written to a legacy
  root, so there is nothing to relocate.
- **Inbound references that move:** 0 (same reason).
- **What breaks during the move:** in principle, nothing existing — this is
  a redirection of *future* writes, not a relocation of present data. The
  main design work is deciding the *subject* subdirectory for each type
  (content roots are organised by subject, not type alone — e.g. does a new
  `knowledge` note land in `01_ARCHITECTURE/knowledge/` unconditionally, or
  does it need a subject/category-derived subdirectory the way the existing
  corpus has one?). That mapping does not exist today and would need to be
  designed, which is real but bounded work — no data migration risk.
- **Recovery path:** revert the `path_resolver.py` mapping change. Since no
  existing note is touched, there is no data to lose or restore.
- **Cost of leaving alone another month:** every note anyone proposes in
  that time keeps landing outside `DEFAULT_ROOTS`, invisible to the graph
  and to `VaultIndex`-based tooling until manually relocated — the live
  consequence described above, recurring for every new note.

## Option C — keep both, under an explicit rule

Formalise the current state: writes go to the legacy roots, reads scan the
union (as `file_engine.py`'s own docstring already documents as deliberate),
and — the part that is not yet explicit anywhere — new notes are routinely
and manually relocated into a content root as part of promotion, or
`DEFAULT_ROOTS` is deliberately extended to include the legacy roots too.

- **Notes that move:** 0 immediately; the rule would need to say whether
  existing/future legacy-root notes are relocated on some cadence, and by
  what mechanism (manual, a script, a lifecycle-transition hook).
- **Inbound references that move:** 0.
- **What breaks:** nothing immediately; this option's entire cost is that
  the `DEFAULT_ROOTS` gap above stays live and undocumented as a rule rather
  than as an accident, unless the explicit rule specifically closes it (e.g.
  "propose() also inserts the legacy write root into DEFAULT_ROOTS for that
  session" or "a promotion step relocates the file"). Writing the rule down
  without deciding that mechanism does not by itself fix the gap.
- **Recovery path:** not applicable — no code changes.
- **Cost of leaving alone another month:** identical to Option B's ongoing
  cost (new notes keep landing outside `DEFAULT_ROOTS`) unless the "explicit
  rule" is written to include a concrete fix for that gap, in which case its
  cost converges with Option B's.

## What this document is not

Not a recommendation. Options B and C are numerically far cheaper to
execute than A (0 notes and 0 references move under either, versus 850 notes
requiring either a hardcoded-root update sweep or a structural reorganisation
under A) — that is a fact about the numbers, not a decision about which
tradeoff the vault owner should accept. Option A may still be right if the
type-based legacy taxonomy is where the owner wants the corpus organised
long-term; that is a design preference this document does not have standing
to resolve.

## What remains open

- The DEFAULT_ROOTS gap is real and live today, independent of which option
  is chosen — it is a defect in its own right (a proposed note becomes
  invisible to the graph/VaultIndex layer), arguably urgent enough to
  warrant its own fix regardless of which of A/B/C is eventually decided.
  Recorded here, not fixed here (WP-4 forbids any path_resolver.py change,
  and this specific fix would touch DEFAULT_ROOTS or the propose() path,
  either of which is a decision this document explicitly defers).
- Option A's "reorganisation vs. relocation" distinction was not sized in
  detail (e.g. how many notes would need a *new* subject-subdirectory
  decision versus a mechanical type-only move) — doing so would require
  designing the type+subject mapping Option A implies, which this document
  does not do.
