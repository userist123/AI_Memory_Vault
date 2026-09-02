import type {
  IPersistenceDescriptor,
  IServiceRegistry,
  ServiceTokenType,
} from '@prosto/platform-sdk';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import {
  Entity,
  type EntityTarget,
  ManyToOne,
  type MigrationInterface,
  PrimaryGeneratedColumn,
  type QueryRunner,
} from 'typeorm';
import {
  collectValidatedTypeOrmMetadata,
  createTypeOrmPersistenceDescriptor,
  TYPEORM_DATA_SOURCE_SERVICE_TOKEN,
  TypeOrmPersistenceProvider,
} from '@/index.js';

import 'reflect-metadata';

@Entity('catalog_product')
class CatalogProduct {
  @PrimaryGeneratedColumn()
  id!: number;
}

@Entity('orders_order')
class OrdersOrder {
  @PrimaryGeneratedColumn()
  id!: number;
}

@Entity('orders_order_item')
class OrdersOrderItem {
  @PrimaryGeneratedColumn()
  id!: number;

  @ManyToOne(() => OrdersOrder)
  order!: OrdersOrder;
}

@Entity('orders_external_reference')
class OrdersExternalReference {
  @PrimaryGeneratedColumn()
  id!: number;

  @ManyToOne(() => CatalogProduct)
  product!: CatalogProduct;
}

@Entity('wrong_prefix')
class InvalidOrdersEntity {
  @PrimaryGeneratedColumn()
  id!: number;
}

class UnregisteredEntity {
  id!: number;
}

@Entity('orders_unregistered_reference')
class OrdersUnregisteredReference {
  @PrimaryGeneratedColumn()
  id!: number;

  @ManyToOne(() => UnregisteredEntity)
  unknown!: UnregisteredEntity;
}

@Entity('catalog_second_product')
class CatalogSecondProduct {
  @PrimaryGeneratedColumn()
  id!: number;
}

class orders_create_order1710000000000 implements MigrationInterface {
  async up(): Promise<void> {
    return;
  }
  async down(): Promise<void> {
    return;
  }
}

class catalog_create_product1710000000000 implements MigrationInterface {
  async up(): Promise<void> {
    return;
  }
  async down(): Promise<void> {
    return;
  }
}

class catalog_create_second_product1710000000001 implements MigrationInterface {
  async up(): Promise<void> {
    return;
  }
  async down(): Promise<void> {
    return;
  }
}

class platform_create_migration_probe1710000000002 implements MigrationInterface {
  async up(queryRunner: QueryRunner): Promise<void> {
    await queryRunner.query(
      'CREATE TABLE platform_migration_probe (id integer PRIMARY KEY)',
    );
  }

  async down(queryRunner: QueryRunner): Promise<void> {
    await queryRunner.query('DROP TABLE platform_migration_probe');
  }
}

class platform_failing_migration1710000000003 implements MigrationInterface {
  async up(): Promise<void> {
    throw new Error('database password=not-for-logs');
  }

  async down(): Promise<void> {
    return;
  }
}

function descriptor(
  ownerId: string,
  entities: readonly EntityTarget<unknown>[],
  migrations: readonly (new () => MigrationInterface)[] = [],
): IPersistenceDescriptor {
  return {
    owner: 'module',
    ownerId,
    payload: createTypeOrmPersistenceDescriptor({
      entities,
      migrations,
    }),
  };
}

class TestServiceRegistry implements IServiceRegistry {
  readonly services = new Map<symbol, unknown>();

  register<TService>(
    token: ServiceTokenType<TService>,
    service: NoInfer<TService>,
  ): void {
    if (this.services.has(token)) {
      throw new Error('Service is already registered.');
    }

    this.services.set(token, service);
  }

  override<TService>(
    token: ServiceTokenType<TService>,
    service: NoInfer<TService>,
  ): void {
    this.services.set(token, service);
  }

  resolve<TService>(token: ServiceTokenType<TService>): TService | undefined {
    return this.services.get(token) as TService | undefined;
  }

  resolveRequired<TService>(token: ServiceTokenType<TService>): TService {
    const service = this.resolve(token);

    if (service === undefined) {
      throw new Error('Service is not registered.');
    }

    return service;
  }

  has<TService>(token: ServiceTokenType<TService>): boolean {
    return this.services.has(token);
  }

  unregister<TService>(token: ServiceTokenType<TService>): void {
    this.services.delete(token);
  }
}

