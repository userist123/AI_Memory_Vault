import type {
  IPlatformDelegatedIdentity,
  PlatformRequestIdentityType,
} from '../interfaces/index.js';

/**
 * @alpha
 * Type guard that narrows a {@link PlatformRequestIdentityType} to {@link IPlatformDelegatedIdentity}.
 */
export function isPlatformDelegatedIdentity(
  identity: PlatformRequestIdentityType,
): identity is IPlatformDelegatedIdentity {
  return identity.authenticationType === 'delegated';
}
