import type {
  IServiceRegistry,
  IPersistenceDescriptor,
  ServiceTokenType,
  IPersistenceInitializationInput,
} from '@prosto/platform-sdk';
import { rm } from 'node:fs/promises';
import { join } from 'node:path';
import { afterAll, describe, expect, it } from 'vitest';
import {
  Table,
  type MigrationInterface,
  type QueryRunner,
  type TableColumnOptions,
} from 'typeorm';
import {
  createTypeOrmPersistenceDescriptor,
  type ITypeOrmPersistenceConfig,
  TYPEORM_DATA_SOURCE_SERVICE_TOKEN,
  type TypeOrmDialectType,
  TypeOrmPersistenceProvider,
} from '@/index.js';

type IntegrationConfigurationType = Pick<
  ITypeOrmPersistenceConfig,
  | 'type'
  | 'host'
  | 'port'
  | 'database'
  | 'username'
  | 'password'
  | 'migrationLockTimeoutMs'
  | 'options'
>;

class TestServiceRegistry implements IServiceRegistry {
  private readonly _services = new Map<symbol, unknown>();

  register<TService>(
    token: ServiceTokenType<TService>,
    service: NoInfer<TService>,
  ): void {
    if (this._services.has(token)) {
      throw new Error('Service is already registered.');
    }

    this._services.set(token, service);
  }

  override<TService>(
    token: ServiceTokenType<TService>,
    service: NoInfer<TService>,
  ): void {
    this._services.set(token, service);
  }

  resolve<TService>(token: ServiceTokenType<TService>): TService | undefined {
    return this._services.get(token) as TService | undefined;
  }

  resolveRequired<TService>(token: ServiceTokenType<TService>): TService {
    const service = this.resolve(token);

    if (service === undefined) {
      throw new Error('Service is not registered.');
    }

    return service;
  }

  has<TService>(token: ServiceTokenType<TService>): boolean {
    return this._services.has(token);
  }

  unregister<TService>(token: ServiceTokenType<TService>): void {
    this._services.delete(token);
  }
}

function primaryKeyColumn(queryRunner: QueryRunner): TableColumnOptions {
  return {
    name: 'id',
    type: queryRunner.connection.options.type === 'mssql' ? 'int' : 'integer',
    isPrimary: true,
  };
}

class platform_create_certification1710000000100 implements MigrationInterface {
  async up(queryRunner: QueryRunner): Promise<void> {
    await queryRunner.createTable(
      new Table({
        name: 'platform_certification_probe',
        columns: [primaryKeyColumn(queryRunner)],
      }),
    );
  }

  async down(queryRunner: QueryRunner): Promise<void> {
    await queryRunner.dropTable('platform_certification_probe');
  }
}

class catalog_create_certification1710000000101 implements MigrationInterface {
  async up(queryRunner: QueryRunner): Promise<void> {
    await queryRunner.createTable(
      new Table({
        name: 'catalog_certification_probe',
        columns: [primaryKeyColumn(queryRunner)],
      }),
    );
  }

  async down(queryRunner: QueryRunner): Promise<void> {
    await queryRunner.dropTable('catalog_certification_probe');
  }
}

class orders_create_certification1710000000102 implements MigrationInterface {
  async up(queryRunner: QueryRunner): Promise<void> {
    await queryRunner.createTable(
      new Table({
        name: 'orders_certification_probe',
        columns: [primaryKeyColumn(queryRunner)],
      }),
    );
  }

  async down(queryRunner: QueryRunner): Promise<void> {
    await queryRunner.dropTable('orders_certification_probe');
  }
}

class platform_hold_lock1710000000103 implements MigrationInterface {
  static entered: (() => void) | undefined;
  static release: Promise<void> | undefined;

  async up(queryRunner: QueryRunner): Promise<void> {
    platform_hold_lock1710000000103.entered?.();
    await platform_hold_lock1710000000103.release;
    await queryRunner.createTable(
      new Table({
        name: 'platform_lock_certification_probe',
        columns: [primaryKeyColumn(queryRunner)],
      }),
    );
  }

