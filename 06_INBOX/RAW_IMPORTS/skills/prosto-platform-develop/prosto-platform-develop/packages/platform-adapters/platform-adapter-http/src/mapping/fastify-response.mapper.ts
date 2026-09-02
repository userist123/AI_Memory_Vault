import { Readable } from 'node:stream';
import type {
  IPlatformHttpContentDisposition,
  IPlatformHttpResponse,
  IPlatformHttpSetCookie,
  PlatformHttpSetCookieInputType,
  PlatformHttpResponseBodyType,
} from '@prosto/platform-sdk';
import {
  PlatformHttpContentDisposition,
  PlatformHttpError,
  PlatformHttpResponse,
} from '@prosto/platform-sdk';
import type { FastifyReply } from 'fastify';
import type { IActiveResponseStream } from '@/http-server.interfaces.js';
import type { IPlatformHttpLogger } from '../observability/index.js';

export const RESERVED_RESPONSE_HEADERS = new Set([
  'content-type',
  'content-length',
  'content-disposition',
  'x-correlation-id',
  'set-cookie',
  'content-security-policy',
  'cross-origin-embedder-policy',
  'cross-origin-opener-policy',
  'cross-origin-resource-policy',
  'origin-agent-cluster',
  'referrer-policy',
  'strict-transport-security',
  'x-content-type-options',
  'x-dns-prefetch-control',
  'x-download-options',
  'x-frame-options',
  'x-permitted-cross-domain-policies',
  'x-xss-protection',
  'access-control-allow-credentials',
  'access-control-allow-headers',
  'access-control-allow-methods',
  'access-control-allow-origin',
  'access-control-expose-headers',
  'access-control-max-age',
]);

/**
 * @internal
 * Converts validated SDK responses into Fastify replies. Node stream types are
 * confined to this mapper; handlers work solely with Web ReadableStreams.
 */
export class FastifyResponseMapper {
  public async send(
    response: unknown,
    reply: FastifyReply,
    options: {
      readonly correlationId: string;
      readonly correlationIdHeaderName: string;
      readonly isHeadRequest: boolean;
      readonly logger: IPlatformHttpLogger;
      registerStream(stream: IActiveResponseStream): void;
      unregisterStream(stream: IActiveResponseStream): void;
    },
  ): Promise<void> {
    const normalizedResponse = this._normalizeResponse(response);

    reply.code(normalizedResponse.status);
    reply.header(options.correlationIdHeaderName, options.correlationId);

    for (const [name, value] of Object.entries(normalizedResponse.headers)) {
      reply.header(name, value);
    }

    if (
      normalizedResponse.cookies !== undefined &&
      normalizedResponse.cookies.length
    ) {
      reply.header(
        'Set-Cookie',
        normalizedResponse.cookies.map((cookie) =>
          this._serializeSetCookie(cookie),
        ),
      );
    }

    await this._sendBody(normalizedResponse.body, reply, options);
  }

  private _normalizeResponse(response: unknown): IPlatformHttpResponse {
    if (!this._isRecord(response)) {
      throw new PlatformHttpError(
        'INTERNAL_HANDLER_ERROR',
        'A route handler must return an HTTP response object.',
      );
    }

    const headers = this._normalizeHeaders(response.headers);
    const cookies = this._normalizeCookies(response.cookies);
    const body = this._normalizeBody(response.body);

    try {
      return new PlatformHttpResponse({
        status: response.status as number,
        headers,
        cookies,
        body,
      });
    } catch (error) {
      if (error instanceof PlatformHttpError) {
        throw error;
      }

      throw new PlatformHttpError(
        'INVALID_BODY_METADATA',
        'The route handler returned invalid response metadata.',
      );
    }
  }

  private _normalizeHeaders(value: unknown): Record<string, string> {
    if (value === undefined) {
      return {};
    }

    if (!this._isRecord(value)) {
      throw new PlatformHttpError(
        'INVALID_HEADER_NAME',
        'Response headers must be a string record.',
      );
    }

    const normalized: Record<string, string> = {};

    for (const [name, headerValue] of Object.entries(value)) {
      const lowerName = name.toLowerCase();

      if (
        !this._isValidHeaderName(name) ||
        RESERVED_RESPONSE_HEADERS.has(lowerName) ||
        typeof headerValue !== 'string' ||
        /[\r\n]/u.test(headerValue)
      ) {
        throw new PlatformHttpError(
          'INVALID_HEADER_NAME',
          'The route handler returned an invalid or reserved response header.',
        );
      }

      normalized[name] = headerValue;
    }

    return normalized;
  }

