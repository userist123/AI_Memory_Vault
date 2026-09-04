import type {
  IEventBus,
  IPersistenceModuleContext,
  IPlatformModuleContext,
  IPlatformModuleManifest,
  IServiceRegistry,
} from '@prosto/platform-sdk';
import { resolveNestedValue } from '@prosto/platform-sdk';
import type { IModuleLoggerFactory } from '@/logging/index.js';
import type { IPlatformConfig } from '@/runtime/index.js';
import {
  ConfigAccessPolicyEvaluator,
  type IConfigAccessEvaluationInput,
  type IConfigAccessPolicy,
  type IConfigAccessPolicyEvaluator,
} from '@/modularity/index.js';
import type {
  ICreateModuleContextOptions,
  IModuleContextFactory,
} from '../interfaces/index.js';
import { buildScopedConfigProjection } from '../utils/index.js';

export class ModuleContextFactory implements IModuleContextFactory {
  constructor(
    private readonly _environment: string,
    private readonly _config: IPlatformConfig,
    private readonly _eventBus: IEventBus,
    private readonly _services: IServiceRegistry,
    private readonly _moduleLoggerFactory: IModuleLoggerFactory,
    private readonly _configAccessPolicyEvaluator: IConfigAccessPolicyEvaluator = new ConfigAccessPolicyEvaluator(),
  ) {}

  create(options: ICreateModuleContextOptions): IPlatformModuleContext {
    const moduleId = options.moduleManifest.id;
    const logger = this._moduleLoggerFactory.create({ moduleId });
    const scopedConfig = this._getScopedConfig(options.moduleManifest);

    const persistence: IPersistenceModuleContext | undefined =
      options.persistenceEnabled
        ? {
            state: options.persistenceProvider?.state ?? 'unavailable',
            // Descriptors become immutable after init; later phases can only
            // observe provider state and resolve services published by it.
            descriptors:
              options.lifecycleStage === 'init'
                ? options.persistenceProvider?.descriptors
                : undefined,
          }
        : undefined;

    return {
      logger,
      moduleId,
      persistence,
      startupPolicy: options.startupPolicy,
      sdkVersion: options.sdkVersion,
      environment: this._environment,
      eventBus: this._eventBus,
      services: this._services,
      config: scopedConfig,
      getConfigValue<T>(key: string, defaultValue?: T): Readonly<T> {
        return resolveNestedValue(scopedConfig, key) ?? (defaultValue as T);
      },
    };
  }

  private _getScopedConfig(
    moduleManifest: IPlatformModuleManifest,
  ): Record<string, unknown> {
    const moduleId = moduleManifest.id;

    const configAccessEvalInput: IConfigAccessEvaluationInput = {
      moduleId,
      isProduction: this._environment === 'production',
    };

    const configAccessPolicy =
      this._config.modules?.configAccessPolicy ??
      this._createDefaultConfigAccessPolicy();

    const configAccessResult = this._configAccessPolicyEvaluator.evaluate(
      configAccessEvalInput,
      configAccessPolicy,
      this._config,
    );

    return buildScopedConfigProjection(
      this._config,
      moduleId,
      configAccessResult.allowedSections,
    );
  }

  private _createDefaultConfigAccessPolicy(): IConfigAccessPolicy {
    return {
      productionStrictMode: true,
    };
  }
}
