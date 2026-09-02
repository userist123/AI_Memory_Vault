import type { QueryRunner } from 'typeorm';
import { MIGRATION_LOCK_NAMESPACE } from '@/constants/index.js';
import { QueryRunnerBaseMigrationLock } from './base.migration-lock.js';

export class SqlServerMigrationLock extends QueryRunnerBaseMigrationLock {
  private readonly _resource: string;

  constructor(runner: QueryRunner, lockKey: string) {
    super(runner);

    this._resource = `${MIGRATION_LOCK_NAMESPACE}${lockKey}`;
  }

  async acquire(timeoutMs: number): Promise<void> {
    const rows: readonly { result: number }[] = await this._runner.query(
      "DECLARE @result int; EXEC @result = sp_getapplock @Resource = @0, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = @1; SELECT @result AS result;",
      [this._resource, timeoutMs],
    );

    if ((rows[0]?.result ?? -1) < 0) {
      throw this._timeout();
    }

    this._acquired = true;
  }

  protected async _releaseLock(): Promise<void> {
    await this._runner.query(
      "EXEC sp_releaseapplock @Resource = @0, @LockOwner = 'Session';",
      [this._resource],
    );
  }
}
