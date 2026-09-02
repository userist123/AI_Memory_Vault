import type {
  IPlatformOidcSessionRecord,
  IPlatformOidcTransactionRecord,
} from '@prosto/platform-adapter-auth-oidc-session';
import { DataSource } from 'typeorm';
import { afterEach, describe, expect, it } from 'vitest';
import { OidcSessionEntity, OidcTransactionEntity } from '@/entities/index.js';
import { auth_oidc_session_create_tables1710000000000 } from '@/migrations/index.js';
import { TypeOrmOidcSessionStore } from '@/stores/index.js';

import 'reflect-metadata';

const NOW = 1_700_000_000_000;
const HASH = 'a'.repeat(43);
const STATE_HASH = 'b'.repeat(43);
const TRANSACTION_HASH = 'c'.repeat(43);
const CIPHERTEXT = Object.freeze({
  keyId: 'key-1',
  nonce: Buffer.alloc(12, 1).toString('base64url'),
  tag: Buffer.alloc(16, 2).toString('base64url'),
  ciphertext: Buffer.from('ciphertext', 'utf8').toString('base64url'),
});

let dataSource: DataSource | undefined;

afterEach(async (): Promise<void> => {
  await dataSource?.destroy();
  dataSource = undefined;
});

function session(expiresAt = NOW + 60_000): IPlatformOidcSessionRecord {
  return {
    sessionIdHash: HASH,
    subjectId: 'subject',
    roles: ['admin'],
    permissions: ['read'],
    createdAt: NOW - 1_000,
    lastSeenAt: NOW - 1_000,
    absoluteExpiresAt: expiresAt,
    accessExpiresAt: NOW + 30_000,
    refreshToken: CIPHERTEXT,
  };
}

function transaction(expiresAt = NOW + 60_000): IPlatformOidcTransactionRecord {
  return {
    transactionIdHash: TRANSACTION_HASH,
    stateHash: STATE_HASH,
    nonce: 'd'.repeat(43),
    expiresAt,
    pkceVerifier: CIPHERTEXT,
  };
}

async function createStore(): Promise<TypeOrmOidcSessionStore> {
  dataSource = new DataSource({
    type: 'sqlite',
    database: ':memory:',
    entities: [OidcSessionEntity, OidcTransactionEntity],
    synchronize: true,
  });

  await dataSource.initialize();

  return new TypeOrmOidcSessionStore(dataSource);
}

describe('TypeOrmOidcSessionStore', (): void => {
  it('creates portable session tables through the migration', async (): Promise<void> => {
    dataSource = new DataSource({
      type: 'sqlite',
      database: ':memory:',
      migrations: [auth_oidc_session_create_tables1710000000000],
    });

    await dataSource.initialize();
    await dataSource.runMigrations();

    await expect(
      dataSource.query(
        "SELECT name FROM sqlite_master WHERE type='table' AND name='auth_oidc_session_sessions'",
      ),
    ).resolves.toHaveLength(1);
    await expect(
      dataSource.query(
        "SELECT name FROM sqlite_master WHERE type='table' AND name='auth_oidc_session_transactions'",
      ),
    ).resolves.toHaveLength(1);
  });

  it('persists records and conditionally owns a refresh lease', async (): Promise<void> => {
    const store = await createStore();

    await store.createTransaction(transaction());

    await expect(
      store.createSessionFromTransaction({
        transactionIdHash: TRANSACTION_HASH,
        stateHash: STATE_HASH,
        session: session(),
      }),
    ).resolves.toBe(true);

    await expect(
      store.acquireRefreshLease({
        sessionIdHash: HASH,
        leaseId: 'e'.repeat(43),
        leaseExpiresAt: NOW + 10_000,
        now: NOW,
      }),
    ).resolves.toBe('acquired');

    await expect(
      store.acquireRefreshLease({
        sessionIdHash: HASH,
        leaseId: 'f'.repeat(43),
        leaseExpiresAt: NOW + 10_000,
        now: NOW,
      }),
    ).resolves.toBe('held');

    await store.releaseRefreshLease(HASH, 'e'.repeat(43));

    expect(await store.findSession(HASH)).toMatchObject({
      subjectId: 'subject',
      roles: ['admin'],
      refreshLeaseId: undefined,
    });
  });

  it('purges no more than the bounded expired records', async (): Promise<void> => {
    const store = await createStore();
    await store.createTransaction(transaction(NOW - 1));

    await expect(
      store.createSessionFromTransaction({
        transactionIdHash: TRANSACTION_HASH,
        stateHash: STATE_HASH,
        session: session(NOW - 1),
      }),
    ).resolves.toBe(true);

    await store.cleanupExpired(NOW, 500);

    await expect(store.findSession(HASH)).resolves.toBeUndefined();
  });
});
