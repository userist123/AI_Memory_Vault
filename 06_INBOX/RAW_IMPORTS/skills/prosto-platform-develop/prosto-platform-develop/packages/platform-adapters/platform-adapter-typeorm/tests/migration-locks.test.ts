import type { QueryRunner } from 'typeorm';
import { describe, expect, it, vi } from 'vitest';
import { PersistenceError } from '@prosto/platform-sdk';
import {
  MySqlMigrationLock,
  PostgresMigrationLock,
  SqlServerMigrationLock,
} from '@/migration-locks/index.js';

function createQueryRunner(queryResult: unknown): QueryRunner {
  return {
    isReleased: false,
    query: vi.fn().mockResolvedValue(queryResult),
    release: vi.fn(),
  } as unknown as QueryRunner;
}

describe('migration lock strategies', () => {
  it('maps a rejected MySQL named lock to a timeout and releases its session', async () => {
    // Arrange
    const runner = createQueryRunner([{ acquired: 0 }]);
    const lock = new MySqlMigrationLock(runner, 'a'.repeat(64));

    // Act and assert
    await expect(lock.acquire(1)).rejects.toBeInstanceOf(PersistenceError);
    await lock.release();

    expect(runner.release).toHaveBeenCalledOnce();
    expect(runner.query).toHaveBeenCalledTimes(1);
  });

  it('acquires a MySQL named lock when GET_LOCK returns a string "1" (MySQL 8.x + supportBigNumbers)', async () => {
    // Arrange — MySQL 8.x reports GET_LOCK as BIGINT; TypeORM enables
    // supportBigNumbers + bigNumberStrings on mysql2 by default, causing
    // the driver to return "1" (string) instead of 1 (number).
    const runner = createQueryRunner([{ acquired: '1' }]);
    const lock = new MySqlMigrationLock(runner, 'a'.repeat(64));

    // Act
    await lock.acquire(5_000);
    await lock.release();

    // Assert
    expect(runner.query).toHaveBeenCalledWith(
      'SELECT GET_LOCK(?, ?) AS acquired',
      expect.any(Array),
    );
  });

  it('maps a string-returned MySQL timeout ("0") to a PersistenceError', async () => {
    // Arrange
    const runner = createQueryRunner([{ acquired: '0' }]);
    const lock = new MySqlMigrationLock(runner, 'a'.repeat(64));

    // Act and assert
    await expect(lock.acquire(1)).rejects.toMatchObject({
      code: 'PersistenceMigrationLockTimeout',
    });
    await lock.release();

    expect(runner.release).toHaveBeenCalledOnce();
  });

  it('releases PostgreSQL advisory locks through the acquiring session', async () => {
    // Arrange
    const runner = createQueryRunner([{ acquired: true }]);
    const lock = new PostgresMigrationLock(runner, 'b'.repeat(64));

    // Act
    await lock.acquire(100);
    await lock.release();

    // Assert
    expect(runner.query).toHaveBeenNthCalledWith(
      1,
      'SELECT pg_try_advisory_lock($1) AS acquired',
      expect.any(Array),
    );
    expect(runner.query).toHaveBeenNthCalledWith(
      2,
      'SELECT pg_advisory_unlock($1)',
      expect.any(Array),
    );
    expect(runner.release).toHaveBeenCalledOnce();
  });

  it('maps a rejected SQL Server application lock to a timeout and releases its session', async () => {
    // Arrange
    const runner = createQueryRunner([{ result: -1 }]);
    const lock = new SqlServerMigrationLock(runner, 'c'.repeat(64));

    // Act and assert
    await expect(lock.acquire(1)).rejects.toMatchObject({
      code: 'PersistenceMigrationLockTimeout',
    });
    await lock.release();

    expect(runner.release).toHaveBeenCalledOnce();
  });
});
