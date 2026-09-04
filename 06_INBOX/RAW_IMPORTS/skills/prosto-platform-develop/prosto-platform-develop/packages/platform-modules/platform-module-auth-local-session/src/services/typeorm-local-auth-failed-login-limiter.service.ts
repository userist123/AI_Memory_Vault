import type { IPlatformLocalAuthFailedLoginLimiter } from '@prosto/platform-adapter-auth-local';
import type { DataSource } from 'typeorm';
import {
  LOCAL_AUTH_FAILED_LOGIN_BLOCK_MS,
  LOCAL_AUTH_FAILED_LOGIN_MAXIMUM,
  LOCAL_AUTH_FAILED_LOGIN_WINDOW_MS,
} from '@/constants/index.js';
import { LocalAuthFailedLoginEntity } from '@/entities/index.js';

interface IProcessFailureState {
  readonly failureCount: number;
  readonly windowStartedAt: number;
  readonly blockedUntil?: number;
}

/** @internal */
export class TypeOrmLocalAuthFailedLoginLimiter implements IPlatformLocalAuthFailedLoginLimiter {
  private static readonly _maximumProcessEntries = 10_000;
  private static readonly _processFailures = new Map<
    string,
    IProcessFailureState
  >();

  constructor(private readonly _dataSource: DataSource) {}

  async isBlocked(normalizedUsername: string, now: number): Promise<boolean> {
    const processState =
      TypeOrmLocalAuthFailedLoginLimiter._processFailures.get(
        normalizedUsername,
      );

    if (
      processState?.blockedUntil !== undefined &&
      processState.blockedUntil > now
    ) {
      return true;
    }

    if (
      processState !== undefined &&
      processState.windowStartedAt <= now - LOCAL_AUTH_FAILED_LOGIN_WINDOW_MS
    ) {
      TypeOrmLocalAuthFailedLoginLimiter._processFailures.delete(
        normalizedUsername,
      );
    }

    const entity = await this._dataSource
      .getRepository(LocalAuthFailedLoginEntity)
      .findOneBy({ username: normalizedUsername });

    return (
      entity?.blockedUntil !== null &&
      entity?.blockedUntil !== undefined &&
      entity.blockedUntil > now
    );
  }

  async recordFailure(normalizedUsername: string, now: number): Promise<void> {
    const processState = this._recordProcessFailure(normalizedUsername, now);

    await this._dataSource.transaction(async (manager): Promise<void> => {
      const repository = manager.getRepository(LocalAuthFailedLoginEntity);
      const existing = await repository.findOneBy({
        username: normalizedUsername,
      });
      const withinWindow =
        existing !== null &&
        existing !== undefined &&
        existing.windowStartedAt > now - LOCAL_AUTH_FAILED_LOGIN_WINDOW_MS;
      const failureCount = withinWindow ? existing.failureCount + 1 : 1;
      const blockedUntil =
        failureCount >= LOCAL_AUTH_FAILED_LOGIN_MAXIMUM
          ? now + LOCAL_AUTH_FAILED_LOGIN_BLOCK_MS
          : null;

      await repository.save({
        username: normalizedUsername,
        failureCount,
        windowStartedAt:
          withinWindow && existing ? existing.windowStartedAt : now,
        blockedUntil,
      });
    });

    if (processState.blockedUntil !== undefined) {
      TypeOrmLocalAuthFailedLoginLimiter._processFailures.set(
        normalizedUsername,
        processState,
      );
    }
  }

  async clearFailures(normalizedUsername: string): Promise<void> {
    TypeOrmLocalAuthFailedLoginLimiter._processFailures.delete(
      normalizedUsername,
    );

    await this._dataSource
      .getRepository(LocalAuthFailedLoginEntity)
      .delete({ username: normalizedUsername });
  }

  private _recordProcessFailure(
    username: string,
    now: number,
  ): IProcessFailureState {
    const existing =
      TypeOrmLocalAuthFailedLoginLimiter._processFailures.get(username);
    const withinWindow =
      existing !== undefined &&
      existing.windowStartedAt > now - LOCAL_AUTH_FAILED_LOGIN_WINDOW_MS;
    const failureCount = withinWindow ? existing.failureCount + 1 : 1;
    const state: IProcessFailureState = {
      failureCount,
      windowStartedAt:
        withinWindow && existing ? existing.windowStartedAt : now,
      blockedUntil:
        failureCount >= LOCAL_AUTH_FAILED_LOGIN_MAXIMUM
          ? now + LOCAL_AUTH_FAILED_LOGIN_BLOCK_MS
          : undefined,
    };

    TypeOrmLocalAuthFailedLoginLimiter._processFailures.set(username, state);
    this._trimProcessFailures(now);

    return state;
  }

  private _trimProcessFailures(now: number): void {
    for (const [
      username,
      state,
    ] of TypeOrmLocalAuthFailedLoginLimiter._processFailures) {
      if (
        state.windowStartedAt <= now - LOCAL_AUTH_FAILED_LOGIN_WINDOW_MS &&
        (state.blockedUntil === undefined || state.blockedUntil <= now)
      ) {
        TypeOrmLocalAuthFailedLoginLimiter._processFailures.delete(username);
      }
    }

    while (
      TypeOrmLocalAuthFailedLoginLimiter._processFailures.size >
      TypeOrmLocalAuthFailedLoginLimiter._maximumProcessEntries
    ) {
      const oldest = TypeOrmLocalAuthFailedLoginLimiter._processFailures
        .keys()
        .next().value;

      if (oldest === undefined) {
        return;
      }

      TypeOrmLocalAuthFailedLoginLimiter._processFailures.delete(oldest);
    }
  }
}
