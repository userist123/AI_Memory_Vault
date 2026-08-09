---
type: system
category: quality
status: active
version: 1.0.0
---

# Quality Control

## Note Quality Score

Evaluate:

- completeness;
- provenance;
- confidence;
- atomicity;
- link quality;
- freshness;
- verification.

## Minimum Standard

A permanent note should have:

- frontmatter;
- title;
- summary or statement;
- source/provenance where applicable;
- confidence;
- related links.

The enforceable schema, lifecycle and findings are defined by [[Canonical Frontmatter]] and [[Integrity Check]].`r`n`r`n## Retrieval Quality

Measure:

- precision;
- recall;
- relevance;
- redundancy;
- context usefulness;
- token cost.

## Failure Modes

### Hallucination

Response contradicts or invents knowledge.

### Retrieval Miss

Relevant note exists but was not retrieved.

### Retrieval Noise

Too many irrelevant notes were retrieved.

### Stale Memory

Old information was used despite newer verified information.

### Goal Drift

Reasoning left the requested objective.

### Memory Pollution

Weak or incorrect information became canonical.

## Corrective Actions

- improve metadata;
- improve chunking;
- improve graph edges;
- add confidence;
- add verification;
- archive obsolete notes;
- adjust retrieval weights.
