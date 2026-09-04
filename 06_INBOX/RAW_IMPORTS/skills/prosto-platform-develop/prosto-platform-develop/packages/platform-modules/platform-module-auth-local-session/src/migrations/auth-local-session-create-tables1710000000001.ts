import type { MigrationInterface, QueryRunner } from 'typeorm';
import { Table, TableIndex } from 'typeorm';

export class auth_local_session_create_tables1710000000001 implements MigrationInterface {
  async up(queryRunner: QueryRunner): Promise<void> {
    await queryRunner.createTable(
      new Table({
        name: 'auth_local_session_accounts',
        columns: [
          { name: 'id', type: 'varchar', length: '36', isPrimary: true },
          { name: 'username', type: 'varchar', length: '255', isUnique: true },
          { name: 'passwordHash', type: 'text' },
          { name: 'rolesJson', type: 'text' },
          { name: 'permissionsJson', type: 'text' },
          { name: 'mustChangePassword', type: 'boolean' },
          { name: 'createdAt', type: 'bigint' },
          { name: 'updatedAt', type: 'bigint' },
          { name: 'disabledAt', type: 'bigint', isNullable: true },
          { name: 'lockoutUntil', type: 'bigint', isNullable: true },
        ],
      }),
    );

    await queryRunner.createTable(
      new Table({
        name: 'auth_local_session_sessions',
        columns: [
          {
            name: 'sessionTokenHash',
            type: 'varchar',
            length: '43',
            isPrimary: true,
          },
          { name: 'accountId', type: 'varchar', length: '36' },
          { name: 'csrfTokenHash', type: 'varchar', length: '43' },
          { name: 'createdAt', type: 'bigint' },
          { name: 'lastSeenAt', type: 'bigint' },
          { name: 'idleExpiresAt', type: 'bigint' },
          { name: 'absoluteExpiresAt', type: 'bigint' },
        ],
      }),
    );

    await queryRunner.createIndex(
      'auth_local_session_sessions',
      new TableIndex({
        name: 'idx_auth_local_session_sessions_account',
        columnNames: ['accountId'],
      }),
    );

    await queryRunner.createIndex(
      'auth_local_session_sessions',
      new TableIndex({
        name: 'idx_auth_local_session_sessions_idle_expiry',
        columnNames: ['idleExpiresAt'],
      }),
    );

    await queryRunner.createIndex(
      'auth_local_session_sessions',
      new TableIndex({
        name: 'idx_auth_local_session_sessions_absolute_expiry',
        columnNames: ['absoluteExpiresAt'],
      }),
    );

    await queryRunner.createTable(
      new Table({
        name: 'auth_local_session_failed_logins',
        columns: [
          {
            name: 'username',
            type: 'varchar',
            length: '255',
            isPrimary: true,
          },
          { name: 'failureCount', type: 'int' },
          { name: 'windowStartedAt', type: 'bigint' },
          { name: 'blockedUntil', type: 'bigint', isNullable: true },
        ],
      }),
    );
  }

  async down(queryRunner: QueryRunner): Promise<void> {
    await queryRunner.dropTable('auth_local_session_failed_logins');
    await queryRunner.dropTable('auth_local_session_sessions');
    await queryRunner.dropTable('auth_local_session_accounts');
  }
}

// TypeORM derives the migration identifier from constructor.name. Preserve it
// when Vite minifies this publishable package.
Object.defineProperty(auth_local_session_create_tables1710000000001, 'name', {
  value: 'auth_local_session_create_tables1710000000001',
});