  async down(queryRunner: QueryRunner): Promise<void> {
    await queryRunner.dropTable('platform_lock_certification_probe');
  }
}

function isDialect(value: string | undefined): value is TypeOrmDialectType {
  return (
    value === 'postgres' ||
    value === 'mysql' ||
    value === 'mariadb' ||
    value === 'sqlite' ||
    value === 'mssql'
  );
}

function requiredEnvironment(name: string): string {
  const value = process.env[name];

  if (value === undefined || value.length === 0) {
    throw new Error(
      `Required TypeORM integration variable ${name} is missing.`,
    );
  }

  return value;
}

function readOptions(dialect: string): Record<string, unknown> | undefined {
  const raw = process.env[`${dialect.toUpperCase()}_OPTIONS`];

  if (raw === undefined || raw.length === 0) {
    return undefined;
  }

  try {
    return JSON.parse(raw) as Record<string, unknown>;
  } catch {
    throw new Error(`${dialect.toUpperCase()}_OPTIONS must be valid JSON.`);
  }
}

function readConfiguration(): IntegrationConfigurationType {
  const dialect = process.env.PROSTO_TYPEORM_DIALECT;

  if (!isDialect(dialect)) {
    throw new Error(
      'PROSTO_TYPEORM_DIALECT must be postgres, mysql, mariadb, sqlite, or mssql.',
    );
  }

  if (dialect === 'sqlite') {
    const directory = requiredEnvironment('PROSTO_TYPEORM_SQLITE_DIRECTORY');

    return {
      type: dialect,
      database: join(directory, 'prosto-typeorm-certification.sqlite'),
      migrationLockTimeoutMs: 2_000,
    };
  }

  const prefix = dialect.toUpperCase();

  return {
    type: dialect,
    host: requiredEnvironment(`${prefix}_HOST`),
    port: Number.parseInt(requiredEnvironment(`${prefix}_PORT`), 10),
    database: requiredEnvironment(`${prefix}_DATABASE`),
    username: requiredEnvironment(`${prefix}_USERNAME`),
    password: requiredEnvironment(`${prefix}_PASSWORD`),
    migrationLockTimeoutMs: 5_000,
    options: readOptions(dialect),
  };
}

function descriptors(
  includeDeferredMigration = false,
): readonly IPersistenceDescriptor[] {
  return [
    {
      owner: 'platform',
      ownerId: 'platform',
      payload: createTypeOrmPersistenceDescriptor({
        entities: [],
        migrations: [
          platform_create_certification1710000000100,
          ...(includeDeferredMigration
            ? [platform_hold_lock1710000000103]
            : []),
        ],
      }),
    },
    {
      owner: 'module',
      ownerId: 'catalog',
      payload: createTypeOrmPersistenceDescriptor({
        entities: [],
        migrations: [catalog_create_certification1710000000101],
      }),
    },
    {
      owner: 'module',
      ownerId: 'orders',
      payload: createTypeOrmPersistenceDescriptor({
        entities: [],
        migrations: [orders_create_certification1710000000102],
      }),
    },
  ];
}

function providerInput(
  configuration: IntegrationConfigurationType,
  services: IServiceRegistry,
  includeDeferredMigration = false,
): IPersistenceInitializationInput {
  return {
    descriptors: descriptors(includeDeferredMigration),
    configuration: { typeorm: { enabled: true, ...configuration } },
    services,
  };
}

const integrationEnabled = process.env.PROSTO_TYPEORM_INTEGRATION === '1';
const configuration = integrationEnabled ? readConfiguration() : undefined;

function getConfiguration(): IntegrationConfigurationType {
  if (configuration === undefined) {
    throw new Error('TypeORM integration configuration is unavailable.');
  }

  return configuration;
}

