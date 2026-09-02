import type {
  IPlatformHttpRequest,
  IPlatformIdentityResolutionRequest,
  PlatformHttpMethodType,
  PlatformHttpRequestBodyType,
  PlatformRequestIdentityType,
} from '@prosto/platform-sdk';
import {
  ALLOWED_APPLICATION_HTTP_METHODS,
  type IPlatformHttpRequestInput,
  PlatformHttpError,
  PlatformHttpRequest,
  PlatformIdentityResolutionRequest,
} from '@prosto/platform-sdk';
import type { FastifyRequest } from 'fastify';
import { FastifyBodyMapper } from './fastify-body.mapper.js';

/**
 * @internal
 * Converts Fastify request data into the framework-neutral SDK value objects.
 * It intentionally copies only primitive HTTP metadata and never exposes
 * Fastify or Node request objects to route handlers or identity resolvers.
 */
export class FastifyRequestMapper {
  createRequest(
    request: FastifyRequest,
    correlationId: string,
    identity: PlatformRequestIdentityType,
  ): IPlatformHttpRequest {
    const metadata = this._extractMetadata(request);

    return new PlatformHttpRequest({
      ...metadata,
      correlationId,
      identity,
    });
  }

  createIdentityResolutionRequest(
    request: IPlatformHttpRequest,
  ): IPlatformIdentityResolutionRequest {
    return new PlatformIdentityResolutionRequest({
      correlationId: request.correlationId,
      method: request.method,
      path: request.path,
      headers: request.headers,
      params: request.params,
      query: request.query,
    });
  }

  private _extractMetadata(
    request: FastifyRequest,
  ): Required<
    Pick<
      IPlatformHttpRequestInput,
      'method' | 'path' | 'params' | 'query' | 'headers' | 'body'
    >
  > {
    return {
      method: this._mapMethod(request.method),
      path: this._mapPath(request),
      params: this._mapParams(request.params),
      query: this._mapStringArrayRecord(request.query, 'query parameter'),
      headers: this._mapHeaders(request),
      body: this._mapBody(request.body),
    };
  }

  private _mapMethod(method: string): PlatformHttpMethodType {
    if (
      !ALLOWED_APPLICATION_HTTP_METHODS.includes(
        method as PlatformHttpMethodType,
      )
    ) {
      throw new PlatformHttpError(
        'INVALID_HTTP_METHOD',
        'The HTTP method is not supported by the application transport.',
      );
    }

    return method as PlatformHttpMethodType;
  }

  private _mapPath(request: FastifyRequest): string {
    const rawUrl = request.raw.url ?? request.url;

    try {
      return new URL(rawUrl, 'http://platform.invalid').pathname;
    } catch {
      throw new PlatformHttpError(
        'INVALID_REQUEST_BODY',
        'The request path could not be normalized.',
      );
    }
  }

  private _mapParams(value: unknown): Record<string, string> {
    const result: Record<string, string> = Object.create(null) as Record<
      string,
      string
    >;

    if (!this._isRecord(value)) {
      return result;
    }

    for (const [name, parameter] of Object.entries(value)) {
      if (typeof parameter !== 'string') {
        throw new PlatformHttpError(
          'INVALID_REQUEST_BODY',
          'Route parameters must be strings.',
        );
      }

      result[name] = parameter;
    }

    return result;
  }

  private _mapStringArrayRecord(
    value: unknown,
    label: string,
  ): Record<string, string[]> {
    const result: Record<string, string[]> = Object.create(null) as Record<
      string,
      string[]
    >;

    if (!this._isRecord(value)) {
      return result;
    }

    for (const [name, rawValue] of Object.entries(value)) {
      const values = Array.isArray(rawValue) ? rawValue : [rawValue];

      if (values.some((item) => typeof item !== 'string')) {
        throw new PlatformHttpError(
          'INVALID_REQUEST_BODY',
          `${label} values must be strings.`,
        );
      }

      result[name] = values as string[];
    }

    return result;
  }

  private _mapHeaders(request: FastifyRequest): Record<string, string[]> {
    const rawHeaders = request.raw.rawHeaders;

    if (rawHeaders.length) {
      const result: Record<string, string[]> = Object.create(null) as Record<
        string,
        string[]
      >;

      for (let index = 0; index < rawHeaders.length; index += 2) {
        const name = rawHeaders[index];
        const value = rawHeaders[index + 1];

        if (name === undefined || value === undefined) {
          continue;
        }

        const normalizedName = name.toLowerCase();
        const values = result[normalizedName] ?? [];

        values.push(value);
        result[normalizedName] = values;
      }

      return result;
    }

    return this._mapStringArrayRecord(request.headers, 'Header');
  }

  private _mapBody(body: unknown): PlatformHttpRequestBodyType {
    if (body === undefined || body === null) {
      return FastifyBodyMapper.empty();
    }

    if (
      typeof body !== 'object' ||
      !('variant' in body) ||
      typeof body.variant !== 'string'
    ) {
      throw new PlatformHttpError(
        'INVALID_REQUEST_BODY',
        'The request body was not parsed by an allowed transport parser.',
      );
    }

    return body as PlatformHttpRequestBodyType;
  }

  private _isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
  }
}
