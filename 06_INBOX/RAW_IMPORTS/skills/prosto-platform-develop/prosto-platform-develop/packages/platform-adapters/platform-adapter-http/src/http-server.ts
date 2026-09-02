import { randomUUID } from 'node:crypto';
import { isIP } from 'node:net';
import fastifyCors, { type FastifyCorsOptions } from '@fastify/cors';
import fastifyHelmet from '@fastify/helmet';
import Fastify, {
  type FastifyInstance,
  type FastifyReply,
  type FastifyRequest,
} from 'fastify';
import {
  ALLOWED_APPLICATION_HTTP_METHODS,
  type IPlatformHttpRequest,
  type IPlatformHttpRouteContextFactoryInput,
  type IPlatformHttpRouteRegistration,
  PlatformAnonymousIdentity,
  PlatformDelegatedIdentity,
  PlatformHttpError,
  type PlatformHttpMethodType,
  type PlatformRequestIdentityType,
} from '@prosto/platform-sdk';
import type {
  IActiveRequestScope,
  IActiveResponseStream,
  IPlatformHttpCorsConfig,
  IPlatformHttpErrorResponse,
  IPlatformHttpServerConfig,
  IRouteRegistrationCandidate,
  PlatformHttpServerStateType,
} from './http-server.interfaces.js';
import {
  PlatformHttpBodyParseError,
  PlatformHttpServerLifecycleError,
} from './http-server.errors.js';
import {
  FastifyBodyMapper,
  FastifyRequestMapper,
  FastifyResponseMapper,
} from './mapping/index.js';
import {
  ConsoleHttpLogger,
  type IPlatformHttpLogger,
} from './observability/index.js';

export const DEFAULT_BODY_LIMIT_BYTES = 1_048_576;
export const DEFAULT_CORRELATION_ID_HEADER_NAME = 'X-Correlation-Id';
export const DEFAULT_SLOW_REQUEST_THRESHOLD_MS = 500;
export const DEFAULT_REQUEST_TIMEOUT_MS = 30_000;
export const DEFAULT_GRACEFUL_SHUTDOWN_TIMEOUT_MS = 30_000;

/**
 * @alpha
 * HTTP server lifecycle facade. Its public API intentionally exposes neither
 * Fastify instances nor Fastify handlers.
 */
export class PlatformHttpServer {
  private readonly _config: Required<
    Pick<
      IPlatformHttpServerConfig,
      | 'bodyLimitBytes'
      | 'correlationIdHeaderName'
      | 'slowRequestThresholdMs'
      | 'requestTimeoutMs'
      | 'gracefulShutdownTimeoutMs'
    >
  > &
    IPlatformHttpServerConfig;
  private readonly _logger: IPlatformHttpLogger;
  private readonly _registrations: IPlatformHttpRouteRegistration[] = [];
  private readonly _routeShapes = new Set<string>();
  private readonly _requestScopes = new Map<
    FastifyRequest,
    IActiveRequestScope
  >();
  private readonly _requestMapper = new FastifyRequestMapper();
  private readonly _responseMapper = new FastifyResponseMapper();
  private readonly _anonymousIdentity = new PlatformAnonymousIdentity();

  private _fastify: FastifyInstance;
  private _stopPromise: Promise<void> | undefined;

  constructor(config: IPlatformHttpServerConfig) {
    this._validateConfig(config);
    this._config = {
      ...config,
      bodyLimitBytes: config.bodyLimitBytes ?? DEFAULT_BODY_LIMIT_BYTES,
      correlationIdHeaderName:
        config.correlationIdHeaderName ?? DEFAULT_CORRELATION_ID_HEADER_NAME,
      slowRequestThresholdMs:
        config.slowRequestThresholdMs ?? DEFAULT_SLOW_REQUEST_THRESHOLD_MS,
      requestTimeoutMs: config.requestTimeoutMs ?? DEFAULT_REQUEST_TIMEOUT_MS,
      gracefulShutdownTimeoutMs:
        config.gracefulShutdownTimeoutMs ??
        DEFAULT_GRACEFUL_SHUTDOWN_TIMEOUT_MS,
    };
    this._logger = config.logger ?? new ConsoleHttpLogger();
    this._fastify = this._createFastify();
  }

