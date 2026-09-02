import type {
  IPlatformHttpRequest,
  PlatformHttpRequestBodyType,
} from '../interfaces/index.js';
import type { PlatformRequestIdentityType } from '../interfaces/platform-request-identity.interface.js';
import type { PlatformHttpMethodType } from '../types/index.js';
import { freezeRecord, freezeRecordOfArrays } from '@/utils/index.js';
import { ALLOWED_APPLICATION_HTTP_METHODS } from '../constants/index.js';

export interface IPlatformHttpRequestInput {
  readonly method: PlatformHttpMethodType;
  readonly path: string;
  readonly params?: Readonly<Record<string, string>>;
  readonly query?: Readonly<Record<string, readonly string[]>>;
  readonly headers?: Readonly<Record<string, readonly string[]>>;
  readonly body?: PlatformHttpRequestBodyType;
  readonly correlationId?: string;
  readonly identity: PlatformRequestIdentityType;
}

/**
 * @alpha
 * Immutable framework-neutral HTTP request value object.
 * Validates method/path, correlation ID, body variant consistency, and defensive-copies binary data.
 */
export class PlatformHttpRequest implements IPlatformHttpRequest {
  readonly method: PlatformHttpMethodType;
  readonly path: string;
  readonly params: Readonly<Record<string, string>>;
  readonly query: Readonly<Record<string, readonly string[]>>;
  readonly headers: Readonly<Record<string, readonly string[]>>;
  readonly body: PlatformHttpRequestBodyType;
  readonly correlationId: string;
  readonly identity: PlatformRequestIdentityType;

  constructor(input: IPlatformHttpRequestInput) {
    this.method = this._validateMethod(input.method);
    this.path = this._validatePath(input.path);
    this.params = freezeRecord(input.params ?? {});
    this.query = freezeRecordOfArrays(input.query ?? {});
    this.headers = freezeRecordOfArrays(input.headers ?? {});
    this.identity = input.identity;

    const rawCorrelationId = input.correlationId ?? '';

    if (this._isValidCorrelationId(rawCorrelationId)) {
      this.correlationId = rawCorrelationId;
    } else {
      this.correlationId = this._generateUUID();
    }

    const bodyInput: PlatformHttpRequestBodyType = input.body ?? {
      variant: 'empty' as const,
    };

    this.body = this._normalizeBody(bodyInput);

    Object.freeze(this);
  }

  private _generateUUID(): string {
    return (
      globalThis.crypto?.randomUUID?.() ??
      'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
        const r = (Math.random() * 16) | 0;
        const v = c === 'x' ? r : (r & 0x3) | 0x8;
        return v.toString(16);
      })
    );
  }

  private _normalizeBody(
    body: PlatformHttpRequestBodyType,
  ): PlatformHttpRequestBodyType {
    switch (body.variant) {
      case 'empty':
        return body;

      case 'json':
        return Object.freeze({
          variant: 'json' as const,
          data: body.data,
        });

      case 'text':
        return Object.freeze({
          variant: 'text' as const,
          data: body.data,
        });

      case 'binary': {
        if (!body.contentType) {
          throw new Error('Binary request body must have a contentType.');
        }

        return Object.freeze({
          variant: 'binary' as const,
          data: this._defensiveCopyBinary(body.data),
          contentType: body.contentType,
        });
      }

      default:
        throw new Error(
          `Unknown request body variant: ${(body as { variant: string }).variant}`,
        );
    }
  }

  private _isValidCorrelationId(value: string): boolean {
    const CORRELATION_ID_MAX_LENGTH = 128;
    const CORRELATION_ID_PATTERN = /^[a-zA-Z0-9\-_./+=]+$/;

    return (
      value.length > 0 &&
      value.length <= CORRELATION_ID_MAX_LENGTH &&
      CORRELATION_ID_PATTERN.test(value)
    );
  }

  private _defensiveCopyBinary(data: Uint8Array): Uint8Array {
    const copy = new Uint8Array(data.byteLength);

    copy.set(data);

    return copy;
  }

  private _validateMethod(
    method: PlatformHttpMethodType,
  ): PlatformHttpMethodType {
    if (!ALLOWED_APPLICATION_HTTP_METHODS.includes(method)) {
      const allowed = ALLOWED_APPLICATION_HTTP_METHODS.join(', ');

      throw new Error(
        `Method "${method}" is not a valid HTTP method. Allowed: ${allowed}.`,
      );
    }

    return method;
  }

  private _validatePath(path: string): string {
    if (!path.startsWith('/')) {
      throw new Error(`Path "${path}" must start with "/".`);
    }

    return path;
  }
}