describe.runIf(integrationEnabled)(
  `TypeORM ${process.env.PROSTO_TYPEORM_DIALECT ?? 'unconfigured'} dialect certification`,
  () => {
    afterAll(async () => {
      if (configuration?.type === 'sqlite' && configuration.database) {
        await rm(configuration.database, { force: true });
      }
    });

    it('migrates prefixed tables once, publishes one ready DataSource, and restarts cleanly', async () => {
      // Arrange
      const firstProvider = new TypeOrmPersistenceProvider();
      const firstServices = new TestServiceRegistry();

      // Act
      await firstProvider.initialize(
        providerInput(getConfiguration(), firstServices),
      );

      const firstDataSource = firstServices.resolveRequired(
        TYPEORM_DATA_SOURCE_SERVICE_TOKEN,
      );

      // Assert
      expect(firstDataSource.isInitialized).toBe(true);
      expect(
        await firstDataSource.query('SELECT * FROM migrations'),
      ).toHaveLength(3);

      const schemaRunner = firstDataSource.createQueryRunner();

      try {
        for (const table of [
          'platform_certification_probe',
          'catalog_certification_probe',
          'orders_certification_probe',
        ]) {
          expect(await schemaRunner.hasTable(table)).toBe(true);
        }
      } finally {
        await schemaRunner.release();
      }

      await firstProvider.dispose();

      expect(firstDataSource.isInitialized).toBe(false);
      expect(firstServices.has(TYPEORM_DATA_SOURCE_SERVICE_TOKEN)).toBe(false);

      const secondProvider = new TypeOrmPersistenceProvider();
      const secondServices = new TestServiceRegistry();

      await secondProvider.initialize(
        providerInput(getConfiguration(), secondServices),
      );

      const secondDataSource = secondServices.resolveRequired(
        TYPEORM_DATA_SOURCE_SERVICE_TOKEN,
      );

      expect(secondDataSource.isInitialized).toBe(true);
      expect(
        await secondDataSource.query('SELECT * FROM migrations'),
      ).toHaveLength(3);

      await secondProvider.dispose();
    });

    it('serializes concurrent startup and reports a redacted lock timeout', async () => {
      // Arrange
      let entered!: () => void;
      const migrationEntered = new Promise<void>((resolve) => {
        entered = resolve;
      });
      let release!: () => void;

      platform_hold_lock1710000000103.entered = entered;
      platform_hold_lock1710000000103.release = new Promise<void>((resolve) => {
        release = resolve;
      });

      const firstProvider = new TypeOrmPersistenceProvider();
      const firstServices = new TestServiceRegistry();
      const firstStartup = firstProvider.initialize(
        providerInput(getConfiguration(), firstServices, true),
      );

      await migrationEntered;

      const timeoutProvider = new TypeOrmPersistenceProvider();
      const timeoutServices = new TestServiceRegistry();
      const waitingProvider = new TypeOrmPersistenceProvider();
      const waitingServices = new TestServiceRegistry();
      let secondStartupCompleted = false;
      const secondStartup = waitingProvider
        .initialize(providerInput(getConfiguration(), waitingServices, true))
        .then(() => {
          secondStartupCompleted = true;
        });

      // Act and assert
      await expect(
        timeoutProvider.initialize(
          providerInput(
            { ...getConfiguration(), migrationLockTimeoutMs: 100 },
            timeoutServices,
            true,
          ),
        ),
      ).rejects.toMatchObject({
        code: 'PersistenceMigrationLockTimeout',
        details: expect.objectContaining({
          remediationHint: expect.any(String),
        }),
      });
      expect(timeoutServices.has(TYPEORM_DATA_SOURCE_SERVICE_TOKEN)).toBe(
        false,
      );
      expect(secondStartupCompleted).toBe(false);

      release();

      await firstStartup;
      await secondStartup;
      await firstProvider.dispose();
      await waitingProvider.dispose();

      const retryProvider = new TypeOrmPersistenceProvider();
      const retryServices = new TestServiceRegistry();
      await retryProvider.initialize(
        providerInput(getConfiguration(), retryServices, true),
      );

      expect(retryServices.has(TYPEORM_DATA_SOURCE_SERVICE_TOKEN)).toBe(true);

      await retryProvider.dispose();

      platform_hold_lock1710000000103.entered = undefined;
      platform_hold_lock1710000000103.release = undefined;
    });
  },
);
