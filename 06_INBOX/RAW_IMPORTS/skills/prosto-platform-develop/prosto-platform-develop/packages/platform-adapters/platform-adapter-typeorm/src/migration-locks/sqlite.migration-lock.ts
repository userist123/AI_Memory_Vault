import type { QueryRunner } from 'typeorm';
import { QueryRunnerBaseMigrationLock } from './base.migration-lock.js';

export class SqliteMigrationLock extends QueryRunnerBaseMigrationLock {
  constructor(
    runner: QueryRunner,
    private readonly _isInMemory: boolean,
  ) {
    super(runner);
  }

  override async acquire(timeoutMs: number): Promise<void> {
    if (this._isInMemory) {
      // In-memory SQLite databases are process-local and cannot contend.
      this._acquired = true;
      return;
    }

    try {
      await this._runner.query(`PRAGMA busy_timeout = ${timeoutMs}`);
      await this._runner.query('BEGIN EXCLUSIVE');

      this._acquired = true;
    } catch {
      throw this._timeout();
    }
  }

  protected override async _releaseLock(): Promise<void> {
    if (!this._isInMemory) {
      await this._runner.query('COMMIT');
    }
  }
}
