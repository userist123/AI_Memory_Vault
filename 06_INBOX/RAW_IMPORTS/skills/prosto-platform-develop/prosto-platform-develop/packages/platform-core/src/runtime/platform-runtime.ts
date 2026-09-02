import type { IBootstrapCoordinator } from '@/bootstrap/index.js';
import type {
  IDiagnosticsReporter,
  IRuntimeFailureDiagnostic,
  IRuntimeOperationalReports,
} from '@/diagnostics/index.js';
import { RuntimeStartupStatus } from '@/diagnostics/index.js';
import type {
  IModuleEnvelope,
  IModuleLifecycleOrchestrator,
  IModuleLifecycleShutdownIssue,
  ModuleArtifactSourceDescriptorType,
} from '@/modularity/index.js';
import type {
  IPlatformConfig,
  IPlatformRuntime,
  IRuntimeOptions,
} from './interfaces/index.js';
import {
  type IServiceRegistry,
  type PlatformStartupPolicyType,
  SDK_CONTRACT_VERSION,
} from '@prosto/platform-sdk';
import {
  assert,
  dateNowIso,
  RuntimeErrorCodes,
  RuntimeStage,
} from '@/common/index.js';

/**
 * @alpha
 * Platform runtime implementation that orchestrates
 * the bootstrapping process and modules lifecycle.
 */
export class PlatformRuntime implements IPlatformRuntime {
  private _startedModules: readonly IModuleEnvelope[] = [];
  private _stoppingPromise: Promise<void> | null = null;

  private readonly _startupPolicy: PlatformStartupPolicyType;
  private readonly _correlationId: string;

  constructor(
    private readonly _modules: readonly ModuleArtifactSourceDescriptorType[],
    private readonly _config: Readonly<IPlatformConfig>,
    private readonly _diagnosticsReporter: IDiagnosticsReporter,
    private readonly _bootstrapCoordinator: IBootstrapCoordinator,
    private readonly _moduleLifecycleOrchestrator: IModuleLifecycleOrchestrator,
    private readonly _services: IServiceRegistry,
    private readonly _options: IRuntimeOptions = {},
  ) {
    this._startupPolicy = this._config.platform.startupPolicy;
    this._correlationId = this._createCorrelationId(
      this._options.correlationId || this._config.runtime.correlationId,
    );
  }

  get startedModuleIds(): readonly string[] {
    return this._startedModules.map(
      (moduleEnvelope) => moduleEnvelope.manifest.id,
    );
  }

  private _started = false;

  get started(): boolean {
    return this._started;
  }

  private _degraded = false;

  get degraded(): boolean {
    return this._degraded;
  }

  private _stopped = false;

  get stopped(): boolean {
    return this._stopped;
  }

  private _reports: IRuntimeOperationalReports = {};

  get reports(): IRuntimeOperationalReports {
    return this._reports;
  }

  async start(): Promise<void> {
    if (this._started) return;

    const startupStartedAt = dateNowIso();
    const policyMode = this._startupPolicy;

    const bootstrapContext = await this._bootstrapCoordinator.coordinate({
      policyMode,
      startupStartedAt,
      modules: this._modules,
      correlationId: this._correlationId,
      runtimeVersion: this._options.runtimeVersion ?? {
        sdkVersion: SDK_CONTRACT_VERSION,
        nodeVersion: process.versions.node,
      },
      persistenceProvider: this._options.persistenceProvider,
      platformPersistenceDescriptor:
        this._options.platformPersistenceDescriptor,
      persistenceConfiguration: this._config.persistence,
      services: this._services,
    });

    const failedDiagnosticsByModuleId = new Map<
      string,
      IRuntimeFailureDiagnostic
    >(
      bootstrapContext.failedDiagnostics.map((diagnostic) => [
        diagnostic.moduleId,
        diagnostic,
      ]),
    );

    const startupReport = this._diagnosticsReporter.createStartupReport({
      policyMode,
      startedAt: startupStartedAt,
      correlationId: this._correlationId,
      failedModules: bootstrapContext.failedDiagnostics,
      loadedModules: bootstrapContext.loadedModules.map((moduleEnvelope) => ({
        moduleId: moduleEnvelope.manifest.id,
        version: moduleEnvelope.manifest.version,
      })),
      skippedModules: bootstrapContext.skippedModuleIds.map((moduleId) => {
        const reason = failedDiagnosticsByModuleId.get(moduleId);
        assert(reason, `Failed diagnostic for module ${moduleId} not found.`);
        return { moduleId, reason };
      }),
    });

    this._reports = { startup: startupReport };
    this._started = startupReport.status !== RuntimeStartupStatus.Failed;
    this._degraded = startupReport.degraded;
    this._startedModules = bootstrapContext.loadedModules;

    if (!this._started && this._isPersistenceEnabled()) {
      await this._options.persistenceProvider?.dispose();
    }
  }

  async stop(): Promise<void> {
    if (this._stopped) return;

    let resolveStoppingPromise: (() => void) | undefined;

    if (this._stoppingPromise) {
      return this._stoppingPromise;
    } else {
      this._stoppingPromise = new Promise(
        (resolve) => (resolveStoppingPromise = resolve),
      );
    }

    const shutdownStartedAt = dateNowIso();
    const issues: IModuleLifecycleShutdownIssue[] = [];

    const shutdownResult = await this._moduleLifecycleOrchestrator.stopModules(
      this._startedModules,
      {
        startupPolicy: this._startupPolicy,
        sdkVersion:
          this._options.runtimeVersion?.sdkVersion ?? SDK_CONTRACT_VERSION,
        timeoutMs: this._config.runtime.shutdownTimeoutMs,
      },
    );

    issues.push(...shutdownResult.issues);

    if (this._isPersistenceEnabled()) {
      try {
        await this._options.persistenceProvider?.dispose();
      } catch {
        issues.push({
          moduleId: 'platform',
          phase: RuntimeStage.Shutdown,
          errorCode: RuntimeErrorCodes.ShutdownFailed,
          message: 'Persistence provider disposal failed.',
          remediationHint: 'Inspect persistence provider shutdown diagnostics.',
        });
      }
    }

    try {
      await this._options.onStopped?.();
    } catch {
      issues.push({
        moduleId: 'platform',
        phase: RuntimeStage.Shutdown,
        errorCode: RuntimeErrorCodes.ShutdownFailed,
        message: 'Runtime service cleanup failed.',
        remediationHint: 'Inspect runtime service cleanup diagnostics.',
      });
    }

    const shutdownReport = this._diagnosticsReporter.createShutdownReport({
      startedAt: shutdownStartedAt,
      correlationId: this._correlationId,
      stopOrder: shutdownResult.stopOrder,
      issues,
    });

    this._reports = { ...this._reports, shutdown: shutdownReport };
    this._stopped = true;
    this._started = false;

    if (resolveStoppingPromise) {
      resolveStoppingPromise();
      this._stoppingPromise = null;
    }
  }

  private _createCorrelationId(seed?: string): string {
    if (seed && seed.trim()) {
      return seed;
    }

    return `rt-${Date.now()}-${Math.random().toString(16).slice(2, 10)}`;
  }

  private _isPersistenceEnabled(): boolean {
    return this._config.persistence?.typeorm?.enabled === true;
  }
}
