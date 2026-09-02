import type {
  IPlatformOidcSessionRecord,
  IPlatformOidcSessionStore,
  IPlatformOidcTransactionRecord,
} from '@prosto/platform-adapter-auth-oidc-session';
import type { IPlatformSecretCiphertext } from '@prosto/platform-sdk';
import type { DataSource } from 'typeorm';
import { OidcSessionEntity, OidcTransactionEntity } from '@/entities/index.js';

const HASH_PATTERN = /^[A-Za-z0-9_-]{43}$/u;
const BASE64_URL_PATTERN = /^[A-Za-z0-9_-]+$/u;
const ASCII_TEXT_PATTERN = /^[\x21-\x7e]+$/u;
const MAX_REFRESH_CIPHERTEXT_LENGTH = 21848;
const SESSION_IDLE_TTL_MS = 30 * 60 * 1000;

/** @internal */
export class TypeOrmOidcSessionStore implements IPlatformOidcSessionStore {
  constructor(private readonly _dataSource: DataSource) {}

  async findSession(
    sessionIdHash: string,
  ): Promise<IPlatformOidcSessionRecord | undefined> {
    const entity = await this._dataSource
      .getRepository(OidcSessionEntity)
      .findOneBy({ sessionIdHash });

    return entity ? this._toSessionRecord(entity) : undefined;
  }

  async touchSession(sessionIdHash: string, now: number): Promise<void> {
    await this._dataSource
      .getRepository(OidcSessionEntity)
      .createQueryBuilder()
      .update()
      .set({ lastSeenAt: now })
      .where('sessionIdHash = :sessionIdHash')
      .andWhere('lastSeenAt <= :touchBefore', {
        sessionIdHash,
        touchBefore: now - 5 * 60 * 1000,
      })
      .execute();
  }

  async deleteSession(sessionIdHash: string): Promise<void> {
    await this._dataSource
      .getRepository(OidcSessionEntity)
      .delete({ sessionIdHash });
  }

  async createTransaction(
    record: IPlatformOidcTransactionRecord,
  ): Promise<void> {
    this._assertTransactionRecord(record);

    await this._dataSource.getRepository(OidcTransactionEntity).insert({
      transactionIdHash: record.transactionIdHash,
      stateHash: record.stateHash,
      nonce: record.nonce,
      expiresAt: record.expiresAt,
      replacedSessionIdHash: record.replacedSessionIdHash ?? null,
      ...this._ciphertextColumns('pkce', record.pkceVerifier),
    });
  }

  async findTransaction(
    transactionIdHash: string,
    stateHash: string,
    now: number,
  ): Promise<IPlatformOidcTransactionRecord | undefined> {
    const entity = await this._dataSource
      .getRepository(OidcTransactionEntity)
      .createQueryBuilder('transaction')
      .where('transaction.transactionIdHash = :transactionIdHash', {
        transactionIdHash,
      })
      .andWhere('transaction.stateHash = :stateHash', { stateHash })
      .andWhere('transaction.expiresAt > :now', { now })
      .getOne();

    return entity ? this._toTransactionRecord(entity) : undefined;
  }

  async consumeTransaction(
    transactionIdHash: string,
    stateHash: string,
  ): Promise<void> {
    await this._dataSource
      .getRepository(OidcTransactionEntity)
      .delete({ transactionIdHash, stateHash });
  }

  async createSessionFromTransaction(input: {
    readonly transactionIdHash: string;
    readonly stateHash: string;
    readonly session: IPlatformOidcSessionRecord;
    readonly replacedSessionIdHash?: string;
  }): Promise<boolean> {
    this._assertSessionRecord(input.session);

    return this._dataSource.transaction(async (manager) => {
      const consumed = await manager
        .getRepository(OidcTransactionEntity)
        .delete({
          transactionIdHash: input.transactionIdHash,
          stateHash: input.stateHash,
        });

      if (consumed.affected !== 1) return false;

      if (input.replacedSessionIdHash !== undefined) {
        await manager
          .getRepository(OidcSessionEntity)
          .delete({ sessionIdHash: input.replacedSessionIdHash });
      }

      await manager
        .getRepository(OidcSessionEntity)
        .insert(this._sessionEntity(input.session));

      return true;
    });
  }

