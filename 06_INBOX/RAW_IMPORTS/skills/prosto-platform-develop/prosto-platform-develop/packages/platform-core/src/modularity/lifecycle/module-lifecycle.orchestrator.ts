import type { PlatformModuleLifecycleStageType } from '@prosto/platform-sdk';
import {
  type IModuleContextFactory,
  type IModuleEnvelope,
  ModuleState,
} from '@/modularity/index.js';
import type {
  IModuleLifecycleContext,
  IModuleLifecycleExecutionIssue,
  IModuleLifecycleOrchestrator,
  IModuleLifecycleShutdownIssue,
  IModuleLifecycleShutdownOptions,
  IModuleLifecycleStartupOptions,
  IModulesInitializationResult,
  IModulesShutdownResult,
  IModulesStartupResult,
  ModuleStartupStagesType,
} from './interfaces/index.js';
import {
  executeWithTimeout,
  RuntimeErrorCodes,
  RuntimeStage,
} from '@/common/index.js';
import { ShutdownTimeoutError } from './module-lifecycle.errors.js';

/**
 * @alpha
 * Lifecycle orchestrator for managing module startup and shutdown.
 */
export class ModuleLifecycleOrchestrator implements IModuleLifecycleOrchestrator {
  constructor(private readonly _moduleContextFactory: IModuleContextFactory) {}

  async initializeModules(
    loadedModules: readonly IModuleEnvelope[],
    options: IModuleLifecycleStartupOptions,
  ): Promise<IModulesInitializationResult> {
    const lifecycleContext = this._createLifecycleContext(options);
    const initializedModules: IModuleEnvelope[] = [];
    const issues: IModuleLifecycleExecutionIssue[] = [];

    for (const moduleEnvelope of loadedModules) {
      moduleEnvelope.state = ModuleState.Initializing;

      try {
        await this._executeModuleStage(
          moduleEnvelope,
          'init',
          lifecycleContext,
        );

        moduleEnvelope.state = ModuleState.Initialized;
      } catch {
        moduleEnvelope.state = ModuleState.NotInitialized;
        lifecycleContext.persistenceProvider?.descriptors.rollback(
          moduleEnvelope.manifest.id,
        );

        issues.push(
          this._createStartupIssue(moduleEnvelope.manifest.id, 'init'),
        );

        continue;
      }

      initializedModules.push(moduleEnvelope);
    }

    return { initializedModules, issues };
  }

  async startModules(
    initializedModules: readonly IModuleEnvelope[],
    options: IModuleLifecycleStartupOptions,
  ): Promise<IModulesStartupResult> {
    const lifecycleContext = this._createLifecycleContext(options);
    const startedModules: IModuleEnvelope[] = [];
    const issues: IModuleLifecycleExecutionIssue[] = [];

    for (const moduleEnvelope of initializedModules) {
      moduleEnvelope.state = ModuleState.Starting;

      try {
        await this._executeModuleStage(
          moduleEnvelope,
          'start',
          lifecycleContext,
        );

        moduleEnvelope.state = ModuleState.Started;
      } catch {
        moduleEnvelope.state = ModuleState.NotStarted;

        issues.push(
          this._createStartupIssue(moduleEnvelope.manifest.id, 'start'),
        );

        continue;
      }

      startedModules.push(moduleEnvelope);
    }

    return { startedModules, issues };
  }

  /**
   * Run the shutdown lifecycle for all started modules.
   * Executes stop stage in reverse order with timeout.
   */
  async stopModules(
    startedModules: readonly IModuleEnvelope[],
    options: IModuleLifecycleShutdownOptions,
  ): Promise<IModulesShutdownResult> {
    const lifecycleContext: IModuleLifecycleContext = {
      startupPolicy: options.startupPolicy,
      sdkVersion: options.sdkVersion,
      persistenceEnabled: false,
    };
    const stopModules = [...startedModules].reverse();
    const issues: IModuleLifecycleShutdownIssue[] = [];

    for (const moduleEnvelope of stopModules) {
      const moduleId = moduleEnvelope.manifest.id;

      try {
        await executeWithTimeout(
          this._executeModuleStage(moduleEnvelope, 'stop', lifecycleContext),
          options.timeoutMs,
          () => new ShutdownTimeoutError(moduleId, options.timeoutMs),
        );
      } catch (error) {
        const isTimeoutError = error instanceof ShutdownTimeoutError;

        issues.push({
          moduleId,
          phase: RuntimeStage.Shutdown,
          errorCode: isTimeoutError
            ? RuntimeErrorCodes.ShutdownTimeout
            : RuntimeErrorCodes.ShutdownFailed,
          message:
            error instanceof Error ? error.message : 'Unknown shutdown error.',
          remediationHint: isTimeoutError
            ? `Ensure module "${moduleId}" stop() resolves before timeout.`
            : `Inspect module "${moduleId}" stop() implementation and dependencies.`,
        });
      }
    }

    return {
      issues,
      stopOrder: stopModules.map((module) => module.manifest.id),
    };
  }

  private _createLifecycleContext(
    options: IModuleLifecycleStartupOptions,
  ): IModuleLifecycleContext {
    return {
      startupPolicy: options.startupPolicy,
      sdkVersion: options.sdkVersion,
      persistenceProvider: options.persistenceProvider,
      persistenceEnabled: options.persistenceEnabled ?? false,
    };
  }

  private async _executeModuleStage(
    moduleEnvelope: IModuleEnvelope,
    stage: PlatformModuleLifecycleStageType,
    lifecycleContext: IModuleLifecycleContext,
  ): Promise<void> {
    const context = this._moduleContextFactory.create({
      lifecycleStage: stage,
      moduleManifest: moduleEnvelope.manifest,
      startupPolicy: lifecycleContext.startupPolicy,
      sdkVersion: lifecycleContext.sdkVersion,
      persistenceProvider: lifecycleContext.persistenceProvider,
      persistenceEnabled: lifecycleContext.persistenceEnabled,
    });

    await moduleEnvelope.module[stage](context);
  }

  private _createStartupIssue(
    moduleId: string,
    stage: ModuleStartupStagesType,
  ): IModuleLifecycleExecutionIssue {
    const reasonCodeMap: Record<ModuleStartupStagesType, RuntimeErrorCodes> = {
      init: RuntimeErrorCodes.LifecycleInitFailed,
      start: RuntimeErrorCodes.LifecycleStartFailed,
    };

    return {
      moduleId,
      phase: RuntimeStage.Lifecycle,
      lifecycleStage: stage,
      errorCode: reasonCodeMap[stage],
      message: `Module failed during ${stage}.`,
      remediationHint: `Inspect module "${moduleId}" ${stage} implementation and runtime dependencies.`,
    };
  }
}
