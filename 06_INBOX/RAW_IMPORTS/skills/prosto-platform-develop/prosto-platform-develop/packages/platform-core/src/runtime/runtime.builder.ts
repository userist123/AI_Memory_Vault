import pkg from '../../package.json' with { type: 'json' };
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import type { IEventBus, IServiceRegistry } from '@prosto/platform-sdk';
import type {
  IPlatformConfig,
  IPlatformRuntime,
  IRuntimeBuilder,
  IRuntimeBuilderOptions,
} from './interfaces/index.js';
import {
  BootstrapCoordinator,
  BootstrapPipeline,
  DiscoverStage,
  type IBootstrapCoordinator,
  ModulesInitializationStage,
  ModulesStartStage,
  PersistenceInitializationStage,
  ResolveDependenciesStage,
  ValidateStage,
} from '@/bootstrap/index.js';
import { FileSystemArtifactCache, NoOpArtifactCache } from '@/caching/index.js';
import { ConfigurationBuilder, loadJsonFileSync } from '@/common/index.js';
import {
  DiagnosticReportBuilder,
  DiagnosticsReporter,
} from '@/diagnostics/index.js';
import { InMemoryEventBus } from '@/events/index.js';
import { ConsoleModuleLoggerFactory } from '@/logging/index.js';
import {
  ArtifactFetcher,
  ArtifactSourceFactory,
  type IModuleContextFactory,
  type IModuleLifecycleOrchestrator,
  type IModuleLoader,
  ManifestValidationStrategy,
  ModuleContextFactory,
  ModuleLifecycleOrchestrator,
  ModuleLoader,
  StartupPolicyEvaluator,
} from '@/modularity/index.js';
import { type ISecretsRedactor, SecretsRedactor } from '@/security/index.js';
import { InMemoryServiceRegistry } from '@/services/index.js';
import { PlatformRuntime } from './platform-runtime.js';
import {
  platformConfigSchema,
  platformLocalPersistenceConfigSchema,
} from './schemas/index.js';

/**
 * @alpha
 * Builder for creating platform runtime instances.
 * Acts as the composition root for wiring all dependencies.
 */
export class RuntimeBuilder implements IRuntimeBuilder {
  build(options: IRuntimeBuilderOptions): IPlatformRuntime {
    const environment =
      options.environment || process.env.NODE_ENV || 'production';

    // Build platform configuration by merging defaults,
    // config files, environment variables, and command-line arguments.
    const config = this._buildPlatformConfig(options);

    // Create a secrets redactor based on configuration to ensure
    // sensitive information is not exposed in logs or diagnostics.
    const secretsRedactor = this._createSecretsRedactor(config);

    const eventBus = new InMemoryEventBus();
    const serviceRegistry = new InMemoryServiceRegistry();

    const diagnosticsReporter = new DiagnosticsReporter(
      new DiagnosticReportBuilder(secretsRedactor),
    );

    const moduleContextFactory = this._createModuleContextFactory(
      environment,
      config,
      eventBus,
      serviceRegistry,
      secretsRedactor,
    );

    const moduleLifecycleOrchestrator = new ModuleLifecycleOrchestrator(
      moduleContextFactory,
    );

    const bootstrapCoordinator = this._createBootstrapCoordinator(
      config,
      moduleLifecycleOrchestrator,
    );

    return new PlatformRuntime(
      options.modules ?? [],
      config,
      diagnosticsReporter,
      bootstrapCoordinator,
      moduleLifecycleOrchestrator,
      serviceRegistry,
      {
        correlationId: options.correlationId,
        persistenceProvider: options.persistenceProvider,
        platformPersistenceDescriptor: options.platformPersistenceDescriptor,
        onStopped: () => {
          serviceRegistry.dispose();
          eventBus.dispose();
        },
      },
    );
  }

