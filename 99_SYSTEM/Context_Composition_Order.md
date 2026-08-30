# Context Composition Order

This is the canonical order for constructing any Council specialist prompt.

```text
1. System/runtime contract
2. User task
3. Selected agent identity
4. Selected skill manifest
5. Full selected skill body (only if required)
6. Top relevant memory
7. Relevant project/file evidence
8. Specialist output contract
```

## Never inject by default
- Other agents' full prompts
- Other agents' full skill bodies
- Entire capability registry
- Entire skill catalog
- Entire Council map
- Entire Knowledge Graph
- Whole Vault
- Historical reports
- Progress/handoff/dispatch/briefing artifacts
- Previous specialist reasoning unless explicitly required

## Deduplication
Each unique instruction/evidence item may appear once in a specialist context. Shared information belongs in the shared prefix or lead synthesis context, not copied into every specialist prompt.

## Budget behavior
If the context exceeds budget, reduce in this order:
1. low-relevance memory
2. optional examples
3. graph expansion
4. secondary evidence
5. additional specialists

Never solve an over-budget context by truncating safety-critical or task-critical instructions first.
