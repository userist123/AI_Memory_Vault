# ADR-0004: Lifecycle Orchestration And Startup Policies

Date: 2026-03-24  
Status: Draft

## Context
Module activation must be deterministic and safe. Failures during startup should not lead to undefined runtime states.

## Decision
Standardize module lifecycle and policy behavior:
- Lifecycle phases are `register -> init -> start -> stop`.
- Dependency graph determines deterministic startup order.
- `stop` executes in reverse startup order with timeout.
- Startup policy modes:
  - `strict`: startup aborts on configured fatal failures.
  - `best-effort`: non-critical module failures are skipped with degraded startup report.
- Critical module failures always abort startup.

## Consequences

### Positive
- Predictable runtime behavior and cleaner failure handling.
- Better operational transparency through explicit startup/shutdown outcomes.
- Safer dependency management with ordered lifecycle transitions.

### Negative
- Module authors must conform to lifecycle contracts and avoid import-time side effects.
- More complexity in orchestrator implementation and diagnostics handling.

## Alternatives Considered
- Single-phase startup with implicit ordering: rejected due to weak control and diagnostics.
- Continue on all failures regardless of criticality: rejected due to availability and consistency risks.

## Related Artifacts
- [C4-03 Core Component View](../c4/03-component-view-kernel.md)
- [SEQ-01 Bootstrap Lifecycle](../sequence/01-bootstrap-lifecycle.md)
- [SEQ-03 Graceful Shutdown](../sequence/03-graceful-shutdown.md)
- [SEQ-04 Critical Module Failure](../sequence/04-critical-module-failure.md)

