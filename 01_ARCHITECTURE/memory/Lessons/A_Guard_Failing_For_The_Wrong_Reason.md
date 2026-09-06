---
id: 123fe8df-2490-44f0-b695-143c3d5a997f
type: lesson
lifecycle: REVIEW
category: engineering.diagnosis
tags: ['benchmark', 'false-positive', 'integrity', 'method']
created: "2026-09-06"
updated: "2026-09-06"
provenance:
  source_type: 'execution'
  source_ref: 'session 2026-09-06: r001-r019, measured in-repo'
confidence: high
verification: unverified
relations: []
---

# A guard that fires for the wrong reason hides the defect it was protecting

## Problem

The held-out benchmark refused to run: `FROZEN_SET_HASH_MISMATCH`. The obvious readings were tampering or a bad freeze. Both were wrong, and the real defect was far larger.

## How it was found

The recorded hash matched the file's content once CRLF was normalised to LF, and git reported the file unchanged. So the set was intact and the guard was hashing raw bytes on a Windows checkout — a platform-dependent false alarm that would pass on Linux CI. Because the guard aborted at the second line, nobody had ever run the benchmark to completion. Doing so revealed that all 48 cases referenced two gold notes that do not exist in the vault, making recall structurally 0 in every configuration.

## What fixed it

Canonical bytes were defined as UTF-8 with CRLF and lone CR normalised to LF, used identically when freezing and when verifying, plus `.gitattributes` pinning `eol=lf` so the conversion does not happen at all. The benchmark itself was rebuilt as v2 with gold anchored in real notes; v1 was kept and marked INVALID rather than repaired.

## How it was verified

Two properties proven rather than asserted: a CRLF round-trip leaves the digest identical, and altering one string still breaks it. A test enforces both, and a separate test fails if any gold id stops resolving in the corpus.

## Reuse this when

When a guard fires, establish **why** before either trusting it or bypassing it. A false alarm is not merely noise: it stops execution before the code under it runs, so it can conceal a defect much worse than the one it appears to report. Ask what the guard prevented you from reaching.

## Still open

Nothing on this specific defect. The rebuilt set carries only 10 graph cases against a ceiling of ~33, so it detects large effects and not subtle ones.