describe('TypeOrmPersistenceProvider', () => {
  it('initializes one SQLite DataSource and publishes it only when ready', async () => {
    // Arrange
    const provider = new TypeOrmPersistenceProvider();
    const services = new TestServiceRegistry();

    // Act
    await provider.initialize({
      descriptors: [],
      configuration: {
        typeorm: { enabled: true, type: 'sqlite', database: ':memory:' },
      },
      services,
    });

    // Assert
    const dataSource = services.resolveRequired(
      TYPEORM_DATA_SOURCE_SERVICE_TOKEN,
    );

    expect(provider.state).toBe('ready');
    expect(dataSource.isInitialized).toBe(true);
    expect(services.resolve(TYPEORM_DATA_SOURCE_SERVICE_TOKEN)).toBe(
      dataSource,
    );

    await provider.dispose();
  });

  it('destroys the registered DataSource once and permits repeated disposal', async () => {
    // Arrange
    const provider = new TypeOrmPersistenceProvider();
    const services = new TestServiceRegistry();

    await provider.initialize({
      descriptors: [],
      configuration: {
        typeorm: { enabled: true, type: 'sqlite', database: ':memory:' },
      },
      services,
    });

    const dataSource = services.resolveRequired(
      TYPEORM_DATA_SOURCE_SERVICE_TOKEN,
    );

    // Act
    await provider.dispose();
    await provider.dispose();

    // Assert
    expect(dataSource.isInitialized).toBe(false);
    expect(services.has(TYPEORM_DATA_SOURCE_SERVICE_TOKEN)).toBe(false);
    expect(provider.state).toBe('disposed');
  });

  it('runs the complete migration set before publishing the DataSource', async () => {
    // Arrange
    const provider = new TypeOrmPersistenceProvider();
    const services = new TestServiceRegistry();
    const platformDescriptor: IPersistenceDescriptor = {
      owner: 'platform',
      ownerId: 'platform',
      payload: createTypeOrmPersistenceDescriptor({
        entities: [],
        migrations: [platform_create_migration_probe1710000000002],
      }),
    };

    // Act
    await provider.initialize({
      descriptors: [platformDescriptor],
      configuration: {
        typeorm: { enabled: true, type: 'sqlite', database: ':memory:' },
      },
      services,
    });

    // Assert
    const dataSource = services.resolveRequired(
      TYPEORM_DATA_SOURCE_SERVICE_TOKEN,
    );

    expect(
      await dataSource.query(
        "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'platform_migration_probe'",
      ),
    ).toHaveLength(1);

    expect(await dataSource.query('SELECT * FROM migrations')).toHaveLength(1);

    await provider.dispose();
  });

  it('applies SQLite file migrations once across repeated startups', async () => {
    // Arrange
    const directory = await mkdtemp(join(tmpdir(), 'prosto-typeorm-'));
    const database = join(directory, 'platform.sqlite');
    const platformDescriptor: IPersistenceDescriptor = {
      owner: 'platform',
      ownerId: 'platform',
      payload: createTypeOrmPersistenceDescriptor({
        entities: [],
        migrations: [platform_create_migration_probe1710000000002],
      }),
    };

    try {
      // Act
      const firstProvider = new TypeOrmPersistenceProvider();
      const firstServices = new TestServiceRegistry();

      await firstProvider.initialize({
        descriptors: [platformDescriptor],
        configuration: { typeorm: { enabled: true, type: 'sqlite', database } },
        services: firstServices,
      });

      await firstProvider.dispose();

      const secondProvider = new TypeOrmPersistenceProvider();
      const secondServices = new TestServiceRegistry();

      await secondProvider.initialize({
        descriptors: [platformDescriptor],
        configuration: { typeorm: { enabled: true, type: 'sqlite', database } },
        services: secondServices,
      });

      // Assert
      const dataSource = secondServices.resolveRequired(
        TYPEORM_DATA_SOURCE_SERVICE_TOKEN,
      );

      expect(await dataSource.query('SELECT * FROM migrations')).toHaveLength(
        1,
      );

      await secondProvider.dispose();
    } finally {
      await rm(directory, { force: true, recursive: true });
    }
  });

  it('does not publish a DataSource when a migration fails', async () => {
    // Arrange
    const provider = new TypeOrmPersistenceProvider();
    const services = new TestServiceRegistry();
    const platformDescriptor: IPersistenceDescriptor = {
      owner: 'platform',
      ownerId: 'platform',
      payload: createTypeOrmPersistenceDescriptor({
        entities: [],
        migrations: [platform_failing_migration1710000000003],
      }),
    };

    // Act and assert
    await expect(
      provider.initialize({
        descriptors: [platformDescriptor],
        configuration: {
          typeorm: { enabled: true, type: 'sqlite', database: ':memory:' },
        },
        services,
      }),
    ).rejects.toMatchObject({
      code: 'PersistenceMigrationFailed',
      message: 'TypeORM migration execution failed.',
    });
    expect(services.has(TYPEORM_DATA_SOURCE_SERVICE_TOKEN)).toBe(false);
    expect(provider.state).toBe('failed');
  });

  it('validates explicit owner-prefixed declarations before opening a DataSource', async () => {
    // Arrange
    const provider = new TypeOrmPersistenceProvider();
    const services = new TestServiceRegistry();

    // Act
    await provider.initialize({
      descriptors: [
        descriptor(
          'catalog',
          [CatalogProduct],
          [catalog_create_product1710000000000],
        ),
        descriptor(
          'orders',
          [OrdersOrder, OrdersOrderItem],
          [orders_create_order1710000000000],
        ),
      ],
      configuration: {
        typeorm: { enabled: true, type: 'sqlite', database: ':memory:' },
      },
      services,
    });

    // Assert
    expect(provider.state).toBe('ready');

    await provider.dispose();
  });

  it('rejects invalid owner table prefixes before opening a DataSource', async () => {
    // Arrange
    const provider = new TypeOrmPersistenceProvider();
    const services = new TestServiceRegistry();

    // Act and assert
    await expect(
      provider.initialize({
        descriptors: [descriptor('orders', [InvalidOrdersEntity])],
        configuration: {
          typeorm: { enabled: true, type: 'sqlite', database: ':memory:' },
        },
        services,
      }),
    ).rejects.toMatchObject({
      code: 'PersistenceDescriptorValidationFailed',
      details: expect.objectContaining({ ownerId: 'orders' }),
    });

    expect(services.has(TYPEORM_DATA_SOURCE_SERVICE_TOKEN)).toBe(false);
  });

  it('rejects duplicate table and migration identities across owners', async () => {
    // Arrange
    const provider = new TypeOrmPersistenceProvider();
    const services = new TestServiceRegistry();

    // Act and assert
    await expect(
      provider.initialize({
        descriptors: [
          descriptor(
            'catalog',
            [CatalogProduct],
            [catalog_create_product1710000000000],
          ),
          descriptor(
            'catalog',
            [CatalogProduct],
            [catalog_create_product1710000000000],
          ),
        ],
        configuration: {
          typeorm: { enabled: true, type: 'sqlite', database: ':memory:' },
        },
        services,
      }),
    ).rejects.toMatchObject({
      code: 'PersistenceDescriptorValidationFailed',
    });

    expect(services.has(TYPEORM_DATA_SOURCE_SERVICE_TOKEN)).toBe(false);
  });

  it('rejects cross-owner entity relations before opening a DataSource', async () => {
    // Arrange
    const provider = new TypeOrmPersistenceProvider();
    const services = new TestServiceRegistry();

    // Act and assert
    await expect(
      provider.initialize({
        descriptors: [
          descriptor('catalog', [CatalogProduct]),
          descriptor('orders', [OrdersExternalReference]),
        ],
        configuration: {
          typeorm: { enabled: true, type: 'sqlite', database: ':memory:' },
        },
        services,
      }),
    ).rejects.toMatchObject({
      code: 'PersistenceDescriptorValidationFailed',
      details: expect.objectContaining({ ownerId: 'orders' }),
    });

    expect(services.has(TYPEORM_DATA_SOURCE_SERVICE_TOKEN)).toBe(false);
  });

  it('rejects relations to entities not declared by any descriptor', async () => {
    // Arrange
    const provider = new TypeOrmPersistenceProvider();
    const services = new TestServiceRegistry();

    // Act and assert
    await expect(
      provider.initialize({
        descriptors: [descriptor('orders', [OrdersUnregisteredReference])],
        configuration: {
          typeorm: { enabled: true, type: 'sqlite', database: ':memory:' },
        },
        services,
      }),
    ).rejects.toMatchObject({
      code: 'PersistenceDescriptorValidationFailed',
      details: expect.objectContaining({ ownerId: 'orders' }),
    });

    expect(services.has(TYPEORM_DATA_SOURCE_SERVICE_TOKEN)).toBe(false);
  });

  it('preserves descriptor and declaration order in collected metadata', () => {
    // Arrange
    const descriptors = [
      descriptor(
        'catalog',
        [CatalogProduct, CatalogSecondProduct],
        [
          catalog_create_product1710000000000,
          catalog_create_second_product1710000000001,
        ],
      ),
      descriptor('orders', [OrdersOrder], [orders_create_order1710000000000]),
    ];

    // Act
    const metadata = collectValidatedTypeOrmMetadata(descriptors, 'sqlite');

    // Assert
    expect(metadata.entities).toEqual([
      CatalogProduct,
      CatalogSecondProduct,
      OrdersOrder,
    ]);

    expect(metadata.migrations).toEqual([
      catalog_create_product1710000000000,
      catalog_create_second_product1710000000001,
      orders_create_order1710000000000,
    ]);

    expect(metadata.ownership).toEqual([
      {
        ownerId: 'catalog',
        entityNames: ['CatalogProduct', 'CatalogSecondProduct'],
        tableNames: ['catalog_product', 'catalog_second_product'],
        migrationNames: [
          'catalog_create_product1710000000000',
          'catalog_create_second_product1710000000001',
        ],
      },
      {
        ownerId: 'orders',
        entityNames: ['OrdersOrder'],
        tableNames: ['orders_order'],
        migrationNames: ['orders_create_order1710000000000'],
      },
    ]);
  });
});
