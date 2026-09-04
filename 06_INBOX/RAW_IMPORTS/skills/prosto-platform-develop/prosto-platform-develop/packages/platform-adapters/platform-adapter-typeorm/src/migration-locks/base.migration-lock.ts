import type { QueryRunner } from 'typeorm';
import type { IMigrationLock } from '@/interfaces/index.js';
import { PersistenceError } from '@prosto/platform-sdk';

export abstract class QueryRunnerBaseMigrationLock implements IMigrationLock {
  protected _acquired = false;

  constructor(protected readonly _runner: QueryRunner) {}

  abstract acquire(timeoutMs: number): Promise<void>;

  async release(): Promise<void> {
    try {
      if (this._acquired) {
        await this._releaseLock();

        this._acquired = false;
      }
    } finally {
      if (!this._runner.isReleased) {
        await this._runner.release();
      }
    }
  }

  protected abstract _releaseLock(): Promise<void>;

  protected _timeout(): PersistenceError {
    return new PersistenceError(
      'PersistenceMigrationLockTimeout',
      'Timed out while waiting for the database migration lock.',
      {
        phase: 'migration-lock',
        remediationHint:
          'Wait for the active platform startup to finish, then retry.',
      },
    );
  }
}