  protected _buildPlatformConfig(
    options: IRuntimeBuilderOptions,
  ): IPlatformConfig {
    const {
      configDir,
      environment = process.env.NODE_ENV || 'production',
      commandLineArgs = process.argv.slice(2),
    } = options;

    const defaultConfig: Partial<IPlatformConfig> = {
      platform: {
        name: 'Prosto Platform',
        version: pkg.version,
        basePath: process.cwd(),
        startupPolicy: 'strict',
      },
      modules: {
        configAccessPolicy: {
          productionStrictMode: true,
        },
        artifactCache: {
          enabled: false,
        },
      },
      security: {
        secretRedaction: {
          enabled: true,
          patterns: [
            'key',
            'token',
            'secret',
            'password',
            'passphrase',
            'url',
            'connectionString',
          ],
        },
      },
    };

    // Package defaults are always loaded first. Deployment overrides are only
    // read from an explicit configDir, never from the current working directory.
    // Resolve the installed package entry point instead of a statically-known
    // JSON URL. Vite embeds the latter as a data URL in the published bundle.
    const packageConfigDir = dirname(
      dirname(fileURLToPath(import.meta.resolve('@prosto/platform-core'))),
    );
    const deploymentConfigDir = configDir ? resolve(configDir) : undefined;
    const packagePaths = new Set([
      resolve(packageConfigDir, 'app_settings.json'),
      resolve(packageConfigDir, `app_settings.${environment}.json`),
    ]);

    const configBuilder = new ConfigurationBuilder(platformConfigSchema)
      .addInMemoryCollection(defaultConfig)
      .addJsonFile(resolve(packageConfigDir, 'app_settings.json'))
      .addJsonFile(
        resolve(packageConfigDir, `app_settings.${environment}.json`),
        { optional: true },
      );

    if (deploymentConfigDir) {
      const deploymentPaths = [
        'app_settings.json',
        `app_settings.${environment}.json`,
      ].map((fileName) => resolve(deploymentConfigDir, fileName));

      for (const filePath of deploymentPaths) {
        if (!packagePaths.has(filePath)) {
          configBuilder.addJsonFile(filePath, { optional: true });
        }
      }

      const localConfigPath = resolve(
        deploymentConfigDir,
        'app_settings.local.json',
      );

      if (!packagePaths.has(localConfigPath)) {
        configBuilder.addInMemoryCollection(
          platformLocalPersistenceConfigSchema.parse(
            loadJsonFileSync(localConfigPath, true),
          ),
        );
      }
    }

    configBuilder
      .addEnvironmentVariables({ prefix: 'PROSTO_' })
      .addCommandLine(commandLineArgs);

    return configBuilder.build();
  }

  protected _createSecretsRedactor(config: IPlatformConfig): ISecretsRedactor {
    return new SecretsRedactor(config.security.secretRedaction);
  }

  protected _createModuleContextFactory(
    environment: string,
    config: IPlatformConfig,
    eventBus: IEventBus,
    serviceRegistry: IServiceRegistry,
    secretsRedactor?: ISecretsRedactor,
  ): IModuleContextFactory {
    return new ModuleContextFactory(
      environment,
      config,
      eventBus,
      serviceRegistry,
      new ConsoleModuleLoggerFactory(secretsRedactor),
    );
  }

  protected _createBootstrapCoordinator(
    config: IPlatformConfig,
    moduleLifecycleOrchestrator: IModuleLifecycleOrchestrator,
  ): IBootstrapCoordinator {
    const moduleLoader = this._createModuleLoader(config);
    const startupPolicyEvaluator = new StartupPolicyEvaluator();

    return new BootstrapCoordinator(
      BootstrapPipeline.create([
        new DiscoverStage(moduleLoader),
        new ValidateStage([new ManifestValidationStrategy()]),
        new ResolveDependenciesStage(startupPolicyEvaluator),
        new ModulesInitializationStage(
          startupPolicyEvaluator,
          moduleLifecycleOrchestrator,
        ),
        new PersistenceInitializationStage(),
        new ModulesStartStage(
          startupPolicyEvaluator,
          moduleLifecycleOrchestrator,
        ),
      ]),
    );
  }

  protected _createModuleLoader(config: IPlatformConfig): IModuleLoader {
    const { artifactCache: artifactCacheConfig } = config.modules;

    const artifactCache = artifactCacheConfig.enabled
      ? new FileSystemArtifactCache({
          maxAgeMs: artifactCacheConfig.maxAgeMs,
          maxSizeBytes: artifactCacheConfig.maxSizeBytes,
          path: resolve(
            config.platform.basePath,
            artifactCacheConfig.path || '.cache/module-artifacts',
          ),
        })
      : new NoOpArtifactCache();

    return new ModuleLoader(
      new ArtifactSourceFactory(new ArtifactFetcher(), artifactCache),
    );
  }
}
