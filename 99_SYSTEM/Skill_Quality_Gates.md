# Skill Quality Gates

A skill is runtime-ready only if it passes all gates below.

## Gate 1 — Identity
- Has one canonical skill ID.
- Has one clear domain/purpose.
- Does not duplicate another skill's primary purpose.

## Gate 2 — Runtime economy
- No full-Vault loading.
- No automatic loading of sibling skills.
- No recursive Council invocation.
- No repeated global Council/memory instructions.
- Navigation links are metadata unless directly required.

## Gate 3 — Capability integrity
- Domain procedure remains intact.
- Required constraints remain intact.
- Required verification/failure handling remains intact.
- Examples are retained when they materially affect execution.

## Gate 4 — Retrieval
- Manifest metadata is sufficient for routing.
- Full body is loaded only after selection.
- A skill should normally be loaded only when its relevance score crosses the configured threshold.

## Gate 5 — Conflict safety
- A skill must not silently override canonical system policies.
- If domain rules conflict with runtime safety/context limits, canonical runtime policy wins.

## Gate 6 — Regression
Before accepting a compressed/normalized skill, compare its capability coverage with the previous version. A reduction in token count is not success if execution capability is lost.

## Status vocabulary
- `PASS`: safe for runtime
- `WARN`: usable but should be normalized
- `FAIL`: must not be loaded automatically