  private _normalizeCookies(
    value: unknown,
  ): readonly PlatformHttpSetCookieInputType[] | undefined {
    if (value === undefined) {
      return undefined;
    }

    if (!Array.isArray(value)) {
      throw new PlatformHttpError(
        'INVALID_COOKIE',
        'Response cookies must be an array of structured cookie instructions.',
      );
    }

    return value as readonly PlatformHttpSetCookieInputType[];
  }

  private _normalizeBody(value: unknown): PlatformHttpResponseBodyType {
    if (value === undefined) {
      return { variant: 'empty' };
    }

    if (!this._isRecord(value) || typeof value.variant !== 'string') {
      throw new PlatformHttpError(
        'INVALID_BODY_METADATA',
        'Response body metadata is invalid.',
      );
    }

    switch (value.variant) {
      case 'empty':
        return { variant: 'empty' };

      case 'json':
        return { variant: 'json', data: value.data };

      case 'binary':
        if (
          !(value.data instanceof Uint8Array) ||
          !this._isValidContentType(value.contentType)
        ) {
          throw new PlatformHttpError(
            'INVALID_BODY_METADATA',
            'Binary response body metadata is invalid.',
          );
        }

        return {
          variant: 'binary',
          data: value.data,
          contentType: value.contentType,
          ...(value.contentDisposition !== undefined && {
            contentDisposition: this._normalizeContentDisposition(
              value.contentDisposition,
            ),
          }),
        };

      case 'stream': {
        const contentLength = value.contentLength;

        if (
          !this._isReadableStream(value.stream) ||
          !this._isValidContentType(value.contentType) ||
          (contentLength !== undefined &&
            (typeof contentLength !== 'number' ||
              !Number.isSafeInteger(contentLength) ||
              contentLength < 0))
        ) {
          throw new PlatformHttpError(
            'INVALID_BODY_METADATA',
            'Stream response body metadata is invalid.',
          );
        }

        return {
          variant: 'stream',
          stream: value.stream,
          contentType: value.contentType,
          ...(contentLength !== undefined && {
            contentLength,
          }),
          ...(value.contentDisposition !== undefined && {
            contentDisposition: this._normalizeContentDisposition(
              value.contentDisposition,
            ),
          }),
        };
      }

      default:
        throw new PlatformHttpError(
          'INVALID_BODY_METADATA',
          'Response body metadata is invalid.',
        );
    }
  }

  private _normalizeContentDisposition(
    value: unknown,
  ): IPlatformHttpContentDisposition {
    if (
      !this._isRecord(value) ||
      (value.type !== 'inline' && value.type !== 'attachment') ||
      (value.filename !== undefined && typeof value.filename !== 'string')
    ) {
      throw new PlatformHttpError(
        'INVALID_BODY_METADATA',
        'Response content disposition metadata is invalid.',
      );
    }

    const normalized = new PlatformHttpContentDisposition({
      type: value.type,
      ...(value.filename !== undefined && { filename: value.filename }),
    });

    return {
      type: normalized.type,
      ...(normalized.filename !== undefined && {
        filename: normalized.filename,
      }),
    };
  }

  private async _sendBody(
    body: PlatformHttpResponseBodyType,
    reply: FastifyReply,
    options: {
      readonly correlationId: string;
      readonly isHeadRequest: boolean;
      readonly logger: IPlatformHttpLogger;
      registerStream(stream: IActiveResponseStream): void;
      unregisterStream(stream: IActiveResponseStream): void;
    },
  ): Promise<void> {
    switch (body.variant) {
      case 'empty': {
        reply.send();
        return;
      }

      case 'json': {
        reply.type('application/json; charset=utf-8');

        if (options.isHeadRequest) {
          reply.send();
          return;
        }

        reply.send(body.data);
        return;
      }

      case 'binary': {
        reply.type(body.contentType);
        reply.header('Content-Length', body.data.byteLength);
        this._setContentDisposition(reply, body.contentDisposition);

        if (options.isHeadRequest) {
          reply.send();
          return;
        }

        reply.send(Buffer.from(body.data));
        return;
      }

      case 'stream': {
        this._validateStreamBody(body);
        reply.type(body.contentType);

        if (body.contentLength !== undefined) {
          reply.header('Content-Length', body.contentLength);
        }

        this._setContentDisposition(reply, body.contentDisposition);

        if (options.isHeadRequest) {
          await this._cancelStream(body.stream, options);
          reply.send();
          return;
        }

        this._sendStream(body.stream, reply, options);
        return;
      }
    }
  }

