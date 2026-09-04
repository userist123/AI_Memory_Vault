import type { output, ZodType, ZodTypeAny } from 'zod';
import type {
  IConfigurationBuilder,
  IConfigurationProvider,
  IEnvOptions,
  IJsonFileOptions,
} from './interfaces/index.js';
import {
  CommandLineConfigurationProvider,
  EnvironmentVariablesConfigurationProvider,
  InMemoryConfigurationProvider,
  JsonFileConfigurationProvider,
} from './providers/index.js';

/**
 * Configuration builder that merges configuration sources and optionally validates the result.
 */
export class ConfigurationBuilder<
  TSchema extends ZodTypeAny = ZodType<Record<string, unknown>>,
> implements IConfigurationBuilder<TSchema> {
  protected readonly _providers: IConfigurationProvider[] = [];

  constructor(protected readonly schema?: TSchema) {}

  addInMemoryCollection(config: Record<string, unknown>): this {
    this._providers.push(new InMemoryConfigurationProvider(config));
    return this;
  }

  addJsonFile(filePath: string, options?: IJsonFileOptions): this {
    this._providers.push(new JsonFileConfigurationProvider(filePath, options));
    return this;
  }

  addEnvironmentVariables(options?: IEnvOptions): this {
    this._providers.push(
      new EnvironmentVariablesConfigurationProvider(options),
    );
    return this;
  }

  addCommandLine(args: string[]): this {
    this._providers.push(new CommandLineConfigurationProvider(args));
    return this;
  }

  build(): output<TSchema>;
  build<T extends object>(): T;
  build(): unknown {
    const sources: Record<string, unknown>[] = [];

    for (const provider of this._providers) {
      sources.push(provider.load());
    }

    let merged = this._merge(sources);

    if (this.schema) {
      merged = this.schema.parse(merged) as Record<string, unknown>;
    }

    return merged;
  }

  protected _merge(
    sources: Record<string, unknown>[],
  ): Record<string, unknown> {
    const result: Record<string, unknown> = {};

    for (const source of sources) {
      this._deepMerge(result, source);
    }

    return result;
  }

  protected _deepMerge(
    target: Record<string, unknown>,
    source: Record<string, unknown>,
  ): void {
    for (const [key, value] of Object.entries(source)) {
      if (
        value &&
        typeof value === 'object' &&
        !Array.isArray(value) &&
        key in target &&
        target[key] &&
        typeof target[key] === 'object' &&
        !Array.isArray(target[key])
      ) {
        this._deepMerge(
          target[key] as Record<string, unknown>,
          value as Record<string, unknown>,
        );
      } else {
        target[key] = value;
      }
    }
  }
}
