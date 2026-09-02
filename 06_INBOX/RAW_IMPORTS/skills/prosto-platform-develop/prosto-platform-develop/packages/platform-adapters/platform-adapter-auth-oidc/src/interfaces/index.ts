/**
 * @alpha
 * Asymmetric JWT signature algorithms accepted by the bearer adapter.
 */
export type PlatformAuthJwtAlgorithmType = 'RS256' | 'PS256' | 'ES256';

/**
 * @alpha
 * Configuration for one deployment-scoped OIDC JWT bearer issuer.
 */
export interface IPlatformOidcBearerResolverConfig {
  readonly issuer: string;
  readonly jwksUri: string;
  readonly audiences: readonly string[];
  readonly allowedAlgorithms?: readonly PlatformAuthJwtAlgorithmType[];
  readonly rolesClaim?: string;
  readonly permissionsClaim?: string;
  readonly jwksTimeoutMs?: number;
  readonly jwksCacheMaxAgeMs?: number;
  readonly jwksCooldownDurationMs?: number;
}

/**
 * @alpha
 * Resolved configuration for a single deployment-scoped OIDC JWT bearer issuer.
 */
export interface IResolvedBearerConfig {
  readonly issuer: string;
  readonly jwksUri: URL;
  readonly audiences: readonly string[];
  readonly allowedAlgorithms: readonly PlatformAuthJwtAlgorithmType[];
  readonly rolesClaim: string;
  readonly permissionsClaim: string;
  readonly jwksTimeoutMs: number;
  readonly jwksCacheMaxAgeMs: number;
  readonly jwksCooldownDurationMs: number;
}

/**
 * @alpha
 * Structured, redacted outcome emitted after bearer identity resolution.
 */
export type PlatformAuthLogOutcomeType =
  | 'anonymous'
  | 'authenticated'
  | 'rejected'
  | 'unavailable';

/**
 * @alpha
 * Immutable non-sensitive event recorded after bearer identity resolution.
 */
export interface IPlatformAuthLogEvent {
  readonly event: 'bearer_identity_resolution';
  readonly correlationId: string;
  readonly outcome: PlatformAuthLogOutcomeType;
  readonly durationMs: number;
}

/**
 * @alpha
 * Sink for redacted bearer authentication outcomes.
 */
export interface IPlatformAuthLogger {
  log(event: IPlatformAuthLogEvent): void;
}
