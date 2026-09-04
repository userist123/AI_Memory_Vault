import type { QueryRunner } from 'typeorm';
import { MIGRATION_LOCK_NAMESPACE } from '@/constants/index.js';
import { QueryRunnerBaseMigrationLock } from './base.migration-lock.js';

export class MySqlMigrationLock extends QueryRunnerBaseMigrationLock {
  private readonly _name: string;

  constructor(runner: QueryRunner, lockKey: string) {
    super(runner);

    // MySQL named locks are limited to 64 characters.
    this._name = `${MIGRATION_LOCK_NAMESPACE}${lockKey}`.slice(0, 64);
  }

  override async acquire(timeoutMs: number): Promise<void> {
    const rows: readonly { acquired: number | string | null }[] =
      await this._runner.query('SELECT GET_LOCK(?, ?) AS acquired', [
        this._name,
        Math.ceil(timeoutMs / 1000),
      ]);

    // MySQL 8.x reports GET_LOCK as a BIGINT column. TypeORM enables
    // supportBigNumbers + bigNumberStrings on mysql2 by default, which
    // makes the driver return BIGINT values as strings (e.g. "1" instead
    // of 1). Normalize via Number() so the strict comparison works
    // regardless of whether the driver returns a number or a string.
    if (Number(rows[0]?.acquired) !== 1) {
      throw this._timeout();
    }

    this._acquired = true;
  }

  protected override async _releaseLock(): Promise<void> {
    await this._runner.query('SELECT RELEASE_LOCK(?)', [this._name]);
  }
}
