# Observability Rules

## Structured Logging (ADR-0007)

### Logger Configuration

**Use Pino for all logging:**

```typescript
import pino from 'pino';

const logger = pino({
  level: process.env.LOG_LEVEL || 'info',
  // Redact sensitive fields
  redact: {
    paths: ['*.password', '*.secret', '*.apiKey', '*.token', 'config.*'],
    remove: true
  },
  // Include timestamp
  timestamp: pino.stdTimeFunctions.isoTime
});
```

### Required Log Fields

**ALL logs MUST include:**

| Field | Type | Description |
|-------|------|-------------|
| `moduleId` | string | Module identifier |
| `phase` | string | Lifecycle phase (register/init/start/stop) |
| `correlationId` | string | Request/correlation ID for tracing |
| `errorCode` | string | Standardized error code (for errors) |

### Log Level Discipline

```typescript
// ERROR: Application cannot continue, requires immediate attention
logger.error({ err, moduleId }, 'Module failed to start');

// WARN: Unexpected but handled, may indicate future problem
logger.warn({ moduleId, version }, 'Module using deprecated API');

// INFO: Normal operational messages
logger.info({ moduleId, phase }, 'Module lifecycle phase completed');

// DEBUG: Detailed diagnostic information
logger.debug({ config, moduleId }, 'Module configuration loaded');
```

### Example: Lifecycle Logging

```typescript
class ModuleLifecycleOrchestrator {
  async executePhase(
    module: IPlatformModule,
    phase: LifecyclePhase,
    ctx: IModuleContext
  ): Promise<void> {
    const start = Date.now();
    
    logger.info({
      moduleId: module.manifest.id,
      phase,
      correlationId: ctx.correlationId
    }, 'Module lifecycle phase starting');

    try {
      await module[phase](ctx);
      
      logger.info({
        moduleId: module.manifest.id,
        phase,
        duration: Date.now() - start,
        correlationId: ctx.correlationId
      }, 'Module lifecycle phase completed');
      
    } catch (error) {
      logger.error({
        err: error,
        moduleId: module.manifest.id,
        phase,
        errorCode: 'LIFECYCLE_PHASE_FAILURE',
        correlationId: ctx.correlationId
      }, 'Module lifecycle phase failed');
      
      throw error;
    }
  }
}
```

---

## Startup Report

### Startup Diagnostics Contract

**Startup report MUST include:**

```typescript
interface IStartupReport {
  timestamp: string;
  duration: number;
  platformVersion: string;
  
  modules: {
    loaded: IModuleSummary[];
    skipped: ISkippedModule[];
    failed: IFailedModule[];
  };
  
  health: {
    status: 'healthy' | 'degraded' | 'unhealthy';
    criticalModulesLoaded: boolean;
    optionalModulesFailed: number;
  };
}

interface IModuleSummary {
  id: string;
  version: string;
  securityClass: SecurityClassType;
  criticality: 'critical' | 'standard' | 'optional';
  loadDuration: number;
}

interface ISkippedModule {
  id: string;
  reason: 'NOT_IN_ALLOWLIST' | 'INCOMPATIBLE_VERSION' | 'OPTIONAL_DISABLED';
  details?: string;
}

interface IFailedModule {
  id: string;
  phase: LifecyclePhase;
  errorCode: string;
  errorMessage: string;
  remediationHint?: string;
}
```

---

## Error Model

### Structured Error Codes

```typescript
const ErrorCodes = {
  // Module loading
  MODULE_NOT_FOUND: 'MODULE_NOT_FOUND',
  MODULE_LOAD_FAILED: 'MODULE_LOAD_FAILED',
  MODULE_VALIDATION_FAILED: 'MODULE_VALIDATION_FAILED',
  MODULE_INTEGRITY_FAILED: 'MODULE_INTEGRITY_FAILED',
  
  // Lifecycle
  LIFECYCLE_PHASE_FAILED: 'LIFECYCLE_PHASE_FAILED',
  LIFECYCLE_TIMEOUT: 'LIFECYCLE_TIMEOUT',
  LIFECYCLE_ORDER_VIOLATION: 'LIFECYCLE_ORDER_VIOLATION',
  
  // Compatibility
  INCOMPATIBLE_VERSION: 'INCOMPATIBLE_VERSION',
  MISSING_DEPENDENCY: 'MISSING_DEPENDENCY',
  CIRCULAR_DEPENDENCY: 'CIRCULAR_DEPENDENCY',
  
  // Security
  SECURITY_POLICY_VIOLATION: 'SECURITY_POLICY_VIOLATION',
  ALLOWLIST_REJECTED: 'ALLOWLIST_REJECTED',
  SIGNATURE_VERIFICATION_FAILED: 'SIGNATURE_VERIFICATION_FAILED'
} as const;
```

### Error Mapping

