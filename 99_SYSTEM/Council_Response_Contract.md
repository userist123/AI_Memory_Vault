# Council Response Contract

## Purpose
Prevent token multiplication during specialist deliberation and final synthesis.

## Specialist response
Each specialist returns only:
```yaml
agent: <id>
decision: <short conclusion>
evidence:
  - <fact or observation>
risks:
  - <material risk>
confidence: <0..1>
needs_followup: <true|false>
```

Target: <= 600 tokens. Prefer substantially less.

## Lead synthesis
The lead receives specialist results, not their hidden reasoning or full prompts. It must:
- deduplicate conclusions;
- preserve conflicting evidence;
- select or explain the final decision;
- avoid restating every specialist response;
- return only the user-relevant result.

Target synthesis input: <= 2500 tokens.

## Forbidden
- Full chain-of-thought transfer between agents
- Copying another specialist's complete response into a new specialist prompt
- Repeating the original task in every handoff
- Recursive Council calls by specialists
- Narrative progress reports in runtime context

## Conflict handling
If specialists disagree, preserve the disagreement as compact evidence and resolve it using source quality, direct execution evidence, user constraints and validation. Do not manufacture consensus.
