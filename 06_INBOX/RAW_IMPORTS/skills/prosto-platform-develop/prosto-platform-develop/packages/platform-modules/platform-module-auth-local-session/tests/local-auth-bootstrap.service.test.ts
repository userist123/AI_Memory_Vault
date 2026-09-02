import { PlatformArgon2idPasswordHasher } from '@prosto/platform-adapter-auth-local';
import { DataSource } from 'typeorm';
import { afterEach, describe, expect, it } from 'vitest';
import {
  LocalAuthAccountEntity,
  LocalAuthFailedLoginEntity,
  LocalAuthSessionEntity,
} from '@/entities/index.js';
import { auth_local_session_create_tables1710000000001 } from '@/migrations/index.js';
import { LocalAuthBootstrapService } from '@/services/index.js';

import 'reflect-metadata';

let dataSource: DataSource | undefined;

async function createDataSource(): Promise<DataSource> {
  dataSource = new DataSource({
    type: 'sqlite',
    database: ':memory:',
    entities: [
      LocalAuthAccountEntity,
      LocalAuthSessionEntity,
      LocalAuthFailedLoginEntity,
    ],
    migrations: [auth_local_session_create_tables1710000000001],
  });

  await dataSource.initialize();
  await dataSource.runMigrations();

  return dataSource;
}

afterEach(async (): Promise<void> => {
  await dataSource?.destroy();
  dataSource = undefined;
});

describe('LocalAuthBootstrapService', (): void => {
  it('creates exactly one forced-change admin account and persists only its hash', async (): Promise<void> => {
    const source = await createDataSource();
    const service = new LocalAuthBootstrapService(
      source,
      new PlatformArgon2idPasswordHasher(),
    );

    const result = await service.bootstrap();
    const account = await source
      .getRepository(LocalAuthAccountEntity)
      .findOneByOrFail({ username: 'admin' });

    expect(result.created).toBe(true);
    expect(result.password).toHaveLength(32);
    expect(account.passwordHash).toMatch(/^\$argon2id\$/u);
    expect(account.passwordHash).not.toBe(result.password);
    expect(account.rolesJson).toBe('["admin"]');
    expect(account.mustChangePassword).toBe(true);
    await expect(service.bootstrap()).resolves.toEqual({ created: false });
    await expect(
      source.getRepository(LocalAuthAccountEntity).count(),
    ).resolves.toBe(1);
  });

  it('does not bootstrap when any account already exists', async (): Promise<void> => {
    const source = await createDataSource();
    const repository = source.getRepository(LocalAuthAccountEntity);
    await repository.insert({
      id: '11111111-1111-4111-8111-111111111111',
      username: 'operator',
      passwordHash: '$argon2id$placeholder',
      rolesJson: '[]',
      permissionsJson: '[]',
      mustChangePassword: false,
      createdAt: 1,
      updatedAt: 1,
      disabledAt: null,
      lockoutUntil: null,
    });

    await expect(
      new LocalAuthBootstrapService(
        source,
        new PlatformArgon2idPasswordHasher(),
      ).bootstrap(),
    ).resolves.toEqual({ created: false });
  });
});
