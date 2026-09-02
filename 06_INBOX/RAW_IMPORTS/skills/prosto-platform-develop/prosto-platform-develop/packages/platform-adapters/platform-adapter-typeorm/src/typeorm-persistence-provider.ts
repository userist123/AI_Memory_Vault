import type {
  IMigrationLockFactoryInterface,
  ITypeOrmPersistenceConfig,
  TypeOrmDialectType,
} from '@/interfaces/index.js';
import {
  type IPersistenceInitializationInput,
  type IPersistenceProvider,
  type IServiceRegistry,
  PersistenceDescriptorRegistry,
  PersistenceError,
  type PersistenceProviderStateType,
} from '@prosto/platform-sdk';
import {
  DataSource,
  type DataSourceOptions,
  type EntityTarget,
  type MigrationInterface,
} from 'typeorm';
import { MigrationLockFactory } from '@/factories/index.js';
import { TypeOrmDescriptorValidationError } from '@/errors/index.js';
import { TYPEORM_DATA_SOURCE_SERVICE_TOKEN } from '@/tokens/index.js';
import { collectValidatedTypeOrmMetadata } from '@/utils/index.js';

/**
 * @alpha
 * Initializes and owns the shared TypeORM DataSource for a runtime.
 */
export class TypeOrmPersistenceProvider implements IPersistenceProvider {
  readonly descriptors = new PersistenceDescriptorRegistry();

  private _dataSource: DataSource | undefined;
  private _services: IServiceRegistry | undefined;

  constructor(
    private _migrationLockFactory: IMigrationLockFactoryInterface = new MigrationLockFactory(),
  ) {}

  private _state: PersistenceProviderStateType = 'collecting';

  get state(): PersistenceProviderStateType {
    return this._state;
  }

  async initialize(input: IPersistenceInitializationInput): Promise<void> {
    if (this._state !== 'collecting') {
      throw new PersistenceError(
        'PersistenceInitializationFailed',
        `TypeORM persistence provider cannot initialize from ${this._state} state.`,
        { phase: this._state },
      );
    }

    this._state = 'initializing';

    let dataSource: DataSource | undefined;

    try {
      const configuration = this._getTypeOrmConfiguration(input.configuration);
      const { entities, migrations } = collectValidatedTypeOrmMetadata(
        input.descriptors,
        configuration.type,
      );

      dataSource = new DataSource(
        this._createDataSourceOptions(configuration, entities, migrations),
      );

      await dataSource.initialize();

      await this._runMigrations(dataSource, configuration);

      // Publish only after initialization, migrations, and lock release succeed.
      input.services.register(TYPEORM_DATA_SOURCE_SERVICE_TOKEN, dataSource);

      this._dataSource = dataSource;
      this._services = input.services;
      this._state = 'ready';
    } catch (error) {
      input.services.unregister(TYPEORM_DATA_SOURCE_SERVICE_TOKEN);

      await this._destroyDataSource(dataSource);

      this._state = 'failed';

      throw this._mapInitializationError(
        error,
        this._getConfiguredDialect(input.configuration),
      );
    }
  }

  async dispose(): Promise<void> {
    if (this._state === 'disposed') {
      return;
    }

    const dataSource = this._dataSource;

    this._dataSource = undefined;
    this._services?.unregister(TYPEORM_DATA_SOURCE_SERVICE_TOKEN);
    this._services = undefined;

    await this._destroyDataSource(dataSource);

    this._state = 'disposed';
  }

  private _getTypeOrmConfiguration(
    configuration: IPersistenceInitializationInput['configuration'],
  ): ITypeOrmPersistenceConfig {
    return configuration.typeorm;
  }

  private _getConfiguredDialect(
    configuration: IPersistenceInitializationInput['configuration'],
  ): TypeOrmDialectType | undefined {
    return this._getTypeOrmConfiguration(configuration).type;
  }