  async acquireRefreshLease(input: {
    readonly sessionIdHash: string;
    readonly leaseId: string;
    readonly leaseExpiresAt: number;
    readonly now: number;
  }): Promise<'acquired' | 'held' | 'missing'> {
    const result = await this._dataSource
      .getRepository(OidcSessionEntity)
      .createQueryBuilder()
      .update()
      .set({
        refreshLeaseId: input.leaseId,
        refreshLeaseExpiresAt: input.leaseExpiresAt,
      })
      .where('sessionIdHash = :sessionIdHash', {
        sessionIdHash: input.sessionIdHash,
      })
      .andWhere(
        '(refreshLeaseExpiresAt IS NULL OR refreshLeaseExpiresAt <= :now)',
        { now: input.now },
      )
      .execute();

    if (result.affected === 1) return 'acquired';

    const exists = await this._dataSource
      .getRepository(OidcSessionEntity)
      .exists({ where: { sessionIdHash: input.sessionIdHash } });

    return exists ? 'held' : 'missing';
  }

  async releaseRefreshLease(
    sessionIdHash: string,
    leaseId: string,
  ): Promise<void> {
    await this._dataSource
      .getRepository(OidcSessionEntity)
      .createQueryBuilder()
      .update()
      .set({ refreshLeaseId: null, refreshLeaseExpiresAt: null })
      .where('sessionIdHash = :sessionIdHash', { sessionIdHash })
      .andWhere('refreshLeaseId = :leaseId', { leaseId })
      .execute();
  }

  async updateSessionAfterRefresh(input: {
    readonly sessionIdHash: string;
    readonly leaseId: string;
    readonly subjectId: string;
    readonly roles: readonly string[];
    readonly permissions: readonly string[];
    readonly accessExpiresAt: number;
    readonly refreshToken: IPlatformSecretCiphertext;
  }): Promise<boolean> {
    this._assertIdentity(input.subjectId, input.roles, input.permissions);
    this._assertCiphertext(input.refreshToken, MAX_REFRESH_CIPHERTEXT_LENGTH);

    const result = await this._dataSource
      .getRepository(OidcSessionEntity)
      .createQueryBuilder()
      .update()
      .set({
        subjectId: input.subjectId,
        rolesJson: this._canonicalArray(input.roles),
        permissionsJson: this._canonicalArray(input.permissions),
        accessExpiresAt: input.accessExpiresAt,
        refreshLeaseId: null,
        refreshLeaseExpiresAt: null,
        ...this._ciphertextColumns('refresh', input.refreshToken),
      })
      .where('sessionIdHash = :sessionIdHash', {
        sessionIdHash: input.sessionIdHash,
      })
      .andWhere('refreshLeaseId = :leaseId', { leaseId: input.leaseId })
      .execute();

    return result.affected === 1;
  }

  async cleanupExpired(now: number, limit: number): Promise<void> {
    const sessions = await this._dataSource
      .getRepository(OidcSessionEntity)
      .createQueryBuilder('session')
      .select('session.sessionIdHash', 'id')
      .where('session.absoluteExpiresAt <= :now', { now })
      .orWhere('session.lastSeenAt <= :idleExpiresAt', {
        idleExpiresAt: now - SESSION_IDLE_TTL_MS,
      })
      .orderBy('session.absoluteExpiresAt', 'ASC')
      .limit(limit)
      .getRawMany<{ readonly id: string }>();

    if (sessions.length) {
      await this._dataSource
        .getRepository(OidcSessionEntity)
        .createQueryBuilder()
        .delete()
        .where('sessionIdHash IN (:...identifiers)', {
          identifiers: sessions.map((session) => session.id),
        })
        .andWhere(
          '(absoluteExpiresAt <= :now OR lastSeenAt <= :idleExpiresAt)',
          { now, idleExpiresAt: now - SESSION_IDLE_TTL_MS },
        )
        .execute();
    }

    const transactions = await this._dataSource
      .getRepository(OidcTransactionEntity)
      .createQueryBuilder('transaction')
      .select('transaction.transactionIdHash', 'id')
      .where('transaction.expiresAt <= :now', { now })
      .orderBy('transaction.expiresAt', 'ASC')
      .limit(limit)
      .getRawMany<{ readonly id: string }>();

    if (transactions.length) {
      await this._dataSource
        .getRepository(OidcTransactionEntity)
        .createQueryBuilder()
        .delete()
        .where('transactionIdHash IN (:...identifiers)', {
          identifiers: transactions.map((transaction) => transaction.id),
        })
        .andWhere('expiresAt <= :now', { now })
        .execute();
    }
  }