```typescript
interface IPlatformError {
  code: string;
  message: string;
  moduleId?: string;
  phase?: LifecyclePhase;
  remediationHint?: string;
  cause?: Error;
}

class ModuleLoadError extends Error implements IPlatformError {
  constructor(
    public readonly code: string,
    public readonly moduleId: string,
    public readonly phase?: LifecyclePhase,
    public readonly remediationHint?: string,
    cause?: Error
  ) {
    super(`Module ${moduleId} failed during ${phase}: ${cause?.message}`);
    this.name = 'ModuleLoadError';
    this.cause = cause;
  }
}

// Usage
throw new ModuleLoadError(
  ErrorCodes.MODULE_INTEGRITY_FAILED,
  'prosto-module-health',
  'register',
  'Verify checksum matches published artifact'
);
```

---

## Health & Readiness

### Health Check Endpoints

**Provided by adapter layer:**

```typescript
interface IHealthResponse {
  status: 'healthy' | 'degraded' | 'unhealthy';
  version: string;
  uptime: number;
  timestamp: string;
  checks: {
    name: string;
    status: 'pass' | 'fail' | 'warn';
    details?: string;
  }[];
}

async function getHealthStatus(): Promise<IHealthResponse> {
  const loadedModules = registry.getLoadedModules();
  const failedModules = registry.getFailedModules();

  const criticalOk = loadedModules
    .filter(m => m.criticality === 'critical')
    .length === expectedCriticalModules;

  return {
    status: criticalOk ? 'healthy' : 'unhealthy',
    version: platformVersion,
    uptime: process.uptime(),
    timestamp: new Date().toISOString(),
    checks: [
      {
        name: 'critical_modules',
        status: criticalOk ? 'pass' : 'fail',
        details: `${loadedModules.filter(m => m.criticality === 'critical').length}/${expectedCriticalModules} critical modules loaded`
      },
      {
        name: 'optional_modules',
        status: failedModules.length > 0 ? 'warn' : 'pass',
        details: `${failedModules.length} optional modules failed to load`
      }
    ]
  };
}
```

### Readiness Probe

```typescript
interface IReadinessResponse {
  ready: boolean;
  reasons: string[];
  modules: {
    registered: number;
    initialized: number;
    started: number;
  };
}

function getReadinessStatus(): IReadinessResponse {
  const reasons: string[] = [];
  
  if (!startupComplete) {
    reasons.push('Startup not complete');
  }
  
  if (failedCriticalModules.length > 0) {
    reasons.push(`${failedCriticalModules.length} critical modules failed`);
  }

  return {
    ready: reasons.length === 0,
    reasons,
    modules: {
      registered: moduleRegistry.registeredCount,
      initialized: moduleRegistry.initializedCount,
      started: moduleRegistry.startedCount
    }
  };
}
```

---

## Trace Propagation

### Correlation ID

```typescript
// Generate correlation ID for each request
function generateCorrelationId(): string {
  return crypto.randomUUID();
}

// Propagate through lifecycle
class ModuleContext {
  constructor(
    public readonly correlationId: string,
    public readonly moduleId: string,
    public readonly logger: Logger
  ) {}
}

// Usage in request handler
async function handleRequest(req: Request): Promise<Response> {
  const correlationId = req.headers['x-correlation-id'] || generateCorrelationId();
  const ctx = new ModuleContext(correlationId, moduleId, logger);
  
  logger.info({ correlationId, moduleId }, 'Processing request');
  
  try {
    return await module.handle(req, ctx);
  } catch (error) {
    logger.error({ err: error, correlationId, moduleId }, 'Request failed');
    throw error;
  }
}
```

---

## Metrics

### Startup Timing Metrics

```typescript
interface IStartupMetrics {
  totalDuration: number;
  phaseDurations: Record<LifecyclePhase, number>;
  moduleDurations: Record<string, number>;
  dependencyResolutionTime: number;
}

class MetricsCollector {
  private phaseTimings = new Map<string, number>();
  private moduleTimings = new Map<string, number>();

  startPhase(phase: LifecyclePhase): void {
    this.phaseTimings.set(phase, Date.now());
  }

  endPhase(phase: LifecyclePhase): number {
    const start = this.phaseTimings.get(phase);
    const duration = Date.now() - start!;
    this.metrics.phaseDurations[phase] = duration;
    return duration;
  }

  trackModuleLoad(moduleId: string, duration: number): void {
    this.moduleTimings.set(moduleId, duration);
    this.metrics.moduleDurations[moduleId] = duration;
  }
}
```

### Module-Level Metrics

```typescript
interface IModuleMetrics {
  moduleId: string;
  loadCount: number;
  unloadCount: number;
  failureCount: number;
  avgLoadDuration: number;
  lastLoadTime: string;
  lastError?: {
    code: string;
    phase: LifecyclePhase;
    timestamp: string;
  };
}
```

---

## Related Documents

- [ADR-0007 Observability And Operability Baseline](../../.context/02-architecture-design/adr/ADR-0007-observability-and-operability-baseline.md)
- [SEQ-03 Graceful Shutdown](../../.context/02-architecture-design/sequence/03-graceful-shutdown.md)
- [10 Phase - Internal MVP Validation](../../.context/04-implementation-plan/10-phase.md)
