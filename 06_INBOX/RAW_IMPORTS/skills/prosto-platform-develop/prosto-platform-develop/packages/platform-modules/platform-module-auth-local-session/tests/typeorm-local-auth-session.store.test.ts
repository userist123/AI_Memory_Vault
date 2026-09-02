import type { IPlatformLocalAuthSession } from '@prosto/platform-adapter-auth-local';
import { DataSource } from 'typeorm';
import { afterEach, describe, expect, it } from 'vitest';
import {
  LocalAuthAccountEntity,
  LocalAuthSessionEntity,
} from '@/entities/index.js';
import { TypeOrmLocalAuthSessionStore } from '@/stores/index.js';

import 'reflect-metadata';

const HASH = 'a'.repeat(43);
const CSRF_HASH = 'b'.repeat(43);
let dataSource: DataSource | undefined;

async function createStore(): Promise<TypeOrmLocalAuthSessionStore> {
  dataSource = new DataSource({
    type: 'sqlite',
    database: ':memory:',
    entities: [LocalAuthAccountEntity, LocalAuthSessionEntity],
    synchronize: true,
  });

  await dataSource.initialize();
  await dataSource.getRepository(LocalAuthAccountEntity).insert({
    id: '11111111-1111-4111-8111-111111111111',
    username: 'admin',
    passwordHash: '$argon2id$hash',
    rolesJson: '["admin"]',
    permissionsJson: '[]',
    mustChangePassword: false,
    createdAt: 1,
    updatedAt: 1,
    disabledAt: null,
    lockoutUntil: null,
  });

  return new TypeOrmLocalAuthSessionStore(dataSource);
}

function session(hash: string): IPlatformLocalAuthSession {
  return {
    sessionTokenHash: hash,
    accountId: '11111111-1111-4111-8111-111111111111',
    csrfTokenHash: CSRF_HASH,
    createdAt: 1,
    lastSeenAt: 1,
    idleExpiresAt: 2,
    absoluteExpiresAt: 3,
  };
}

afterEach(async (): Promise<void> => {
  await dataSource?.destroy();
  dataSource = undefined;
});

describe('TypeOrmLocalAuthSessionStore', (): void => {
  it('atomically replaces all account sessions to prevent session fixation', async (): Promise<void> => {
    const store = await createStore();

    await store.replaceAccountSessions({
      accountId: session(HASH).accountId,
      session: session(HASH),
    });

    const replacementHash = 'c'.repeat(43);

    await store.replaceAccountSessions({
      accountId: session(HASH).accountId,
      session: session(replacementHash),
    });

    await expect(store.findSession(HASH)).resolves.toBeUndefined();
    await expect(store.findSession(replacementHash)).resolves.toMatchObject({
      csrfTokenHash: CSRF_HASH,
    });
  });
});