  private _state: PlatformHttpServerStateType = 'created';

  public get state(): PlatformHttpServerStateType {
    return this._state;
  }

  public registerRoutes(
    registrations: readonly IPlatformHttpRouteRegistration[],
  ): void {
    if (this._state !== 'created' && this._state !== 'routesRegistered') {
      throw this._invalidTransition('register routes');
    }

    const candidates = this._validateRegistrationBatch(registrations);

    try {
      for (const candidate of candidates) {
        this._fastify.route({
          method: candidate.registration.method,
          url: candidate.registration.route,
          handler: async (request, reply): Promise<void> => {
            await this._dispatchRoute(candidate.registration, request, reply);
          },
        });
      }
    } catch (error) {
      this._failRouteRegistration(error);
    }

    for (const candidate of candidates) {
      this._registrations.push(candidate.registration);
      this._routeShapes.add(candidate.shapeKey);
    }

    this._state = 'routesRegistered';
  }

  public async start(): Promise<void> {
    if (this._state !== 'routesRegistered') {
      throw this._invalidTransition('start');
    }

    this._state = 'starting';

    try {
      await this._fastify.ready();
      await this._fastify.listen({
        host: this._config.host,
        port: this._config.port,
      });

      this._state = 'started';
      this._logger.info('HTTP server started.', {
        host: this._config.host,
        port: this._config.port,
      });
    } catch (error) {
      this._state = 'routesRegistered';
      throw error;
    }
  }

  public stop(): Promise<void> {
    if (this._stopPromise !== undefined) {
      return this._stopPromise;
    }

    if (this._state === 'stopped') {
      return Promise.resolve();
    }

    if (this._state !== 'started') {
      throw this._invalidTransition('stop');
    }

    this._state = 'stopping';
    this._stopPromise = this._stopGracefully();

    return this._stopPromise;
  }

  private _createFastify(): FastifyInstance {
    const fastify = Fastify({
      logger: false,
      bodyLimit: this._config.bodyLimitBytes,
      exposeHeadRoutes: false,
      trustProxy:
        this._config.trustedProxyAddresses !== undefined &&
        this._config.trustedProxyAddresses.length > 0
          ? [...this._config.trustedProxyAddresses]
          : false,
    });

    this._configureBodyParsers(fastify);
    this._configureRequestObservability(fastify);
    this._configureTransportErrors(fastify);

    fastify.register(fastifyHelmet, {
      contentSecurityPolicy: this._config.helmet?.contentSecurityPolicy,
      crossOriginEmbedderPolicy: this._config.helmet?.crossOriginEmbedderPolicy,
    });

    if (this._config.cors) {
      fastify.register(fastifyCors, this._createCorsOptions(this._config.cors));
    }

    return fastify;
  }

  private _validateRegistrationBatch(
    registrations: readonly IPlatformHttpRouteRegistration[],
  ): IRouteRegistrationCandidate[] {
    if (!registrations.length) {
      throw new PlatformHttpServerLifecycleError(
        'INVALID_SERVER_CONFIGURATION',
        'Route registration batch must not be empty.',
        this._state,
      );
    }

    const batchShapes = new Set<string>();
    const candidates: IRouteRegistrationCandidate[] = [];

    for (const registration of registrations) {
      const shapeKey = this._validateRouteRegistration(registration);

      if (batchShapes.has(shapeKey) || this._routeShapes.has(shapeKey)) {
        throw new PlatformHttpError(
          'DUPLICATE_ROUTE',
          'A route with the same method and normalized shape is already registered.',
        );
      }

      batchShapes.add(shapeKey);
      candidates.push({ registration, shapeKey });
    }

    return candidates;
  }

