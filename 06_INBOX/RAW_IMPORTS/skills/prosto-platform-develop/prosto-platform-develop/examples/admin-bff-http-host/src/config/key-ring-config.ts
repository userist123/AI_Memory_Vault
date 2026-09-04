import { AdminBffHostConfigurationError } from './auth-config.js';

/** @internal */
export interface IAdminBffKeyRingConfig {
  readonly activeKeyId: string;
  readonly keys: readonly IAdminBffKeyRingEntry[];
}

/** @internal */
export interface IAdminBffKeyRingEntry {
  readonly id: string;
  readonly key: string;
}

/** @internal */
export function parseKeyRingConfig(
  environment: NodeJS.ProcessEnv,
): IAdminBffKeyRingConfig {
  try {
    const raw = environment.ADMIN_BFF_SESSION_KEY_RING_JSON;

    if (typeof raw !== 'string' || !raw.length) {
      throw new Error('Missing key ring.');
    }

    const parsed: unknown = JSON.parse(raw);

    if (!isKeyRingConfig(parsed)) {
      throw new Error('Invalid key ring.');
    }

    return Object.freeze({
      activeKeyId: parsed.activeKeyId,
      keys: Object.freeze(
        parsed.keys.map((entry) => Object.freeze({ ...entry })),
      ),
    });
  } catch {
    throw new AdminBffHostConfigurationError();
  }
}

function isKeyRingConfig(value: unknown): value is {
  readonly activeKeyId: string;
  readonly keys: readonly { readonly id: string; readonly key: string }[];
} {
  return (
    typeof value === 'object' &&
    value !== null &&
    !Array.isArray(value) &&
    Object.keys(value).length === 2 &&
    typeof (value as { activeKeyId?: unknown }).activeKeyId === 'string' &&
    Array.isArray((value as { keys?: unknown }).keys) &&
    (value as { keys: unknown[] }).keys.every(
      (entry) =>
        typeof entry === 'object' &&
        entry !== null &&
        !Array.isArray(entry) &&
        Object.keys(entry).length === 2 &&
        typeof (entry as { id?: unknown }).id === 'string' &&
        typeof (entry as { key?: unknown }).key === 'string',
    )
  );
}
