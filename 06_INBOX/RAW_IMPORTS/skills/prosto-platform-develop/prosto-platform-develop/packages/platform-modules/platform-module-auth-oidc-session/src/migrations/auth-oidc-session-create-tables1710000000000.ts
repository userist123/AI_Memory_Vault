import type { MigrationInterface, QueryRunner } from 'typeorm';
import { Table, TableIndex } from 'typeorm';

export class auth_oidc_session_create_tables1710000000000 implements MigrationInterface {
  async up(queryRunner: QueryRunner): Promise<void> {
    await queryRunner.createTable(
      new Table({
        name: 'auth_oidc_session_sessions',
        columns: [
          {
            name: 'sessionIdHash',
            type: 'varchar',
            length: '43',
            isPrimary: true,
          },
          { name: 'subjectId', type: 'varchar', length: '255' },
          { name: 'rolesJson', type: 'text' },
          { name: 'permissionsJson', type: 'text' },
          { name: 'createdAt', type: 'bigint' },
          { name: 'lastSeenAt', type: 'bigint' },
          { name: 'absoluteExpiresAt', type: 'bigint' },
          { name: 'accessExpiresAt', type: 'bigint' },
          { name: 'version', type: 'int', default: '1' },
          {
            name: 'refreshLeaseId',
            type: 'varchar',
            length: '43',
            isNullable: true,
          },
          { name: 'refreshLeaseExpiresAt', type: 'bigint', isNullable: true },
          { name: 'refreshKeyId', type: 'varchar', length: '64' },
          { name: 'refreshNonce', type: 'varchar', length: '64' },
          { name: 'refreshTag', type: 'varchar', length: '64' },
          { name: 'refreshCiphertext', type: 'text' },
        ],
      }),
    );

    await queryRunner.createIndex(
      'auth_oidc_session_sessions',
      new TableIndex({
        name: 'idx_auth_oidc_session_sessions_expiry',
        columnNames: ['absoluteExpiresAt'],
      }),
    );

    await queryRunner.createTable(
      new Table({
        name: 'auth_oidc_session_transactions',
        columns: [
          {
            name: 'transactionIdHash',
            type: 'varchar',
            length: '43',
            isPrimary: true,
          },
          { name: 'stateHash', type: 'varchar', length: '43' },
          { name: 'nonce', type: 'varchar', length: '43' },
          { name: 'expiresAt', type: 'bigint' },
          {
            name: 'replacedSessionIdHash',
            type: 'varchar',
            length: '43',
            isNullable: true,
          },
          { name: 'pkceKeyId', type: 'varchar', length: '64' },
          { name: 'pkceNonce', type: 'varchar', length: '64' },
          { name: 'pkceTag', type: 'varchar', length: '64' },
          { name: 'pkceCiphertext', type: 'varchar', length: '256' },
        ],
      }),
    );

    await queryRunner.createIndex(
      'auth_oidc_session_transactions',
      new TableIndex({
        name: 'idx_auth_oidc_session_transactions_expiry',
        columnNames: ['expiresAt'],
      }),
    );

    await queryRunner.createIndex(
      'auth_oidc_session_transactions',
      new TableIndex({
        name: 'idx_auth_oidc_session_transactions_state',
        columnNames: ['stateHash'],
      }),
    );
  }

  async down(queryRunner: QueryRunner): Promise<void> {
    await queryRunner.dropTable('auth_oidc_session_transactions');
    await queryRunner.dropTable('auth_oidc_session_sessions');
  }
}

// TypeORM derives the migration identifier from constructor.name. Preserve it
// when Vite minifies this publishable package.
Object.defineProperty(auth_oidc_session_create_tables1710000000000, 'name', {
  value: 'auth_oidc_session_create_tables1710000000000',
});
