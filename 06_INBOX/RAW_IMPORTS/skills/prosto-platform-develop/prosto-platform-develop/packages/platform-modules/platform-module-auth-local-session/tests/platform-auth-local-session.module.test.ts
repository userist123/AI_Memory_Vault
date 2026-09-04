import type { IPlatformModuleContext } from '@prosto/platform-sdk';
import { DataSource } from 'typeorm';
import { afterEach, describe, expect, it } from 'vitest';
import {
  LocalAuthAccountEntity,
  LocalAuthFailedLoginEntity,
  LocalAuthSessionEntity,
} from '@/entities/index.js';
import { auth_local_session_create_tables1710000000001 } from '@/migrations/index.js';
import { PlatformAuthLocalSessionModule } from '@/platform-auth-local-session.module.js';

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

function context(source: DataSource): IPlatformModuleContext {
  return {
    moduleId: 'auth-local-session',
    services: {
      resolveRequired: <T>(): T => source as T,
    },
    logger: {
      info: (): void => undefined,
    },
  } as unknown as IPlatformModuleContext;
}

afterEach(async (): Promise<void> => {
  await dataSource?.destroy();
  dataSource = undefined;
});

describe('PlatformAuthLocalSessionModule', (): void => {
  it('starts only after interactive bootstrap and exposes the local provider facade', async (): Promise<void> => {
    const source = await createDataSource();
    const output: string[] = [];
    const module = new PlatformAuthLocalSessionModule({
      origin: 'http://127.0.0.1:3001',
      bootstrapOutput: {
        isInteractive: true,
        write: (message: string) => output.push(message),
      },
    });

    await module.start(context(source));

    expect(module.facade.ready).toBe(true);
    expect(module.facade.provider.mode).toBe('local');
    expect(module.facade.provider.publicRouteRegistrations).toHaveLength(4);
    expect(output.join('')).toContain('One-time password:');
    await expect(
      source.getRepository(LocalAuthAccountEntity).count(),
    ).resolves.toBe(1);
  });

  it('refuses non-interactive initialization without persisting a secret', async (): Promise<void> => {
    const source = await createDataSource();
    const module = new PlatformAuthLocalSessionModule({
      origin: 'http://127.0.0.1:3001',
      bootstrapOutput: { isInteractive: false, write: (): void => undefined },
    });

    await expect(module.start(context(source))).rejects.toThrow(
      'prosto-platform auth bootstrap-local',
    );
    expect(module.facade.ready).toBe(false);
    await expect(
      source.getRepository(LocalAuthAccountEntity).count(),
    ).resolves.toBe(0);
  });
});
