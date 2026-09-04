import {
  normalizePlatformLocalAuthUsername,
  type IPlatformLocalAuthAccount,
  type IPlatformLocalAuthSession,
  type IPlatformLocalAuthSessionStore,
} from '@prosto/platform-adapter-auth-local';
import type { DataSource } from 'typeorm';
import {
  LocalAuthAccountEntity,
  LocalAuthSessionEntity,
} from '@/entities/index.js';

const HASH_PATTERN = /^[A-Za-z0-9_-]{43}$/u;
const MAX_IDENTITY_ENTRIES = 100;
const MAX_IDENTITY_ENTRY_BYTES = 128;

/** @internal */
export class TypeOrmLocalAuthSessionStore implements IPlatformLocalAuthSessionStore {
  constructor(private readonly _dataSource: DataSource) {}

  async findAccountByUsername(
    normalizedUsername: string,
  ): Promise<IPlatformLocalAuthAccount | undefined> {
    const entity = await this._dataSource
      .getRepository(LocalAuthAccountEntity)
      .findOneBy({ username: normalizedUsername });

    return entity === null ? undefined : this._toAccount(entity);
  }

  async findAccountById(
    accountId: string,
  ): Promise<IPlatformLocalAuthAccount | undefined> {
    const entity = await this._dataSource
      .getRepository(LocalAuthAccountEntity)
      .findOneBy({ id: accountId });

    return entity === null ? undefined : this._toAccount(entity);
  }

  async updateAccountPassword(input: {
    readonly accountId: string;
    readonly passwordHash: string;
    readonly mustChangePassword: boolean;
  }): Promise<boolean> {
    this._assertPasswordHash(input.passwordHash);

    const result = await this._dataSource
      .getRepository(LocalAuthAccountEntity)
      .createQueryBuilder()
      .update()
      .set({
        passwordHash: input.passwordHash,
        mustChangePassword: input.mustChangePassword,
        updatedAt: Date.now(),
      })
      .where('id = :accountId', { accountId: input.accountId })
      .execute();

    return result.affected === 1;
  }

  async findSession(
    sessionTokenHash: string,
  ): Promise<IPlatformLocalAuthSession | undefined> {
    const entity = await this._dataSource
      .getRepository(LocalAuthSessionEntity)
      .findOneBy({ sessionTokenHash });

    return entity === null ? undefined : this._toSession(entity);
  }

  async touchSession(input: {
    readonly sessionTokenHash: string;
    readonly lastSeenAt: number;
    readonly idleExpiresAt: number;
  }): Promise<void> {
    this._assertTimestamp(input.lastSeenAt);
    this._assertTimestamp(input.idleExpiresAt);

    await this._dataSource
      .getRepository(LocalAuthSessionEntity)
      .createQueryBuilder()
      .update()
      .set({
        lastSeenAt: input.lastSeenAt,
        idleExpiresAt: input.idleExpiresAt,
      })
      .where('sessionTokenHash = :sessionTokenHash', {
        sessionTokenHash: input.sessionTokenHash,
      })
      .execute();
  }

  async rotateSessionCsrfToken(input: {
    readonly sessionTokenHash: string;
    readonly csrfTokenHash: string;
  }): Promise<boolean> {
    this._assertHash(input.sessionTokenHash);
    this._assertHash(input.csrfTokenHash);

    const result = await this._dataSource
      .getRepository(LocalAuthSessionEntity)
      .update(
        { sessionTokenHash: input.sessionTokenHash },
        {
          csrfTokenHash: input.csrfTokenHash,
        },
      );

    return result.affected === 1;
  }

  async deleteSession(sessionTokenHash: string): Promise<void> {
    await this._dataSource
      .getRepository(LocalAuthSessionEntity)
      .delete({ sessionTokenHash });
  }

  async replaceAccountSessions(input: {
    readonly accountId: string;
    readonly session: IPlatformLocalAuthSession;
  }): Promise<void> {
    this._assertSession(input.session);

    await this._dataSource.transaction(async (manager): Promise<void> => {
      await manager
        .getRepository(LocalAuthSessionEntity)
        .delete({ accountId: input.accountId });
      await manager
        .getRepository(LocalAuthSessionEntity)
        .insert(this._sessionEntity(input.session));
    });
  }

  private _sessionEntity(
    session: IPlatformLocalAuthSession,
  ): LocalAuthSessionEntity {
    const entity = new LocalAuthSessionEntity();
    entity.sessionTokenHash = session.sessionTokenHash;
    entity.accountId = session.accountId;
    entity.csrfTokenHash = session.csrfTokenHash;
    entity.createdAt = session.createdAt;
    entity.lastSeenAt = session.lastSeenAt;
    entity.idleExpiresAt = session.idleExpiresAt;
    entity.absoluteExpiresAt = session.absoluteExpiresAt;
    return entity;
  }

