import type { EventTokenType } from '../interfaces/index.js';
import { normalizeTokenName } from '@/utils/index.js';
import { EVENT_TOKEN_NAME_PREFIX } from '../constants/index.js';

/**
 * @alpha
 * Returns the canonical key used to create an event token.
 */
export function getEventTokenKey(name: string): string {
  return `${EVENT_TOKEN_NAME_PREFIX}${normalizeTokenName(name)}`;
}

/**
 * @alpha
 * Creates a globally stable, typed event token.
 */
export function createEventToken<TPayload>(
  name: string,
): EventTokenType<TPayload> {
  return Symbol.for(getEventTokenKey(name)) as EventTokenType<TPayload>;
}
