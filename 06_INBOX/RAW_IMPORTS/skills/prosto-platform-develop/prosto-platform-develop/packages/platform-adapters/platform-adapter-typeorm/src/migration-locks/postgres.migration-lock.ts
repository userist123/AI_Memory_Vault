import type { QueryRunner } from 'typeorm';
import { sleep } from '@/utils/index.js';
import { QueryRunnerBaseMigrationLock } from './base.migration-lock.js';

export class PostgresMigrationLock extends QueryRunnerBaseMigrationLock {
  private readonly POLL_INTERVAL_MS = 50;

  private readonly _advisoryKey: string;

  constructor(runner: QueryRunner, lockKey: string) {
    super(runner);

    this._advisoryKey = BigInt.asIntN(
      64,
      BigInt(`0x${lockKey.slice(0, 16)}`),
    ).toString();
  }

  override async acquire(timeoutMs: number): Promise<void> {
    const deadline = Date.now() + timeoutMs;

    while (Date.now() <= deadline) {
      const rows: readonly { acquired: boolean }[] = await this._runner.query(
        'SELECT pg_try_advisory_lock($1) AS acquired',
        [this._advisoryKey],
      );

      if (rows[0]?.acquired === true) {
        this._acquired = true;
        return;
      }

      await sleep(
        Math.min(this.POLL_INTERVAL_MS, Math.max(1, deadline - Date.now())),
      );
    }

    throw this._timeout();
  }

  protected override async _releaseLock(): Promise<void> {
    await this._runner.query('SELECT pg_advisory_unlock($1)', [
      this._advisoryKey,
    ]);
  }
}
