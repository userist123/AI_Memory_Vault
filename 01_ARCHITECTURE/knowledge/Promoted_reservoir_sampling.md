---
id: "d7e9a1bd-341c-4da5-b482-6521c4e37146"
type: knowledge
lifecycle: REVIEW
category: procedures
tags: [ontology-promoted, procedures]
created: "2026-09-07"
updated: "2026-09-07"
provenance:
  source_type: import
  source_ref: "Sarfraz et al. (2022) SYNERgy - Synaptic Consolidation & Experience Replay"
  source_date: "2026-09-07"
  provenance_status: complete
  redaction: none
confidence: high
verification: unverified
relations:
  # Three separate mismatches with what SynapseStore.from_index() reads, each
  # of which silently produced no edge (synapse_store.py:234 does `continue`
  # when target_id is absent):
  #   target:            -> target_id, and it must be a NOTE ID, not a path
  #   relation:          -> type
  #   derived_from       -> not in ALLOWED_RELATIONS; it degraded to related_to
  # part_of is the weakest relation that is true here: the concept belongs to
  # the procedures slot rather than depending on or superseding it.
  - type: part_of
    target_id: slot-06-procedures
---

# Reservoir Sampling

## Canonical Definition

Reservoir sampling is an algorithmic procedure (Vitter, 1985) for randomly choosing a sample of k items from a list or stream of n items containing an unknown total count, ensuring every item has an equal probability of selection without requiring explicit stream boundaries or prior knowledge of total length.

## Judgment & Evaluation

Load-bearing algorithmic stream-processing procedure (Vitter, 1985) essential for online experience replay and continual learning without task boundaries.

## Code Cross-References

Potentially relevant to implementation modules in `03_IMPLEMENTATION/` or procedures in `10_DOCUMENTATION/procedures/`, not independently verified this package.