  private _createDataSourceOptions(
    configuration: ITypeOrmPersistenceConfig,
    entities: EntityTarget<unknown>[],
    migrations: (new () => MigrationInterface)[],
  ): DataSourceOptions {
    if (configuration.enabled === false || configuration.type === undefined) {
      throw new PersistenceError(
        'PersistenceInitializationFailed',
        'TypeORM persistence is not enabled or does not specify a dialect.',
        { phase: 'configuration' },
      );
    }

    const commonOptions: Pick<
      DataSourceOptions,
      'entities' | 'migrations' | 'synchronize' | 'migrationsRun'
    > = {
      // EntityTarget additionally admits adapter payload metadata. TypeORM accepts
      // the validated constructor/schema subset in its DataSource options.
      entities: entities as DataSourceOptions['entities'],
      migrations: migrations as DataSourceOptions['migrations'],
      synchronize: false,
      migrationsRun: false,
    };

    if (configuration.url !== undefined) {
      return {
        type: configuration.type,
        url: configuration.url,
        ...commonOptions,
      } as DataSourceOptions;
    }

    if (configuration.database === undefined) {
      throw new PersistenceError(
        'PersistenceInitializationFailed',
        'TypeORM persistence configuration requires a database or URL.',
        { dialect: configuration.type, phase: 'configuration' },
      );
    }

    switch (configuration.type) {
      case 'sqlite':
        return {
          type: 'sqlite',
          database: configuration.database,
          ...commonOptions,
        };

      case 'postgres':
        return {
          type: 'postgres',
          host: configuration.host,
          port: configuration.port,
          database: configuration.database,
          username: configuration.username,
          password: configuration.password,
          schema: configuration.schema,
          connectTimeoutMS: configuration.connectTimeoutMs,
          extra:
            configuration.poolSize === undefined
              ? undefined
              : { max: configuration.poolSize },
          ...commonOptions,
        };

      case 'mysql':
      case 'mariadb':
        return {
          type: configuration.type,
          host: configuration.host,
          port: configuration.port,
          database: configuration.database,
          username: configuration.username,
          password: configuration.password,
          connectTimeout: configuration.connectTimeoutMs,
          extra:
            configuration.poolSize === undefined
              ? undefined
              : { connectionLimit: configuration.poolSize },
          ...commonOptions,
        };

      case 'mssql':
        return {
          type: 'mssql',
          host: configuration.host,
          port: configuration.port,
          database: configuration.database,
          username: configuration.username,
          password: configuration.password,
          connectionTimeout: configuration.connectTimeoutMs,
          options: configuration.options,
          pool:
            configuration.poolSize === undefined
              ? undefined
              : { max: configuration.poolSize },
          ...commonOptions,
        };
    }
  }

  private async _destroyDataSource(dataSource?: DataSource): Promise<void> {
    if (dataSource?.isInitialized === true) {
      await dataSource.destroy();
    }
  }

  private async _runMigrations(
    dataSource: DataSource,
    configuration: ITypeOrmPersistenceConfig,
  ): Promise<void> {
    const lock = this._migrationLockFactory.create(dataSource, configuration);
    let migrationError: unknown;

    try {
      await lock.acquire(configuration.migrationLockTimeoutMs ?? 60000);
      await dataSource.runMigrations({
        // SQLite's exclusive lock is itself the migration transaction.
        transaction:
          configuration.type === 'sqlite' &&
          configuration.database !== ':memory:'
            ? 'none'
            : (configuration.migrationTransactionMode ?? 'each'),
      });
    } catch (error) {
      migrationError = error;
    }

    try {
      await lock.release();
    } catch {
      throw new PersistenceError(
        'PersistenceMigrationFailed',
        'TypeORM migration lock release failed.',
        {
          phase: 'migration-lock-release',
          remediationHint:
            'Verify database connectivity and confirm no stale migration lock remains.',
        },
      );
    }

    if (migrationError) {
      if (migrationError instanceof PersistenceError) {
        throw migrationError;
      }

      throw new PersistenceError(
        'PersistenceMigrationFailed',
        'TypeORM migration execution failed.',
        {
          phase: 'migrations',
          remediationHint:
            'Inspect the migration identifiers and database migration journal.',
        },
      );
    }
  }

  private _mapInitializationError(
    error: unknown,
    dialect: TypeOrmDialectType | undefined,
  ): PersistenceError {
    if (error instanceof PersistenceError) {
      return error;
    }

    if (error instanceof TypeOrmDescriptorValidationError) {
      return new PersistenceError(
        'PersistenceDescriptorValidationFailed',
        error.message,
        {
          ownerId: error.descriptor.ownerId,
          moduleId:
            error.descriptor.owner === 'module'
              ? error.descriptor.ownerId
              : undefined,
          phase: 'validation',
          remediationHint: error.remediationHint,
        },
      );
    }

    const message = error instanceof Error ? error.message : String(Error);
    const driver = this._getUnavailableDriver(message, dialect);

    if (driver !== undefined) {
      return new PersistenceError(
        'PersistenceDriverUnavailable',
        `TypeORM driver package ${driver} is unavailable. Install it in the application using this adapter.`,
        {
          dialect,
          remediationHint: `Install the ${driver} peer dependency.`,
        },
      );
    }

    return new PersistenceError(
      'PersistenceInitializationFailed',
      'TypeORM DataSource initialization failed.',
      { phase: 'initializing' },
    );
  }

  private _getUnavailableDriver(
    message: string,
    dialect: TypeOrmDialectType | undefined,
  ): string | undefined {
    const explicitDriver =
      /Please install (pg|mysql2|sqlite3|mssql) package manually/i.exec(
        message,
      )?.[1];

    if (explicitDriver !== undefined) {
      return explicitDriver;
    }

    if (!/package has not been found installed/i.test(message)) {
      return undefined;
    }

    switch (dialect) {
      case 'postgres':
        return 'pg';

      case 'mysql':
      case 'mariadb':
        return 'mysql2';

      case 'sqlite':
        return 'sqlite3';

      case 'mssql':
        return 'mssql';
    }
  }
}
