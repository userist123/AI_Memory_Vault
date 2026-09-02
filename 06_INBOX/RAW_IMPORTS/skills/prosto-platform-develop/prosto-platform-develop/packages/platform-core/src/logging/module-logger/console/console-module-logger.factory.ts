import type { IPlatformModuleLogger } from '@prosto/platform-sdk';
import type { ISecretsRedactor } from '@/security/index.js';
import type {
  ICreateModuleLoggerOptions,
  IModuleLoggerFactory,
} from '../interfaces/index.js';
import { ConsoleModuleLogger } from './console-module-logger.js';

export class ConsoleModuleLoggerFactory implements IModuleLoggerFactory {
  constructor(private readonly _secretsRedactor?: ISecretsRedactor) {}

  create(options: ICreateModuleLoggerOptions): IPlatformModuleLogger {
    return new ConsoleModuleLogger(options.moduleId, this._secretsRedactor);
  }
}