  private _validateRouteRegistration(
    registration: IPlatformHttpRouteRegistration,
  ): string {
    const method: unknown = registration.method;
    const route: unknown = registration.route;

    if (
      typeof method !== 'string' ||
      !ALLOWED_APPLICATION_HTTP_METHODS.includes(
        method as PlatformHttpMethodType,
      )
    ) {
      throw new PlatformHttpError(
        'INVALID_HTTP_METHOD',
        'Route registration uses an unsupported HTTP method.',
      );
    }

    if (typeof route !== 'string') {
      throw new PlatformHttpError(
        'INVALID_ROUTE_GRAMMAR',
        'Route registration must use an absolute path string.',
      );
    }

    if (route === '/' || !route.startsWith('/') || route.endsWith('/')) {
      throw new PlatformHttpError(
        'INVALID_ROUTE_GRAMMAR',
        'Route registration must use a non-root absolute path without a trailing slash.',
      );
    }

    const segments = route.slice(1).split('/');
    const normalizedSegments: string[] = [];

    for (const segment of segments) {
      if (!segment.length) {
        throw new PlatformHttpError(
          'INVALID_ROUTE_GRAMMAR',
          'Route registration must not contain empty path segments.',
        );
      }

      if (segment.startsWith(':')) {
        if (!/^:[a-zA-Z_][a-zA-Z0-9_]*$/u.test(segment)) {
          throw new PlatformHttpError(
            'INVALID_ROUTE_GRAMMAR',
            'Route parameters must use ASCII identifier names.',
          );
        }

        normalizedSegments.push(':');
        continue;
      }

      if (!/^[a-zA-Z0-9\-_.~]+$/u.test(segment)) {
        throw new PlatformHttpError(
          'INVALID_ROUTE_GRAMMAR',
          'Route registration contains a forbidden path segment.',
        );
      }

      normalizedSegments.push(segment);
    }

    return `${method} /${normalizedSegments.join('/')}`;
  }

  private _failRouteRegistration(error: unknown): never {
    this._state = 'failed';

    void this._fastify.close().catch((closeError) => {
      this._logger.error(
        'HTTP server failed to discard an invalid route registry.',
        {
          errorCode: 'ROUTE_REGISTRATION_FAILED',
          errorName:
            closeError instanceof Error ? closeError.name : 'UnknownError',
        },
      );
    });

    this._logger.error('HTTP route registration failed unexpectedly.', {
      errorCode: 'ROUTE_REGISTRATION_FAILED',
      errorName: error instanceof Error ? error.name : 'UnknownError',
    });

    throw new PlatformHttpServerLifecycleError(
      'ROUTE_REGISTRATION_FAILED',
      'Fastify rejected a pre-validated route registration. Create a new server instance.',
      this._state,
    );
  }

  private _configureBodyParsers(fastify: FastifyInstance): void {
    fastify.removeAllContentTypeParsers();

    fastify.addContentTypeParser(
      'application/json',
      { parseAs: 'buffer' },
      (request, payload, done): void => {
        try {
          this._assertSupportedContentType(
            request.headers['content-type'],
            /^application\/json(?:;\s*charset=utf-8)?$/iu,
          );
          done(null, FastifyBodyMapper.json(this._asBuffer(payload)));
        } catch (error) {
          done(error as Error);
        }
      },
    );

    fastify.addContentTypeParser(
      'text/plain',
      { parseAs: 'buffer' },
      (request, payload, done): void => {
        try {
          this._assertSupportedContentType(
            request.headers['content-type'],
            /^text\/plain(?:;\s*charset=utf-8)?$/iu,
          );
          done(null, FastifyBodyMapper.text(this._asBuffer(payload)));
        } catch (error) {
          done(error as Error);
        }
      },
    );

    fastify.addContentTypeParser(
      'application/octet-stream',
      { parseAs: 'buffer' },
      (request, payload, done): void => {
        try {
          this._assertSupportedContentType(
            request.headers['content-type'],
            /^application\/octet-stream$/iu,
          );
          done(null, FastifyBodyMapper.binary(this._asBuffer(payload)));
        } catch (error) {
          done(error as Error);
        }
      },
    );
  }

