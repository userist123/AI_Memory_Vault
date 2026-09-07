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
  - relation: derived_from
    target: "01_ARCHITECTURE/ontology/slots/06_procedures.md"
---

# Reservoir Sampling

## Canonical Definition

Reservoir sampling is an algorithmic procedure (Vitter, 1985) for randomly choosing a sample of k items from a list or stream of n items containing an unknown total count, ensuring every item has an equal probability of selection without requiring explicit stream boundaries or prior knowledge of total length.

## Judgment & Evaluation

Load-bearing algorithmic stream-processing procedure (Vitter, 1985) essential for online experience replay and continual learning without task boundaries.

## Code Cross-References

Potentially relevant to implementation modules in `03_IMPLEMENTATION/` or procedures in `10_DOCUMENTATION/procedures/`, not independently verified this package.