  private _sessionEntity(
    record: IPlatformOidcSessionRecord,
  ): OidcSessionEntity {
    const entity = new OidcSessionEntity();

    entity.sessionIdHash = record.sessionIdHash;
    entity.subjectId = record.subjectId;
    entity.rolesJson = this._canonicalArray(record.roles);
    entity.permissionsJson = this._canonicalArray(record.permissions);
    entity.createdAt = record.createdAt;
    entity.lastSeenAt = record.lastSeenAt;
    entity.absoluteExpiresAt = record.absoluteExpiresAt;
    entity.accessExpiresAt = record.accessExpiresAt;
    entity.version = 1;
    entity.refreshLeaseId = record.refreshLeaseId ?? null;
    entity.refreshLeaseExpiresAt = record.refreshLeaseExpiresAt ?? null;
    entity.refreshKeyId = record.refreshToken.keyId;
    entity.refreshNonce = record.refreshToken.nonce;
    entity.refreshTag = record.refreshToken.tag;
    entity.refreshCiphertext = record.refreshToken.ciphertext;

    return entity;
  }

  private _toSessionRecord(
    entity: OidcSessionEntity,
  ): IPlatformOidcSessionRecord {
    const record: IPlatformOidcSessionRecord = {
      sessionIdHash: entity.sessionIdHash,
      subjectId: entity.subjectId,
      roles: this._parseArray(entity.rolesJson),
      permissions: this._parseArray(entity.permissionsJson),
      createdAt: this._timestamp(entity.createdAt),
      lastSeenAt: this._timestamp(entity.lastSeenAt),
      absoluteExpiresAt: this._timestamp(entity.absoluteExpiresAt),
      accessExpiresAt: this._timestamp(entity.accessExpiresAt),
      refreshToken: this._columnsCiphertext('refresh', entity),
      refreshLeaseId: entity.refreshLeaseId ?? undefined,
      refreshLeaseExpiresAt:
        entity.refreshLeaseExpiresAt === null
          ? undefined
          : this._timestamp(entity.refreshLeaseExpiresAt),
    };

    this._assertSessionRecord(record);

    return Object.freeze(record);
  }

  private _toTransactionRecord(
    entity: OidcTransactionEntity,
  ): IPlatformOidcTransactionRecord {
    const record = {
      transactionIdHash: entity.transactionIdHash,
      stateHash: entity.stateHash,
      nonce: entity.nonce,
      expiresAt: this._timestamp(entity.expiresAt),
      pkceVerifier: this._columnsCiphertext('pkce', entity),
      replacedSessionIdHash: entity.replacedSessionIdHash ?? undefined,
    };

    this._assertTransactionRecord(record);

    return Object.freeze(record);
  }

  private _ciphertextColumns(
    prefix: 'refresh' | 'pkce',
    value: IPlatformSecretCiphertext,
  ): Record<string, string> {
    this._assertCiphertext(
      value,
      prefix === 'refresh' ? MAX_REFRESH_CIPHERTEXT_LENGTH : 256,
    );

    return {
      [`${prefix}KeyId`]: value.keyId,
      [`${prefix}Nonce`]: value.nonce,
      [`${prefix}Tag`]: value.tag,
      [`${prefix}Ciphertext`]: value.ciphertext,
    };
  }

  private _columnsCiphertext(
    prefix: 'refresh' | 'pkce',
    entity: OidcSessionEntity | OidcTransactionEntity,
  ): IPlatformSecretCiphertext {
    const value =
      prefix === 'refresh'
        ? this._sessionCiphertext(entity as OidcSessionEntity)
        : this._transactionCiphertext(entity as OidcTransactionEntity);

    this._assertCiphertext(
      value,
      prefix === 'refresh' ? MAX_REFRESH_CIPHERTEXT_LENGTH : 256,
    );

    return Object.freeze(value);
  }

  private _sessionCiphertext(
    entity: OidcSessionEntity,
  ): IPlatformSecretCiphertext {
    return {
      keyId: entity.refreshKeyId,
      nonce: entity.refreshNonce,
      tag: entity.refreshTag,
      ciphertext: entity.refreshCiphertext,
    };
  }

