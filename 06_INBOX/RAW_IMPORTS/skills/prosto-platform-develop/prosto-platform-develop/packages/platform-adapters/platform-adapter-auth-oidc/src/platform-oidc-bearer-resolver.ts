import type {
  IPlatformAuthLogEvent,
  IPlatformAuthLogger,
  IPlatformOidcBearerResolverConfig,
  IResolvedBearerConfig,
  PlatformAuthLogOutcomeType,
} from './interfaces/index.js';
import {
  type IPlatformIdentityResolutionRequest,
  type IPlatformRequestIdentityResolver,
  PlatformAnonymousIdentity,
  type PlatformRequestIdentityType,
} from '@prosto/platform-sdk';
import { createRemoteJWKSet, customFetch, jwtVerify } from 'jose';
import {
  extractBearerToken,
  fetchJwksWithoutRedirects,
  mapDelegatedIdentity,
  resolveConfig,
  toResolutionError,
} from '@/utils/index.js';

/**
 * @alpha
 * OIDC bearer resolver that verifies asymmetric JWTs against one configured remote JWKS.
 */
export class PlatformOidcBearerResolver implements IPlatformRequestIdentityResolver {
  private readonly _config: IResolvedBearerConfig;
  private readonly _jwks: unknown;
  private readonly _logger: IPlatformAuthLogger | undefined;

  constructor(
    config: IPlatformOidcBearerResolverConfig,
    logger?: IPlatformAuthLogger,
  ) {
    this._config = resolveConfig(config);
    this._logger = logger;
    this._jwks = createRemoteJWKSet(this._config.jwksUri, {
      timeoutDuration: this._config.jwksTimeoutMs,
      cacheMaxAge: this._config.jwksCacheMaxAgeMs,
      cooldownDuration: this._config.jwksCooldownDurationMs,
      [customFetch]: fetchJwksWithoutRedirects,
    });
  }

  async resolve(
    request: IPlatformIdentityResolutionRequest,
  ): Promise<PlatformRequestIdentityType> {
    const startedAt = Date.now();

    try {
      const token = extractBearerToken(request.headers.authorization);

      if (token === undefined) {
        const identity = new PlatformAnonymousIdentity();

        this._log(request.correlationId, 'anonymous', startedAt);

        return identity;
      }

      const verification = await jwtVerify(token, this._jwks as never, {
        issuer: this._config.issuer,
        audience: [...this._config.audiences],
        algorithms: [...this._config.allowedAlgorithms],
        clockTolerance: 0,
      });
      const identity = mapDelegatedIdentity(verification.payload, this._config);

      this._log(request.correlationId, 'authenticated', startedAt);

      return identity;
    } catch (error) {
      const httpError = toResolutionError(error);
      this._log(
        request.correlationId,
        httpError.code === 'HTTP_UNAUTHENTICATED' ? 'rejected' : 'unavailable',
        startedAt,
      );
      throw httpError;
    }
  }

  private _log(
    correlationId: string,
    outcome: PlatformAuthLogOutcomeType,
    startedAt: number,
  ): void {
    if (this._logger === undefined) {
      return;
    }

    const event: IPlatformAuthLogEvent = Object.freeze({
      event: 'bearer_identity_resolution',
      correlationId,
      outcome,
      durationMs: Math.max(0, Date.now() - startedAt),
    });

    try {
      this._logger.log(event);
    } catch {
      // Authentication must not depend on an observability sink.
    }
  }
}