  private _assertSupportedContentType(
    value: string | undefined,
    pattern: RegExp,
  ): void {
    if (value === undefined || !pattern.test(value)) {
      throw new PlatformHttpError(
        'UNSUPPORTED_MEDIA_TYPE',
        'The request media type is not supported.',
      );
    }
  }

  private _configureRequestObservability(fastify: FastifyInstance): void {
    fastify.addHook('onRequest', (request, reply, done): void => {
      const scope: IActiveRequestScope = {
        abortController: new AbortController(),
        correlationId: this._readCorrelationId(request),
        startedAt: performance.now(),
        activeStreams: new Set<IActiveResponseStream>(),
      };

      this._requestScopes.set(request, scope);

      request.raw.once('aborted', (): void => {
        void this._abortRequestScope(scope, 'client-aborted');
        this._requestScopes.delete(request);
      });

      reply.raw.once('close', (): void => {
        if (!reply.raw.writableEnded) {
          void this._abortRequestScope(scope, 'client-closed');
          this._requestScopes.delete(request);
        }
      });

      done();
    });

    fastify.addHook('onRequestAbort', (request, done): void => {
      const scope = this._requestScopes.get(request);

      if (scope) {
        void this._abortRequestScope(scope, 'client-aborted');
        this._requestScopes.delete(request);
      }

      done();
    });

    fastify.addHook('onResponse', (request, reply, done): void => {
      const scope = this._requestScopes.get(request);

      if (scope) {
        const durationMs = performance.now() - scope.startedAt;
        const context = {
          correlationId: scope.correlationId,
          durationMs: Math.round(durationMs),
          method: request.method,
          route: request.routeOptions.url ?? 'unmatched',
          statusCode: reply.statusCode,
        };
        const log =
          durationMs >= this._config.slowRequestThresholdMs
            ? this._logger.warn.bind(this._logger)
            : this._logger.info.bind(this._logger);

        log('HTTP request completed.', context);
        this._requestScopes.delete(request);
      }

      done();
    });
  }

  private _configureTransportErrors(fastify: FastifyInstance): void {
    fastify.setErrorHandler((error: unknown, request, reply): void => {
      const correlationId = this._ensureCorrelationId(request);

      if (reply.sent || reply.raw.headersSent) {
        this._logger.error('HTTP response failed after it was committed.', {
          correlationId,
          errorCode: 'STREAM_TRANSFER_FAILURE',
          errorName: error instanceof Error ? error.name : 'UnknownError',
        });
        void this._terminateCommittedResponse(request, reply);
        return;
      }

      const mappedError = this._mapError(error);

      this._logger.warn('HTTP request failed before response commitment.', {
        correlationId,
        errorCode: mappedError.code,
        method: request.method,
        statusCode: mappedError.statusCode,
      });

      this._sendErrorEnvelope(reply, correlationId, mappedError);
    });

    fastify.setNotFoundHandler((request, reply): void => {
      const correlationId = this._ensureCorrelationId(request);

      this._sendErrorEnvelope(reply, correlationId, {
        statusCode: 404,
        code: 'ROUTE_NOT_FOUND',
        message: 'The requested route was not found.',
      });
    });
  }

  private async _dispatchRoute(
    registration: IPlatformHttpRouteRegistration,
    request: FastifyRequest,
    reply: FastifyReply,
  ): Promise<void> {
    const scope = this._getRequestScope(request);
    const response = await this._withRequestTimeout(scope, async () => {
      const provisionalRequest = this._requestMapper.createRequest(
        request,
        scope.correlationId,
        this._anonymousIdentity,
      );

      this._setNormalizedCorrelationId(scope, provisionalRequest.correlationId);
      reply.header(this._config.correlationIdHeaderName, scope.correlationId);

      const identity = await this._resolveIdentity(provisionalRequest);
      const mappedRequest =
        identity === this._anonymousIdentity
          ? provisionalRequest
          : this._requestMapper.createRequest(
              request,
              scope.correlationId,
              identity,
            );
      const input: IPlatformHttpRouteContextFactoryInput = Object.freeze({
        request: mappedRequest,
        baseContext: Object.freeze({
          correlationId: mappedRequest.correlationId,
          identity,
          signal: scope.abortController.signal,
        }),
      });

      return registration.execute(input);
    });

    await this._responseMapper.send(response, reply, {
      correlationId: scope.correlationId,
      correlationIdHeaderName: this._config.correlationIdHeaderName,
      isHeadRequest: registration.method === 'HEAD',
      logger: this._logger,
      registerStream: (stream): void => {
        scope.activeStreams.add(stream);
      },
      unregisterStream: (stream): void => {
        scope.activeStreams.delete(stream);
      },
    });
  }

