import type { IPlatformLocalAuthPasswordHasher } from '@prosto/platform-adapter-auth-local';
import type { IPlatformLocalAuthBootstrapResult } from '@/interfaces/index.js';
import { randomBytes, randomUUID } from 'node:crypto';
import { DataSource, type DataSourceOptions } from 'typeorm';
import {
  LocalAuthAccountEntity,
  LocalAuthFailedLoginEntity,
  LocalAuthSessionEntity,
} from '@/entities/index.js';
import { auth_local_session_create_tables1710000000001 } from '@/migrations/index.js';

const INITIAL_USERNAME = 'admin';

/** @internal */
export function createPlatformLocalAuthBootstrapDataSource(
  database: string,
): DataSource {
  const options: DataSourceOptions = {
    type: 'sqlite',
    database,
    entities: [
      LocalAuthAccountEntity,
      LocalAuthSessionEntity,
      LocalAuthFailedLoginEntity,
    ],
    migrations: [auth_local_session_create_tables1710000000001],
    synchronize: false,
  };

  return new DataSource(options);
}

/** @internal */
export class LocalAuthBootstrapService {
  constructor(
    private readonly _dataSource: DataSource,
    private readonly _passwordHasher: IPlatformLocalAuthPasswordHasher,
    private readonly _roles: readonly string[] = ['admin'],
    private readonly _permissions: readonly string[] = [],
  ) {}

  async requiresBootstrap(): Promise<boolean> {
    return !(await this._dataSource
      .getRepository(LocalAuthAccountEntity)
      .exists());
  }

  async bootstrap(): Promise<IPlatformLocalAuthBootstrapResult> {
    if (!(await this.requiresBootstrap())) {
      return Object.freeze({ created: false });
    }

    const password = randomBytes(24).toString('base64url');
    const passwordHash = await this._passwordHasher.hash(password);
    const now = Date.now();

    const created = await this._dataSource.transaction(
      async (manager): Promise<boolean> => {
        const repository = manager.getRepository(LocalAuthAccountEntity);

        if (await repository.exists()) {
          return false;
        }

        try {
          await repository.insert({
            id: randomUUID(),
            username: INITIAL_USERNAME,
            passwordHash,
            rolesJson: this._identityJson(this._roles),
            permissionsJson: this._identityJson(this._permissions),
            mustChangePassword: true,
            createdAt: now,
            updatedAt: now,
            disabledAt: null,
            lockoutUntil: null,
          });

          return true;
        } catch (error) {
          if (await repository.exists()) {
            return false;
          }

          throw error;
        }
      },
    );

    return created
      ? Object.freeze({ created: true, username: INITIAL_USERNAME, password })
      : Object.freeze({ created: false });
  }

  private _identityJson(values: readonly string[]): string {
    if (
      values.length > 100 ||
      new Set(values).size !== values.length ||
      values.some(
        (value) =>
          typeof value !== 'string' ||
          value.trim().length === 0 ||
          value.length > 128 ||
          /\p{Cc}/u.test(value),
      )
    ) {
      throw new Error('Invalid local authentication bootstrap identity.');
    }

    return JSON.stringify(values.map((value) => value.trim()));
  }
}
