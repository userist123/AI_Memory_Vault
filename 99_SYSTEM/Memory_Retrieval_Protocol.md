# Memory Retrieval Protocol

## Objective
Retrieve the smallest evidence set that materially improves the current task.

## Pipeline
```text
TASK
 -> INTENT CLASSIFICATION
 -> QUERY NORMALIZATION
 -> CANDIDATE RETRIEVAL
 -> RELEVANCE GATE
 -> DEDUPLICATION
 -> BUDGET GATE
 -> MEMORY PACK
```

## Runtime defaults
- Maximum returned memory items: 5
- Maximum graph expansion: 1 hop
- Prefer canonical knowledge over raw imports.
- Prefer project-specific evidence when the task is project-specific.
- Do not retrieve by folder-wide inclusion.
- Do not expand from one memory item to all linked notes.

## Relevance gate
A memory item is included only when it contributes facts, constraints, decisions, procedures, failure knowledge, or evidence needed for the current task.

Navigation-only links, broad topic similarity, historical reports, and unrelated project notes do not qualify.

## Deduplication
If multiple notes express the same fact, retain the strongest source by provenance and keep one representative item.

## Budget fallback
When over budget, remove in order:
1. weak semantic matches
2. duplicated facts
3. navigation-only material
4. low-value historical context
5. graph-expanded material

Never remove user-confirmed constraints or safety-critical evidence solely because it is inconvenient.

## Output contract
The retrieval layer should return compact evidence with provenance, relevance and reason for inclusion. It should not return a narrative summary unless explicitly requested.
