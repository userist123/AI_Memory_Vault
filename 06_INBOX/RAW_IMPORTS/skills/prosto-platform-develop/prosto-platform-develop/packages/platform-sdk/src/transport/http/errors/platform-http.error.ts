import { PlatformSdkError } from '@/errors/index.js';

/**
 * @alpha
 * Typed error codes for SDK transport contract violations and route-boundary rejections.
 */
export type PlatformHttpErrorCodeType =
  | 'INVALID_HTTP_METHOD'
  | 'INVALID_CORRELATION_ID'
  | 'INVALID_STATUS_CODE'
  | 'INVALID_HEADER_NAME'
  | 'INVALID_COOKIE'
  | 'INVALID_ROUTE_GRAMMAR'
  | 'DUPLICATE_ROUTE'
  | 'UNSUPPORTED_MEDIA_TYPE'
  | 'INVALID_REQUEST_BODY'
  | 'PAYLOAD_TOO_LARGE'
  | 'IDENTITY_RESOLUTION_UNAVAILABLE'
  | 'HTTP_UNAUTHENTICATED'
  | 'INTERNAL_HANDLER_ERROR'
  | 'GATEWAY_TIMEOUT'
  | 'STREAM_TRANSFER_FAILURE'
  | 'INVALID_BODY_METADATA'
  | 'INVALID_LIFECYCLE_TRANSITION';

/**
 * @alpha
 * Framework-neutral HTTP transport error.
 * Covers SDK transport contract violations and route-boundary rejections.
 * Does not contain Fastify lifecycle or network errors.
 */
export class PlatformHttpError extends PlatformSdkError {
  constructor(
    code: PlatformHttpErrorCodeType,
    message: string,
    details?: Readonly<Record<string, unknown>>,
  ) {
    super(code, message, details);
    this.name = 'PlatformHttpError';
  }
}
