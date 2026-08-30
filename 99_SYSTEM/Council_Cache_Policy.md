# Council Cache Policy

## Objective
Avoid re-sending identical context during repeated or staged Council work.

## Cacheable units
- Runtime contract version
- Agent identity manifest
- Skill manifest
- Normalized task classification
- Memory item IDs plus provenance
- Stable project constraints
- Validated evidence

## Do not cache
- Secrets
- Unvalidated claims
- User-private data without an approved memory policy
- Temporary credentials
- Tool results whose validity has expired

## Reuse rule
A cached unit may be reused only when its identity/version and applicability still match the current task.

## Context rule
Cache references are not a substitute for required content. The runtime should inject the smallest representation that the target model/tool can actually resolve.

## Invalidation
Invalidate cached context when:
- the underlying file changes;
- the skill version changes;
- the project constraint changes;
- the user explicitly corrects the information;
- validation evidence becomes stale;
- task scope changes materially.

## Goal
Repeated Council turns should transfer stable identifiers and compact evidence rather than replaying the same full context.
