import type { output, ZodType, ZodTypeAny } from 'zod';
import type { IEnvOptions } from './env-options.interface.js';
import type { IJsonFileOptions } from './json-file-options.interface.js';

/**
 * @alpha
 * Configuration builder interface.
 */
export interface IConfigurationBuilder<
  TSchema extends ZodTypeAny = ZodType<Record<string, unknown>>,
> {
  addInMemoryCollection(
    config: Record<string, unknown>,
  ): IConfigurationBuilder<TSchema>;
  addJsonFile(
    filePath: string,
    options?: IJsonFileOptions,
  ): IConfigurationBuilder<TSchema>;
  addEnvironmentVariables(
    options?: IEnvOptions,
  ): IConfigurationBuilder<TSchema>;
  addCommandLine(args: string[]): IConfigurationBuilder<TSchema>;
  build(): output<TSchema>;
  build<T extends object>(): T;
}
