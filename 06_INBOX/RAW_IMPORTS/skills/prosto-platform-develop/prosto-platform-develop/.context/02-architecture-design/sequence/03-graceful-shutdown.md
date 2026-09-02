# SEQ-03 Graceful Shutdown

Date: 2026-03-24  
Scope: Controlled runtime shutdown with timeout and reverse order stop

## Sequence Diagram

```mermaid
sequenceDiagram
  autonumber
  participant Op as Platform Operator
  participant Core as Core Bootstrap Controller
  participant Life as Lifecycle Orchestrator
  participant ModA as Module A
  participant ModB as Module B
  participant Bus as Event Bus
  participant Diag as Diagnostics Reporter

  Op->>Core: stop runtime
  Core->>Life: beginShutdown(timeout, strategy=reverseStartOrder)
  Life->>Bus: publish(hooks.beforeStop)

  Note over Life: stop in reverse startup order
  Life->>ModB: stop(ctx)
  ModB-->>Life: stopped|error
  Life->>ModA: stop(ctx)
  ModA-->>Life: stopped|error

  alt all modules stopped before timeout
    Life->>Bus: publish(hooks.afterStop)
    Core->>Diag: emitShutdownReport(success)
    Diag-->>Op: graceful shutdown complete
  else timeout or stop failures
    Life->>Core: forceFinalize(remainingModules)
    Core->>Diag: emitShutdownReport(partial, failedModules)
    Diag-->>Op: shutdown completed with issues
  end
```

## Notes
- Stop order is reverse of startup order to preserve dependency safety.
- Timeout is mandatory to prevent indefinite hangs.
- Shutdown report captures modules that failed to stop cleanly.

Related:
- [C4-03 Core Component View](../c4/03-component-view-kernel.md)
- [ADR-0004 Lifecycle Policies](../adr/ADR-0004-lifecycle-orchestration-and-startup-policies.md)
- [ADR-0007 Observability Baseline](../adr/ADR-0007-observability-and-operability-baseline.md)