  private async _resolveIdentity(
    request: IPlatformHttpRequest,
  ): Promise<PlatformRequestIdentityType> {
    if (!this._config.identityResolver) {
      return this._anonymousIdentity;
    }

    let resolvedIdentity: PlatformRequestIdentityType;

    try {
      resolvedIdentity = await this._config.identityResolver.resolve(
        this._requestMapper.createIdentityResolutionRequest(request),
      );
    } catch (error) {
      if (
        error instanceof PlatformHttpError &&
        (error.code === 'HTTP_UNAUTHENTICATED' ||
          error.code === 'IDENTITY_RESOLUTION_UNAVAILABLE')
      ) {
        throw error;
      }

      this._logger.error('HTTP identity resolution failed.', {
        correlationId: request.correlationId,
        errorCode: 'IDENTITY_RESOLUTION_UNAVAILABLE',
        errorName: error instanceof Error ? error.name : 'UnknownError',
      });

      throw new PlatformHttpError(
        'IDENTITY_RESOLUTION_UNAVAILABLE',
        'Request identity resolution is unavailable.',
      );
    }

    try {
      return this._normalizeIdentity(resolvedIdentity);
    } catch (error) {
      this._logger.error(
        'HTTP identity resolver returned an invalid identity.',
        {
          correlationId: request.correlationId,
          errorCode: 'IDENTITY_RESOLUTION_UNAVAILABLE',
          errorName: error instanceof Error ? error.name : 'UnknownError',
        },
      );

      throw new PlatformHttpError(
        'IDENTITY_RESOLUTION_UNAVAILABLE',
        'Request identity resolution is unavailable.',
      );
    }
  }

  private _normalizeIdentity(identity: unknown): PlatformRequestIdentityType {
    if (
      typeof identity !== 'object' ||
      identity === null ||
      !('authenticationType' in identity)
    ) {
      throw new PlatformHttpError(
        'IDENTITY_RESOLUTION_UNAVAILABLE',
        'Identity resolver returned an invalid identity.',
      );
    }

    const candidate = identity as Record<string, unknown>;

    if (candidate.authenticationType === 'anonymous') {
      if (
        !this._isEmptyStringArray(candidate.roles) ||
        !this._isEmptyStringArray(candidate.permissions)
      ) {
        throw new PlatformHttpError(
          'IDENTITY_RESOLUTION_UNAVAILABLE',
          'Anonymous identities must not contain roles or permissions.',
        );
      }

      return this._anonymousIdentity;
    }

    if (
      candidate.authenticationType === 'delegated' &&
      typeof candidate.subjectId === 'string' &&
      this._isStringArray(candidate.roles) &&
      this._isStringArray(candidate.permissions)
    ) {
      return new PlatformDelegatedIdentity({
        subjectId: candidate.subjectId,
        roles: candidate.roles,
        permissions: candidate.permissions,
      });
    }

    throw new PlatformHttpError(
      'IDENTITY_RESOLUTION_UNAVAILABLE',
      'Identity resolver returned an invalid identity.',
    );
  }

  private _isEmptyStringArray(value: unknown): value is readonly string[] {
    return this._isStringArray(value) && value.length === 0;
  }

