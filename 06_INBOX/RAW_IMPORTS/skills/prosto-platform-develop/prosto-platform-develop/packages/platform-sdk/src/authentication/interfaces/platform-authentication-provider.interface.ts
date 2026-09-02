import type {
  IPlatformHttpRouteRegistration,
  IPlatformRequestIdentityResolver,
} from '@/transport/index.js';
import type {
  PLATFORM_AUTHENTICATION_PROVIDER_MODES,
  PLATFORM_AUTHENTICATION_SESSION_STATES,
} from '../constants/index.js';

/**
 * @alpha
 * Provider-neutral session state that contains no subject or credential data.
 */
export type PlatformAuthenticationSessionStateType =
  (typeof PLATFORM_AUTHENTICATION_SESSION_STATES)[number];

/**
 * @alpha
 * Provider-neutral state exposed by an authentication implementation.
 * It intentionally excludes subject, credential, and provider-specific data.
 */
export interface IPlatformAuthenticationSessionState {
  readonly state: PlatformAuthenticationSessionStateType;
}

/**
 * @alpha
 * Host-selected authentication provider mode.
 */
export type PlatformAuthenticationProviderModeType =
  (typeof PLATFORM_AUTHENTICATION_PROVIDER_MODES)[number];

/**
 * @alpha
 * Host-facing authentication provider facade.
 * The composition root uses its resolver and declared public routes without
 * coupling to a particular authentication protocol or persistence adapter.
 */
export interface IPlatformAuthenticationProvider {
  readonly mode: PlatformAuthenticationProviderModeType;
  readonly resolver: IPlatformRequestIdentityResolver;
  readonly publicRouteRegistrations: readonly IPlatformHttpRouteRegistration[];
}
