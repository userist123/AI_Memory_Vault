import { createHash } from 'node:crypto';
import type { DataSource } from 'typeorm';
import type {
  IMigrationLock,
  IMigrationLockFactoryInterface,
  ITypeOrmPersistenceConfig,
} from '@/interfaces/index.js';
import {
  MySqlMigrationLock,
  PostgresMigrationLock,
  SqliteMigrationLock,
  SqlServerMigrationLock,
} from '@/migration-locks/index.js';

export class MigrationLockFactory implements IMigrationLockFactoryInterface {
  create(
    dataSource: DataSource,
    configuration: ITypeOrmPersistenceConfig,
  ): IMigrationLock {
    const lockKey = this._createMigrationLockKey(configuration);

    switch (configuration.type) {
      case 'postgres':
        return new PostgresMigrationLock(
          dataSource.createQueryRunner(),
          lockKey,
        );

      case 'mysql':
      case 'mariadb':
        return new MySqlMigrationLock(dataSource.createQueryRunner(), lockKey);

      case 'mssql':
        return new SqlServerMigrationLock(
          dataSource.createQueryRunner(),
          lockKey,
        );

      case 'sqlite':
        return new SqliteMigrationLock(
          dataSource.createQueryRunner(),
          configuration.database === ':memory:',
        );

      default: {
        throw new Error(
          'A supported TypeORM dialect is required for migrations.',
        );
      }
    }
  }

  /**
   * Hashes non-secret database identity so lock names never reveal connection data.
   */
  protected _createMigrationLockKey(
    configuration: ITypeOrmPersistenceConfig,
  ): string {
    const identity = [
      'prosto:typeorm:migrations:v1',
      configuration.type,
      configuration.host ?? '',
      configuration.port ?? '',
      configuration.database ?? '',
      configuration.schema ?? '',
      // A URL is deliberately hashed as an opaque value and never logged.
      configuration.url ?? '',
    ].join('|');

    return createHash('sha256').update(identity).digest('hex');
  }
}