  private _isStringArray(value: unknown): value is readonly string[] {
    return (
      Array.isArray(value) && value.every((item) => typeof item === 'string')
    );
  }

  private async _withRequestTimeout<T>(
    scope: IActiveRequestScope,
    operation: () => Promise<T>,
  ): Promise<T> {
    let timeout: ReturnType<typeof setTimeout> | undefined;
    const timeoutPromise = new Promise<never>((_resolve, reject): void => {
      timeout = setTimeout((): void => {
        void this._abortRequestScope(scope, 'request-timeout');
        reject(
          new PlatformHttpError(
            'GATEWAY_TIMEOUT',
            'The route did not complete before the request timeout.',
          ),
        );
      }, this._config.requestTimeoutMs);
    });

    try {
      return await Promise.race([operation(), timeoutPromise]);
    } finally {
      if (timeout !== undefined) {
        clearTimeout(timeout);
      }
    }
  }

  private _createCorsOptions(
    cors: IPlatformHttpCorsConfig,
  ): FastifyCorsOptions {
    const allowedOrigins = new Set(cors.allowedOrigins);

    return {
      credentials: cors.credentials === true,
      methods: [...cors.allowedMethods],
      origin: async (origin?: string): Promise<boolean> =>
        !!origin && allowedOrigins.has(origin),
      preflightContinue: false,
    };
  }

  private _readCorrelationId(request: FastifyRequest): string {
    const value =
      request.headers[this._config.correlationIdHeaderName.toLowerCase()];

    return typeof value === 'string' ? value : randomUUID();
  }

  private _ensureCorrelationId(request: FastifyRequest): string {
    const scope = this._getRequestScope(request);

    try {
      const normalizedRequest = this._requestMapper.createRequest(
        request,
        scope.correlationId,
        this._anonymousIdentity,
      );

      this._setNormalizedCorrelationId(scope, normalizedRequest.correlationId);
    } catch {
      scope.correlationId = randomUUID();
    }

    return scope.correlationId;
  }

  private _setNormalizedCorrelationId(
    scope: IActiveRequestScope,
    correlationId: string,
  ): void {
    if (scope.correlationId !== correlationId) {
      this._logger.warn('Invalid correlation ID was replaced.', {
        correlationId,
        errorCode: 'INVALID_CORRELATION_ID',
      });
    }

    scope.correlationId = correlationId;
  }

  private _getRequestScope(request: FastifyRequest): IActiveRequestScope {
    const existingScope = this._requestScopes.get(request);

    if (existingScope) {
      return existingScope;
    }

    const scope: IActiveRequestScope = {
      abortController: new AbortController(),
      correlationId: randomUUID(),
      startedAt: performance.now(),
      activeStreams: new Set<IActiveResponseStream>(),
    };

    this._requestScopes.set(request, scope);

    return scope;
  }

  private _mapError(error: unknown): IPlatformHttpErrorResponse {
    const code = this._getErrorCode(error);

    switch (code) {
      case 'FST_ERR_CTP_BODY_TOO_LARGE':
      case 'PAYLOAD_TOO_LARGE':
        return {
          statusCode: 413,
          code: 'PAYLOAD_TOO_LARGE',
          message: 'The HTTP request payload is too large.',
        };

      case 'FST_ERR_CTP_INVALID_MEDIA_TYPE':
      case 'UNSUPPORTED_MEDIA_TYPE':
        return {
          statusCode: 415,
          code: 'UNSUPPORTED_MEDIA_TYPE',
          message: 'The request media type is not supported.',
        };

      case 'INVALID_REQUEST_BODY':
      case 'FST_ERR_CTP_INVALID_JSON_BODY':
        return {
          statusCode: 400,
          code: 'INVALID_REQUEST_BODY',
          message: 'The HTTP request body is invalid.',
        };

      case 'IDENTITY_RESOLUTION_UNAVAILABLE':
        return {
          statusCode: 503,
          code: 'IDENTITY_RESOLUTION_UNAVAILABLE',
          message: 'Request identity resolution is temporarily unavailable.',
        };

      case 'HTTP_UNAUTHENTICATED':
        return {
          statusCode: 401,
          code: 'UNAUTHENTICATED',
          message: 'Authentication is required for this route.',
        };

      case 'GATEWAY_TIMEOUT':
        return {
          statusCode: 504,
          code: 'GATEWAY_TIMEOUT',
          message: 'The request timed out.',
        };

      default:
        return {
          statusCode: 500,
          code: 'INTERNAL_ERROR',
          message: 'An unexpected error occurred.',
        };
    }
  }