  private _transactionCiphertext(
    entity: OidcTransactionEntity,
  ): IPlatformSecretCiphertext {
    return {
      keyId: entity.pkceKeyId,
      nonce: entity.pkceNonce,
      tag: entity.pkceTag,
      ciphertext: entity.pkceCiphertext,
    };
  }

  private _assertSessionRecord(record: IPlatformOidcSessionRecord): void {
    if (
      !HASH_PATTERN.test(record.sessionIdHash) ||
      !Number.isFinite(record.createdAt) ||
      !Number.isFinite(record.lastSeenAt) ||
      !Number.isFinite(record.absoluteExpiresAt) ||
      !Number.isFinite(record.accessExpiresAt) ||
      record.createdAt > record.lastSeenAt ||
      record.lastSeenAt > record.absoluteExpiresAt
    ) {
      throw new Error('Invalid stored session.');
    }

    this._assertIdentity(record.subjectId, record.roles, record.permissions);
    this._assertCiphertext(record.refreshToken, MAX_REFRESH_CIPHERTEXT_LENGTH);
  }

  private _assertTransactionRecord(
    record: IPlatformOidcTransactionRecord,
  ): void {
    if (
      !HASH_PATTERN.test(record.transactionIdHash) ||
      !HASH_PATTERN.test(record.stateHash) ||
      !HASH_PATTERN.test(record.nonce) ||
      !Number.isFinite(record.expiresAt) ||
      (record.replacedSessionIdHash !== undefined &&
        !HASH_PATTERN.test(record.replacedSessionIdHash))
    ) {
      throw new Error('Invalid stored transaction.');
    }

    this._assertCiphertext(record.pkceVerifier, 256);
  }

  private _assertIdentity(
    subjectId: string,
    roles: readonly string[],
    permissions: readonly string[],
  ): void {
    if (!this._isSafeText(subjectId, 255)) {
      throw new Error('Invalid stored identity.');
    }

    this._canonicalArray(roles);
    this._canonicalArray(permissions);
  }

  private _canonicalArray(values: readonly string[]): string {
    if (
      !Array.isArray(values) ||
      values.length > 100 ||
      new Set(values).size !== values.length ||
      values.some(
        (value) => typeof value !== 'string' || !this._isSafeText(value, 128),
      )
    ) {
      throw new Error('Invalid stored identity array.');
    }

    const json = JSON.stringify(values.map((value) => value.trim()));

    if (Buffer.byteLength(json, 'utf8') > 8192) {
      throw new Error('Invalid stored identity array.');
    }

    return json;
  }

  private _parseArray(value: string): readonly string[] {
    try {
      const parsed: unknown = JSON.parse(value);

      if (
        !Array.isArray(parsed) ||
        !parsed.every((entry): entry is string => typeof entry === 'string')
      ) {
        throw new Error();
      }

      const canonical = this._canonicalArray(parsed);

      if (canonical !== value) {
        throw new Error();
      }

      return Object.freeze([...parsed]);
    } catch {
      throw new Error('Invalid stored identity array.');
    }
  }

  private _assertCiphertext(
    value: IPlatformSecretCiphertext,
    maxCiphertextLength: number,
  ): void {
    if (
      !ASCII_TEXT_PATTERN.test(value.keyId) ||
      value.keyId.length > 64 ||
      !this._isCanonicalBase64Url(value.nonce) ||
      !this._isCanonicalBase64Url(value.tag) ||
      !this._isCanonicalBase64Url(value.ciphertext) ||
      value.ciphertext.length > maxCiphertextLength
    ) {
      throw new Error('Invalid stored ciphertext.');
    }
  }

  private _timestamp(value: number | string): number {
    const timestamp = typeof value === 'number' ? value : Number(value);

    if (!Number.isSafeInteger(timestamp)) {
      throw new Error('Invalid stored timestamp.');
    }

    return timestamp;
  }

  private _isSafeText(value: string, maxBytes: number): boolean {
    return (
      value.trim().length > 0 &&
      !/\p{Cc}/u.test(value) &&
      Buffer.byteLength(value, 'utf8') <= maxBytes
    );
  }

  private _isCanonicalBase64Url(value: string): boolean {
    return (
      BASE64_URL_PATTERN.test(value) &&
      Buffer.from(value, 'base64url').toString('base64url') === value
    );
  }
}