  private _toAccount(
    entity: LocalAuthAccountEntity,
  ): IPlatformLocalAuthAccount {
    const account: IPlatformLocalAuthAccount = {
      id: entity.id,
      username: entity.username,
      passwordHash: entity.passwordHash,
      roles: this._parseIdentityArray(entity.rolesJson),
      permissions: this._parseIdentityArray(entity.permissionsJson),
      mustChangePassword: entity.mustChangePassword,
      disabledAt:
        entity.disabledAt === null
          ? undefined
          : this._timestamp(entity.disabledAt),
      lockoutUntil:
        entity.lockoutUntil === null
          ? undefined
          : this._timestamp(entity.lockoutUntil),
    };

    if (
      !this._isSafeText(account.id, 36) ||
      normalizePlatformLocalAuthUsername(account.username) !==
        account.username ||
      !this._isSafeText(account.username, 255) ||
      typeof account.mustChangePassword !== 'boolean'
    ) {
      throw new Error('Invalid stored local authentication account.');
    }

    this._assertPasswordHash(account.passwordHash);
    return Object.freeze(account);
  }

  private _toSession(
    entity: LocalAuthSessionEntity,
  ): IPlatformLocalAuthSession {
    const session: IPlatformLocalAuthSession = {
      sessionTokenHash: entity.sessionTokenHash,
      accountId: entity.accountId,
      csrfTokenHash: entity.csrfTokenHash,
      createdAt: this._timestamp(entity.createdAt),
      lastSeenAt: this._timestamp(entity.lastSeenAt),
      idleExpiresAt: this._timestamp(entity.idleExpiresAt),
      absoluteExpiresAt: this._timestamp(entity.absoluteExpiresAt),
    };

    this._assertSession(session);
    return Object.freeze(session);
  }

  private _assertSession(session: IPlatformLocalAuthSession): void {
    if (
      !HASH_PATTERN.test(session.sessionTokenHash) ||
      !HASH_PATTERN.test(session.csrfTokenHash) ||
      !this._isSafeText(session.accountId, 36) ||
      !Number.isSafeInteger(session.createdAt) ||
      !Number.isSafeInteger(session.lastSeenAt) ||
      !Number.isSafeInteger(session.idleExpiresAt) ||
      !Number.isSafeInteger(session.absoluteExpiresAt) ||
      session.createdAt > session.lastSeenAt ||
      session.lastSeenAt > session.idleExpiresAt ||
      session.idleExpiresAt > session.absoluteExpiresAt
    ) {
      throw new Error('Invalid stored local authentication session.');
    }
  }

  private _parseIdentityArray(value: string): readonly string[] {
    try {
      const parsed: unknown = JSON.parse(value);

      if (
        !Array.isArray(parsed) ||
        !parsed.every((entry) => typeof entry === 'string')
      ) {
        throw new Error();
      }

      const canonical = this._canonicalIdentityArray(parsed);

      if (canonical !== value) {
        throw new Error();
      }

      return Object.freeze([...parsed]);
    } catch {
      throw new Error('Invalid stored local authentication identity array.');
    }
  }

  private _canonicalIdentityArray(values: readonly string[]): string {
    if (
      values.length > MAX_IDENTITY_ENTRIES ||
      new Set(values).size !== values.length ||
      values.some((value) => !this._isSafeText(value, MAX_IDENTITY_ENTRY_BYTES))
    ) {
      throw new Error('Invalid stored local authentication identity array.');
    }

    const json = JSON.stringify(values.map((value) => value.trim()));

    if (Buffer.byteLength(json, 'utf8') > 8192) {
      throw new Error('Invalid stored local authentication identity array.');
    }

    return json;
  }

  private _assertPasswordHash(value: string): void {
    if (
      typeof value !== 'string' ||
      value.length === 0 ||
      value.length > 1024
    ) {
      throw new Error('Invalid stored local authentication password hash.');
    }
  }

  private _assertHash(value: string): void {
    if (!HASH_PATTERN.test(value)) {
      throw new Error('Invalid local authentication token hash.');
    }
  }

  private _timestamp(value: number | string): number {
    const timestamp = typeof value === 'number' ? value : Number(value);
    this._assertTimestamp(timestamp);
    return timestamp;
  }

  private _assertTimestamp(value: number): void {
    if (!Number.isSafeInteger(value)) {
      throw new Error('Invalid stored local authentication timestamp.');
    }
  }

  private _isSafeText(value: string, maximumBytes: number): boolean {
    return (
      typeof value === 'string' &&
      value.trim().length > 0 &&
      !/\p{Cc}/u.test(value) &&
      Buffer.byteLength(value, 'utf8') <= maximumBytes
    );
  }
}