  private _sendStream(
    stream: ReadableStream<Uint8Array>,
    reply: FastifyReply,
    options: {
      readonly correlationId: string;
      readonly logger: IPlatformHttpLogger;
      registerStream(stream: IActiveResponseStream): void;
      unregisterStream(stream: IActiveResponseStream): void;
    },
  ): void {
    const nodeStream = Readable.fromWeb(
      stream as unknown as Parameters<typeof Readable.fromWeb>[0],
    );
    const activeStream: IActiveResponseStream = {
      stream,
      cancel: (): void => {
        nodeStream.destroy();
      },
    };

    options.registerStream(activeStream);

    const release = (): void => {
      options.unregisterStream(activeStream);
    };

    nodeStream.once('close', release);
    nodeStream.once('error', (error: Error): void => {
      options.logger.error('HTTP response stream transfer failed.', {
        correlationId: options.correlationId,
        errorCode: 'STREAM_TRANSFER_FAILURE',
        errorName: error.name,
      });
      void this._cancelStream(stream, options);
    });

    try {
      reply.send(nodeStream);
    } catch (error) {
      release();
      nodeStream.destroy();
      throw error;
    }
  }

  private _validateStreamBody(
    body: Extract<PlatformHttpResponseBodyType, { variant: 'stream' }>,
  ): void {
    if (
      body.contentLength !== undefined &&
      (!Number.isSafeInteger(body.contentLength) || body.contentLength < 0)
    ) {
      throw new PlatformHttpError(
        'INVALID_BODY_METADATA',
        'Stream content length must be a non-negative safe integer.',
      );
    }
  }

  private _setContentDisposition(
    reply: FastifyReply,
    disposition: IPlatformHttpContentDisposition | undefined,
  ): void {
    if (disposition === undefined) {
      return;
    }

    const normalized = new PlatformHttpContentDisposition(disposition);
    let value = normalized.type;

    if (normalized.safeFilename !== undefined) {
      const escapedFilename = normalized.safeFilename.replace(
        /[\\"]/gu,
        '\\$&',
      );

      value += `; filename="${escapedFilename}"`;
    }

    if (normalized.rfc5987Filename !== undefined) {
      value += `; filename*=${normalized.rfc5987Filename}`;
    }

    reply.header('Content-Disposition', value);
  }

  private _serializeSetCookie(cookie: IPlatformHttpSetCookie): string {
    let value = `${cookie.name}=${cookie.value}`;

    if (cookie.expiresAt !== undefined) {
      value += `; Expires=${new Date(cookie.expiresAt).toUTCString()}`;
    }

    if (cookie.maxAge !== undefined) {
      value += `; Max-Age=${cookie.maxAge}`;
    }

    if (cookie.domain !== undefined) {
      value += `; Domain=${cookie.domain}`;
    }

    if (cookie.path !== undefined) {
      value += `; Path=${cookie.path}`;
    }

    if (cookie.httpOnly === true) {
      value += '; HttpOnly';
    }

    if (cookie.secure === true) {
      value += '; Secure';
    }

    if (cookie.sameSite !== undefined) {
      const sameSite =
        cookie.sameSite === 'strict'
          ? 'Strict'
          : cookie.sameSite === 'lax'
            ? 'Lax'
            : 'None';

      value += `; SameSite=${sameSite}`;
    }

    return value;
  }

  private async _cancelStream(
    stream: ReadableStream<Uint8Array>,
    options: {
      readonly correlationId: string;
      readonly logger: IPlatformHttpLogger;
    },
  ): Promise<void> {
    try {
      await stream.cancel();
    } catch (error) {
      options.logger.warn('HTTP response stream cancellation failed.', {
        correlationId: options.correlationId,
        errorCode: 'STREAM_TRANSFER_FAILURE',
        errorName: error instanceof Error ? error.name : 'UnknownError',
      });
    }
  }

  private _isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
  }

  private _isReadableStream(
    value: unknown,
  ): value is ReadableStream<Uint8Array> {
    return (
      typeof value === 'object' &&
      value !== null &&
      'getReader' in value &&
      typeof value.getReader === 'function' &&
      'cancel' in value &&
      typeof value.cancel === 'function'
    );
  }

  private _isValidContentType(value: unknown): value is string {
    return (
      typeof value === 'string' &&
      value.trim().length > 0 &&
      value === value.trim() &&
      /^[!#$%&'*+\-.^_`|~0-9A-Za-z]+\/[!#$%&'*+\-.^_`|~0-9A-Za-z]+(?:\s*;\s*[^\r\n]+)?$/u.test(
        value,
      )
    );
  }

  private _isValidHeaderName(name: string): boolean {
    return /^[!#$%&'*+\-.^_`|~0-9A-Za-z]+$/u.test(name);
  }
}
