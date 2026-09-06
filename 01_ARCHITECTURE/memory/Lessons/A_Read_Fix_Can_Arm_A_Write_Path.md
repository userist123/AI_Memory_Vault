---
id: cf1e2487-4130-44fd-92ff-d3a8466cacac
type: lesson
lifecycle: REVIEW
category: engineering.diagnosis
tags: ['storage', 'data-loss', 'regression', 'method']
created: "2026-09-06"
updated: "2026-09-06"
provenance:
  source_type: 'execution'
  source_ref: 'session 2026-09-06: r001-r019, measured in-repo'
confidence: high
verification: unverified
relations: []
---

# Repairing a read path can arm a destructive write path

## Problem

`FileStorageEngine` scanned seven directories that no longer existed and loaded 0 notes. Fixing that made it see 738. Within the hour it emerged that `set()` resolved every write through `resolve_path()` and then deleted the note's previous file — so updating any note in `01_ARCHITECTURE` would write a copy into the legacy tree and remove the original.

## How it was found

The write bug predated the read fix and was inert only because `id_to_path` was empty: with no known paths, the delete branch never ran. It became reachable the moment the engine could see the corpus. An external reviewer spotted it in the diff; a targeted read of `set()` confirmed the exact lines.

## What fixed it

A note already living under a content root now keeps its exact path, directory and file name. The file name is not cosmetic there: Obsidian and `VaultIndex.by_slug` both resolve `[[links]]` by file name, so renaming on a category change would silently break every inbound link. Legacy notes keep their previous behaviour and new notes are still placed by `resolve_path()`.

## How it was verified

Tests covering: canonical update keeps the tree and the original file; a category change does not rename a canonical note; a type change does not drag it into the legacy tree; new notes still land in legacy; legacy notes keep renaming. Full suite green.

## Reuse this when

Whenever a change makes a component **see more** than it did — a wider scan, a new index, a larger corpus — enumerate what that component can now **write or delete**, and check each path. Dormant destructive code is not safe code; it is code whose preconditions were never met.

## Still open

The write taxonomy is still unmigrated: new notes land in `01_KNOWLEDGE` while the corpus lives in `01_ARCHITECTURE`. That is an owner decision, recorded rather than resolved.
