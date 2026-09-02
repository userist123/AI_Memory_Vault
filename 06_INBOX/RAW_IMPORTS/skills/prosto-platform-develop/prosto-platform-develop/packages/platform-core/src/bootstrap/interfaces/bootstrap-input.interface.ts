import type {
  IPersistenceDescriptor,
  IPersistenceProvider,
  IPlatformRuntimeVersionContext,
  IServiceRegistry,
  PlatformStartupPolicyType,
} from '@prosto/platform-sdk';
import type { ModuleArtifactSourceDescriptorType } from '@/modularity/index.js';
import type { IPersistencePlatformConfig } from '@/runtime/index.js';

/**
 * @alpha
 * Input parameters for the bootstrap coordinator.
 */
export interface IBootstrapInput {
  readonly policyMode: PlatformStartupPolicyType;
  readonly runtimeVersion: IPlatformRuntimeVersionContext;
  readonly modules: readonly ModuleArtifactSourceDescriptorType[];
  readonly correlationId: string;
  readonly startupStartedAt: string;
  readonly persistenceProvider?: IPersistenceProvider;
  readonly persistenceConfiguration?: IPersistencePlatformConfig;
  readonly platformPersistenceDescriptor?: IPersistenceDescriptor;
  readonly services: IServiceRegistry;
}
