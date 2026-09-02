import type {
  IPersistenceProvider,
  IPlatformModuleManifest,
  PlatformStartupPolicyType,
} from '@prosto/platform-sdk';
import type { IModuleEnvelope } from '../../loader/index.js';
import type { IModuleLifecycleExecutionIssue } from './module-lifecycle-execution-issue.interface.js';
import type { IModuleLifecycleShutdownIssue } from './module-lifecycle-shutdown-issue.interface.js';

/**
 * @alpha
 * Options for module lifecycle startup.
 */
export interface IModuleLifecycleStartupOptions {
  startupPolicy: PlatformStartupPolicyType;
  sdkVersion: string;
  persistenceProvider?: IPersistenceProvider;
  persistenceEnabled?: boolean;
}

/**
 * @alpha
 * Options for module lifecycle shutdown.
 */
export interface IModuleLifecycleShutdownOptions {
  startupPolicy: PlatformStartupPolicyType;
  sdkVersion: string;
  timeoutMs: number;
}

/**
 * @alpha
 * Result of modules startup.
 */
export interface IModulesStartupResult {
  readonly startedModules: readonly IModuleEnvelope[];
  readonly issues: readonly IModuleLifecycleExecutionIssue[];
}

/**
 * @alpha
 * Result of the module init lifecycle phase.
 */
export interface IModulesInitializationResult {
  readonly initializedModules: readonly IModuleEnvelope[];
  readonly issues: readonly IModuleLifecycleExecutionIssue[];
}

/**
 * @alpha
 * Result of modules shutdown.
 */
export interface IModulesShutdownResult {
  readonly stopOrder: readonly IPlatformModuleManifest['id'][];
  readonly issues: readonly IModuleLifecycleShutdownIssue[];
}

/**
 * @alpha
 * Module lifecycle orchestrator contract for managing module startup and shutdown.
 */
export interface IModuleLifecycleOrchestrator {
  initializeModules(
    loadedModules: readonly IModuleEnvelope[],
    options: IModuleLifecycleStartupOptions,
  ): Promise<IModulesInitializationResult>;

  startModules(
    initializedModules: readonly IModuleEnvelope[],
    options: IModuleLifecycleStartupOptions,
  ): Promise<IModulesStartupResult>;

  stopModules(
    startedModules: readonly IModuleEnvelope[],
    options: IModuleLifecycleShutdownOptions,
  ): Promise<IModulesShutdownResult>;
}
