import type { ServiceTokenType } from '../interfaces/index.js';
import { normalizeTokenName } from '@/utils/index.js';
import { SERVICE_TOKEN_NAME_PREFIX } from '../constants/index.js';

/**
 * @alpha
 * Returns the canonical key used to create a service token.
 */
export function getServiceTokenKey(name: string): string {
  return `${SERVICE_TOKEN_NAME_PREFIX}${normalizeTokenName(name)}`;
}

/**
 * @alpha
 * Creates a globally stable, typed service token.
 */
export function createServiceToken<TService>(
  name: string,
): ServiceTokenType<TService> {
  return Symbol.for(getServiceTokenKey(name)) as ServiceTokenType<TService>;
}
