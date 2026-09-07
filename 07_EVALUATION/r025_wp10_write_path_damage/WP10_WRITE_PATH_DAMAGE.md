# WP-10 — write-path damage measured at zero notes today; the vulnerability itself is unchanged and fully live

package: WP-10 | intent: verify (measurement only) | status: DONE
baseline: r024 WP-4's finding — 0 of 850 tracked notes live in any legacy write root, `path_resolver.py`
still targets those roots for every new note
result: **0 notes currently sit in any legacy write root** — no measurable damage has actually
accumulated yet | n: 850 content-root notes checked, 7 legacy roots scanned | decision: report the
gap as zero-today-but-fully-live, not as resolved; no `path_resolver.py` change made, per this
package's own restriction.

## What was measured

`measure_write_path_damage.py` scans every file under the 7 `LEGACY_WRITE_ROOTS`
(`00_CORE, 01_KNOWLEDGE, 02_PROJECTS, 03_PROCEDURES, 04_MEMORY, 05_RESOURCES, 99_SYSTEM` —
`FileStorageEngine`'s own constant), the exact set `path_resolver.resolve_path()` maps every new
`propose()`-created note into, and cross-references each against `VaultIndex` (`DEFAULT_ROOTS` =
`01_ARCHITECTURE, 02_PRODUCT, 10_DOCUMENTATION, 00_GOVERNANCE` only — the graph layer, every
benchmark harness this session, and `SynapseStore.from_index()` all read through this).

| check | result |
|---|---:|
| notes currently in any legacy write root | **0** |
| of those, added to git after the storage-fix commit (`da99af0f6`, 2026-09-06) | 0 (n/a — none exist) |
| of those, invisible to `VaultIndex` right now | 0 (n/a — none exist) |
| legacy-root directories that exist on disk at all | 1 of 7 (`01_KNOWLEDGE`, empty) |

**"Since the storage fix" is measured as "since commit `da99af0f6`"** ("fix(storage): production
storage engine could not see the vault at all", 2026-09-06) — the commit that let `FileStorageEngine`
see the 850-note corpus at all, the natural reference point for "since the write layer became usable
in practice." No note anywhere in the vault's current git history occupies a legacy root, before or
after that commit.

## What this means: the damage is zero, the exposure is not

r024 WP-4's finding (0/850 in a legacy root) still holds exactly, unchanged, months of session-time
later. **No one has actually called `propose()` against the real, file-backed `MemoryController` yet**
— every `propose()` call this vault's own test suite exercises runs against the in-memory
`StorageEngine` fixture (confirmed: `20_TESTS/memory_controller/test_cache.py`'s fixture and its
siblings), never `FileStorageEngine`. The write-path defect r024 WP-4 documented is therefore not
"contained" — it is simply **untriggered**. `path_resolver.resolve_path()` is unmodified since WP-4
looked at it (per this package's own restriction, still unmodified here) and would place the very
next real `propose()` call against the live vault into a directory `VaultIndex` never scans — 6 of the
7 target directories do not even exist on disk yet and would be silently created by the first such
call.

## Smallest fix and its cost (restated from WP-4's own options, costs updated with today's zero-note count)

WP-4 enumerated three options without recommending one, per its own brief. Today's zero count changes
the COST of two of them, not the options themselves:

| option | description | notes to move (measured today) |
|---|---|---:|
| A | Reorganize legacy taxonomy into content roots (type-only -> subject-based) | 850 (all of `01_ARCHITECTURE` and friends) — a full corpus reorganization, not a relocation |
| B | Redirect future writes only: point `path_resolver.py` at a content root | **0** — nothing exists in a legacy root to migrate; this is now a pure forward-looking fix with zero migration cost |
| C | Status quo, formalized (document that new notes are graph/VaultIndex-invisible until manually relocated) | 0 — nothing to formalize retroactively; only the risk statement itself |

Option B's cost dropped from "hypothetical" (WP-4's own framing) to **concretely zero migration
cost**, because zero notes have ever actually landed in the legacy tree to migrate. This is a fact
about cost, not a recommendation — per this package's own scope, no option is chosen or implemented
here.

## Forbidden — honoured

No change to `path_resolver.py`. No migration performed or proposed as an action. Measurement only.

## What remains open

- The exposure remains live: the next real `propose()` call against the file-backed
  `MemoryController` (by any future package, script, or production usage) will reproduce r024 WP-4's
  defect for real, immediately, with no measurement or test currently guarding against it.
- This report does not add a regression test asserting `propose()` writes land in a content root (that
  would itself be a `path_resolver.py`-adjacent decision, arguably in scope for whichever future
  package picks an option above) — noted as a gap, not filled here.
- r025 WP-6 (this session, immediately prior) found `MemoryController.update()` itself unusable for
  most real notes (lifecycle gate, id-format schema, corrupt date fields) — a related but distinct
  finding about the MUTATION path in general. WP-10's own finding is narrower and specifically about
  WHERE `propose()` physically places new files, not whether `update()` can subsequently touch them.
