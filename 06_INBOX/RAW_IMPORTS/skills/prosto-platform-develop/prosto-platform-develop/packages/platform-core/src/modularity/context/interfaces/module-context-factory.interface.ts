import type {
  IPersistenceProvider,
  IPlatformModuleContext,
  IPlatformModuleManifest,
  PlatformModuleLifecycleStageType,
  PlatformStartupPolicyType,
} from '@prosto/platform-sdk';

/**
 * @alpha
 * Options for creating module contexts.
 */
export interface ICreateModuleContextOptions {
  readonly startupPolicy: PlatformStartupPolicyType;
  readonly sdkVersion: string;
  readonly moduleManifest: IPlatformModuleManifest;
  readonly lifecycleStage: PlatformModuleLifecycleStageType;
  readonly persistenceEnabled: boolean;
  readonly persistenceProvider?: IPersistenceProvider;
}

/**
 * @alpha
 * Factory for creating module contexts.
 */
export interface IModuleContextFactory {
  create(options: ICreateModuleContextOptions): IPlatformModuleContext;
}
