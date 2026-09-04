import type { IPlatformModuleLogger } from '@prosto/platform-sdk';

/**
 * @alpha
 * Options for creating a module logger.
 */
export interface ICreateModuleLoggerOptions {
  readonly moduleId: string;
}

/**
 * @alpha
 * Factory interface for creating module loggers.
 */
export interface IModuleLoggerFactory {
  create(options: ICreateModuleLoggerOptions): IPlatformModuleLogger;
}
