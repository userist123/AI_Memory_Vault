import type {
  IPersistenceProvider,
  PlatformStartupPolicyType,
} from '@prosto/platform-sdk';

/**
 * @alpha
 * Lifecycle execution context shared across lifecycle stages.
 */
export interface IModuleLifecycleContext {
  readonly startupPolicy: PlatformStartupPolicyType;
  readonly sdkVersion: string;
  readonly persistenceEnabled: boolean;
  readonly persistenceProvider?: IPersistenceProvider;
}
