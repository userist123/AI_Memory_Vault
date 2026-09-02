import type {
  IPersistenceDescriptor,
  IPlatformModule,
  IPlatformModuleContext,
  IPlatformModuleManifest,
} from '@prosto/platform-sdk';
import { fileURLToPath } from 'node:url';
import {
  createTypeOrmPersistenceDescriptor,
  TYPEORM_DATA_SOURCE_SERVICE_TOKEN,
  TypeOrmPersistenceProvider,
} from '@prosto/platform-adapter-typeorm';
import { RuntimeBuilder } from '@prosto/platform-core';
import Fastify from 'fastify';
import {
  Entity,
  PrimaryGeneratedColumn,
  type MigrationInterface,
  Table,
  type QueryRunner,
} from 'typeorm';

import 'reflect-metadata';

// Entities

@Entity('platform_audit_entry')
class PlatformAuditEntry {
  @PrimaryGeneratedColumn()
  id!: number;
}

@Entity('orders_order')
class OrdersOrder {
  @PrimaryGeneratedColumn()
  id!: number;
}

// Migrations

class platform_create_audit_entry1710000001000 implements MigrationInterface {
  async up(queryRunner: QueryRunner): Promise<void> {
    await queryRunner.createTable(
      new Table({
        name: 'platform_audit_entry',
        columns: [{ name: 'id', type: 'integer', isPrimary: true }],
      }),
    );
  }

  async down(queryRunner: QueryRunner): Promise<void> {
    await queryRunner.dropTable('platform_audit_entry');
  }
}

class orders_create_order1710000001001 implements MigrationInterface {
  async up(queryRunner: QueryRunner): Promise<void> {
    await queryRunner.createTable(
      new Table({
        name: 'orders_order',
        columns: [{ name: 'id', type: 'integer', isPrimary: true }],
      }),
    );
  }

  async down(queryRunner: QueryRunner): Promise<void> {
    await queryRunner.dropTable('orders_order');
  }
}

// Platform persistence descriptor
const platformPersistenceDescriptor: IPersistenceDescriptor = {
  owner: 'platform',
  ownerId: 'platform',
  payload: createTypeOrmPersistenceDescriptor({
    entities: [PlatformAuditEntry],
    migrations: [platform_create_audit_entry1710000001000],
  }),
};

// Module manifest
const ordersManifest: IPlatformModuleManifest = {
  id: 'orders',
  version: '1.0.0',
  sdkVersion: '^0.0.0',
  title: 'Orders example',
  dependencies: [],
};

// Orders module
class OrdersModule implements IPlatformModule {
  init(context: IPlatformModuleContext): void {
    context.persistence?.descriptors?.register(context.moduleId, {
      owner: 'module',
      ownerId: context.moduleId,
      payload: createTypeOrmPersistenceDescriptor({
        entities: [OrdersOrder],
        migrations: [orders_create_order1710000001001],
      }),
    });
  }

  async start(context: IPlatformModuleContext): Promise<void> {
    const dataSource = context.services.resolveRequired(
      TYPEORM_DATA_SOURCE_SERVICE_TOKEN,
    );

    console.log(
      `[orders module] DataSource is ready: ${dataSource.isInitialized}`,
    );

    const ordersRepository = dataSource.getRepository(OrdersOrder);
    const allOrders = await ordersRepository.find();

    console.log(
      `[orders module] All orders: ${JSON.stringify(allOrders, null, 2)}`,
    );
  }

  stop(_context: IPlatformModuleContext): void {
    return;
  }
}

// Main entry point
async function main(): Promise<void> {
  const runtime = new RuntimeBuilder().build({
    configDir: fileURLToPath(new URL('../config', import.meta.url)),
    persistenceProvider: new TypeOrmPersistenceProvider(),
    platformPersistenceDescriptor,
    modules: [
      {
        type: 'memory',
        manifest: ordersManifest,
        module: new OrdersModule(),
      },
    ],
  });

  await runtime.start();

  console.log(JSON.stringify(runtime.reports.startup, null, 2));

  const shutdown = async (): Promise<void> => {
    console.log('Shutting down...');
    await runtime.stop();
    console.log(JSON.stringify(runtime.reports.shutdown, null, 2));
    process.exit(0);
  };

  process.on('SIGINT', shutdown);
  process.on('SIGTERM', shutdown);

  const fastify = Fastify({
    logger: true,
  });

  fastify.get('/', async function handler(_request, _reply) {
    return runtime.reports.startup;
  });

  await fastify.listen({ port: 3001 }).catch((error) => {
    fastify.log.error(error);

    throw new Error(error instanceof Error ? error.message : String(error), {
      cause: error,
    });
  });
}

main().catch(() => {
  process.exit(1);
});