  private _sendErrorEnvelope(
    reply: FastifyReply,
    correlationId: string,
    error: IPlatformHttpErrorResponse,
  ): void {
    reply
      .code(error.statusCode)
      .header(this._config.correlationIdHeaderName, correlationId);

    if (error.code === 'UNAUTHENTICATED') {
      reply.header('WWW-Authenticate', 'Bearer');
    }

    reply.type('application/json; charset=utf-8').send({
      correlationId,
      error: {
        code: error.code,
        message: error.message,
      },
    });
  }

  private async _abortRequestScope(
    scope: IActiveRequestScope,
    reason: string,
  ): Promise<void> {
    if (!scope.abortController.signal.aborted) {
      scope.abortController.abort(reason);
    }

    const streams = [...scope.activeStreams];

    scope.activeStreams.clear();

    await Promise.all(
      streams.map(async (activeStream): Promise<void> => {
        activeStream.cancel();

        try {
          await activeStream.stream.cancel(reason);
        } catch (error) {
          this._logger.warn('HTTP response stream cancellation failed.', {
            correlationId: scope.correlationId,
            errorCode: 'STREAM_TRANSFER_FAILURE',
            errorName: error instanceof Error ? error.name : 'UnknownError',
          });
        }
      }),
    );
  }

  private async _terminateCommittedResponse(
    request: FastifyRequest,
    reply: FastifyReply,
  ): Promise<void> {
    const scope = this._requestScopes.get(request);

    if (scope) {
      await this._abortRequestScope(scope, 'response-transfer-failed');
      this._requestScopes.delete(request);
    }

    if (!reply.raw.destroyed) {
      reply.raw.destroy();
    }
  }

  private _asBuffer(payload: string | Buffer): Buffer {
    return Buffer.isBuffer(payload) ? payload : Buffer.from(payload, 'utf-8');
  }

  private async _stopGracefully(): Promise<void> {
    const closePromise = this._fastify.close();
    let gracefulTimeout: ReturnType<typeof setTimeout> | undefined;
    let didTimeOut = false;
    const timeoutPromise = new Promise<void>((resolve): void => {
      gracefulTimeout = setTimeout((): void => {
        didTimeOut = true;

        for (const scope of this._requestScopes.values()) {
          void this._abortRequestScope(scope, 'graceful-shutdown-timeout');
        }

        this._fastify.server.closeIdleConnections?.();
        this._fastify.server.closeAllConnections?.();

        resolve();
      }, this._config.gracefulShutdownTimeoutMs);
    });

    try {
      await Promise.race([closePromise, timeoutPromise]);

      if (!didTimeOut && gracefulTimeout) {
        clearTimeout(gracefulTimeout);
      }

      if (didTimeOut) {
        void closePromise.catch((error) => {
          this._logger.error('HTTP server close failed after grace timeout.', {
            errorCode: 'GRACEFUL_SHUTDOWN_FAILURE',
            errorName: error instanceof Error ? error.name : 'UnknownError',
          });
        });
      }

      this._state = 'stopped';
      this._logger.info('HTTP server stopped.', { didTimeOut });
    } catch (error) {
      this._state = 'started';
      throw error;
    }
  }

  private _getErrorCode(error: unknown): string | undefined {
    if (error instanceof PlatformHttpBodyParseError) {
      return error.code;
    }

    if (error instanceof PlatformHttpError) {
      return error.code;
    }

    if (
      typeof error === 'object' &&
      error !== null &&
      'code' in error &&
      typeof error.code === 'string'
    ) {
      return error.code;
    }

    return undefined;
  }

