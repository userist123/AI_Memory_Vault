import type {
  IPersistenceDescriptor,
  IPersistenceProvider,
  IPlatformRuntimeVersionContext,
} from '@prosto/platform-sdk';

/**
 * @alpha
 * Configuration options for creating a platform runtime instance.
 */
export interface IRuntimeOptions {
  /**
   * Runtime version context
   */
  readonly runtimeVersion?: IPlatformRuntimeVersionContext;

  /**
   * Optional correlation ID for tracing
   */
  readonly correlationId?: string;

  /**
   * Optional persistence provider
   */
  readonly persistenceProvider?: IPersistenceProvider;

  /**
   * Optional platform persistence descriptor
   */
  readonly platformPersistenceDescriptor?: IPersistenceDescriptor;

  /**
   * Optional callback to execute when the runtime is stopping
   */
  readonly onStopped?: () => void | Promise<void>;
}
