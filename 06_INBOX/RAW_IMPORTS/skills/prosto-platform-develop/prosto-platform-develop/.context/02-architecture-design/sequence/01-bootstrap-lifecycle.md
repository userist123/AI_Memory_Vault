# SEQ-01 Bootstrap Lifecycle

Date: 2026-03-24  
Scope: Runtime startup in normal path with policy-driven error branch

## Sequence Diagram

```mermaid
sequenceDiagram
  autonumber
  participant Op as Platform Operator
  participant CLI as Platform CLI
  participant Core as Core Bootstrap Controller
  participant Cfg as Config Engine
  participant Loader as Module Loader
  participant Val as Manifest Validator
  participant Compat as Compatibility Checker
  participant Graph as Dependency Resolver
  participant Life as Lifecycle Orchestrator
  participant Mod as Platform Module
  participant Diag as Diagnostics Reporter

  Op->>CLI: start runtime
  CLI->>Core: boot(configRef)
  Core->>Cfg: parseAndValidate(configRef)
  Cfg-->>Core: typedConfig(policy, allowlist, moduleRefs)

  Core->>Loader: discover(allowlist, moduleRefs)
  Loader-->>Core: moduleArtifacts[]

  loop for each module artifact
    Core->>Val: validateManifest(manifest)
    Val-->>Core: valid|invalid
    Core->>Compat: checkVersionAndCapabilities(manifest, runtime)
    Compat-->>Core: compatible|incompatible
  end

  Core->>Graph: resolveOrder(validatedModules)
  Graph-->>Core: startupOrder[]

  Core->>Life: execute(register->init->start, startupOrder)
  loop each module by startup order
    Life->>Mod: register(ctx)
    Mod-->>Life: ok
    Life->>Mod: init(ctx)
    Mod-->>Life: ok/fail
    Life->>Mod: start(ctx)
    Mod-->>Life: ok/fail
  end

  alt no fatal failures
    Core->>Diag: emitStartupReport(loaded, skipped, warnings)
    Diag-->>Op: startup success report
  else failures detected
    Core->>Life: evaluatePolicy(strict|best-effort, moduleCriticality)
    alt strict or critical failure
      Life-->>Core: abortStartup
      Core->>Diag: emitStartupFailure(reason, failedModule, phase)
      Diag-->>Op: startup failed
    else best-effort and non-critical failure
      Life-->>Core: continueWithoutModule
      Core->>Diag: emitDegradedStartup(skippedModules)
      Diag-->>Op: startup success with degradation
    end
  end
```

## Notes
- Lifecycle order is deterministic from dependency graph topological sort.
- Policy evaluation runs on every load/lifecycle error with module criticality context.
- Startup report is a required operational artifact.

Related:
- [DFD-03 Module Loading L2](../dfd/03-module-loading-l2.md)
- [ADR-0004 Lifecycle Policies](../adr/ADR-0004-lifecycle-orchestration-and-startup-policies.md)

