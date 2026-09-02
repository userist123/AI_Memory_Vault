import type {
  IPlatformHttpRouteRegistration,
  IPlatformRequestIdentityResolver,
  PlatformHttpMethodType,
} from '@prosto/platform-sdk';
import type { IPlatformHttpLogger } from './observability/index.js';

/**
 * @alpha
 * Explicit CORS policy for the HTTP transport. CORS is disabled when this
 * configuration is omitted; wildcard origins are intentionally unsupported.
 */
export interface IPlatformHttpCorsConfig {
  readonly allowedOrigins: readonly string[];
  readonly allowedMethods: readonly PlatformHttpMethodType[];
  readonly credentials?: boolean;
}

/**
 * @alpha
 * Explicit, deliberately small override surface for the default Helmet
 * policy. Fastify and Helmet option types remain implementation details.
 */
export interface IPlatformHttpHelmetConfig {
  readonly contentSecurityPolicy?: boolean;
  readonly crossOriginEmbedderPolicy?: boolean;
}

/**
 * @alpha
 * Framework-neutral configuration for the Fastify-backed HTTP transport.
 * TLS terminates at a trusted ingress or reverse proxy, not in this adapter.
 */
export interface IPlatformHttpServerConfig {
  readonly host: string;
  readonly port: number;
  readonly trustedProxyAddresses?: readonly string[];
  readonly bodyLimitBytes?: number;
  readonly cors?: IPlatformHttpCorsConfig;
  readonly helmet?: IPlatformHttpHelmetConfig;
  readonly logger?: IPlatformHttpLogger;
  readonly identityResolver?: IPlatformRequestIdentityResolver;
  readonly correlationIdHeaderName?: string;
  readonly slowRequestThresholdMs?: number;
  readonly requestTimeoutMs?: number;
  readonly gracefulShutdownTimeoutMs?: number;
}

/**
 * @alpha
 * States of a {@link PlatformHttpServer} lifecycle.
 */
export type PlatformHttpServerStateType =
  | 'created'
  | 'routesRegistered'
  | 'starting'
  | 'started'
  | 'stopping'
  | 'stopped'
  | 'failed';

/**
 * @alpha
 * A scope for a request, used to track the lifecycle of a request.
 */
export interface IActiveRequestScope {
  readonly abortController: AbortController;
  correlationId: string;
  readonly startedAt: number;
  readonly activeStreams: Set<IActiveResponseStream>;
}

export interface IActiveResponseStream {
  readonly stream: ReadableStream<Uint8Array>;
  cancel(): void;
}

export interface IRouteRegistrationCandidate {
  readonly registration: IPlatformHttpRouteRegistration;
  readonly shapeKey: string;
}

export interface IPlatformHttpErrorResponse {
  readonly statusCode: number;
  readonly code: string;
  readonly message: string;
}
