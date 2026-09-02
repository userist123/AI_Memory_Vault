# SEQ-04 Critical Module Failure During Startup

Date: 2026-03-24  
Scope: Failure handling for critical module by startup policy

## Sequence Diagram

```mermaid
sequenceDiagram
  autonumber
  participant Core as Bootstrap Controller
  participant Loader as Module Loader
  participant Life as Lifecycle Orchestrator
  participant Crit as Critical Module
  participant Policy as Startup Policy Evaluator
  participant Diag as Diagnostics Reporter
  participant Op as Platform Operator

  Core->>Loader: load(criticalModuleArtifact)
  Loader-->>Core: module instance loaded
  Core->>Life: init/start critical module
  Life->>Crit: init(ctx)
  Crit-->>Life: error(code=MODULE_INIT_FAILED)
  Life->>Policy: evaluate(error, criticality=critical, mode)

  alt mode = strict
    Policy-->>Core: abort startup
    Core->>Diag: emitFailure(moduleId, phase, errorCode, remediationHint)
    Diag-->>Op: startup failed
  else mode = best-effort
    Note over Policy: critical module still blocks startup by design
    Policy-->>Core: abort startup
    Core->>Diag: emitFailure(moduleId, phase, errorCode, remediationHint)
    Diag-->>Op: startup failed (critical dependency)
  end
```

## Notes
- Critical module failures always abort startup.
- `best-effort` applies only to non-critical modules.
- Failure payload must include phase, module identity, and remediation hint.

Related:
- [DFD-03 Module Loading L2](../dfd/03-module-loading-l2.md)
- [ADR-0003 Module Loading Security](../adr/ADR-0003-module-loading-security-allowlist-integrity.md)
- [ADR-0004 Lifecycle Policies](../adr/ADR-0004-lifecycle-orchestration-and-startup-policies.md)