  private _invalidTransition(action: string): PlatformHttpServerLifecycleError {
    return new PlatformHttpServerLifecycleError(
      'INVALID_LIFECYCLE_TRANSITION',
      `Cannot ${action} while server is ${this._state}.`,
      this._state,
    );
  }

  private _validateConfig(config: IPlatformHttpServerConfig): void {
    if (!config.host.trim().length || !Number.isInteger(config.port)) {
      throw new PlatformHttpServerLifecycleError(
        'INVALID_SERVER_CONFIGURATION',
        'Server host must be non-empty and port must be an integer.',
        this._state,
      );
    }

    const positiveIntegerFields: [string, number | undefined][] = [
      ['bodyLimitBytes', config.bodyLimitBytes],
      ['slowRequestThresholdMs', config.slowRequestThresholdMs],
      ['requestTimeoutMs', config.requestTimeoutMs],
      ['gracefulShutdownTimeoutMs', config.gracefulShutdownTimeoutMs],
    ];

    for (const [name, value] of positiveIntegerFields) {
      if (
        value !== undefined &&
        (!Number.isFinite(value) || value <= 0 || !Number.isInteger(value))
      ) {
        throw new PlatformHttpServerLifecycleError(
          'INVALID_SERVER_CONFIGURATION',
          `${name} must be a positive finite integer.`,
          this._state,
        );
      }
    }

    if (config.trustedProxyAddresses) {
      this._validateTrustedProxyAddresses(config.trustedProxyAddresses);
    }

    if (config.cors) {
      this._validateCorsConfig(config.cors);
    }
  }

  private _validateCorsConfig(cors: IPlatformHttpCorsConfig): void {
    this._validateNonEmptyStrings(cors.allowedOrigins, 'cors.allowedOrigins');

    if (!cors.allowedOrigins.length || !cors.allowedMethods.length) {
      throw this._invalidConfiguration(
        'CORS origin and method allowlists must not be empty.',
      );
    }

    for (const origin of cors.allowedOrigins) {
      try {
        const parsedOrigin = new URL(origin);

        if (
          (parsedOrigin.protocol !== 'http:' &&
            parsedOrigin.protocol !== 'https:') ||
          parsedOrigin.origin !== origin
        ) {
          throw new Error('Invalid origin.');
        }
      } catch {
        throw this._invalidConfiguration(
          'CORS origins must be exact HTTP(S) origins without wildcards.',
        );
      }
    }

    for (const method of cors.allowedMethods) {
      if (!ALLOWED_APPLICATION_HTTP_METHODS.includes(method)) {
        throw this._invalidConfiguration(
          'CORS methods must be supported application HTTP methods.',
        );
      }
    }
  }

  private _validateNonEmptyStrings(
    values: readonly string[],
    name: string,
  ): void {
    if (values.some((value) => !value.trim().length)) {
      throw this._invalidConfiguration(
        `${name} must contain only non-empty strings.`,
      );
    }
  }

  private _validateTrustedProxyAddresses(values: readonly string[]): void {
    for (const value of values) {
      const [address, prefix, ...rest] = value.split('/');
      const ipVersion = address === undefined ? 0 : isIP(address);
      const hasValidPrefix =
        prefix === undefined ||
        (/^\d+$/u.test(prefix) &&
          Number.isInteger(Number(prefix)) &&
          Number(prefix) >= 0 &&
          Number(prefix) <= (ipVersion === 4 ? 32 : 128));

      if (ipVersion === 0 || rest.length > 0 || !hasValidPrefix) {
        throw this._invalidConfiguration(
          'trustedProxyAddresses must contain only explicit IP addresses or CIDR ranges.',
        );
      }
    }
  }

  private _invalidConfiguration(
    message: string,
  ): PlatformHttpServerLifecycleError {
    return new PlatformHttpServerLifecycleError(
      'INVALID_SERVER_CONFIGURATION',
      message,
      this._state,
    );
  }
}
