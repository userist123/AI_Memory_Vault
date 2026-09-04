import type { IPlatformHttpContentDisposition } from './platform-http-request-body.interface.js';

/**
 * @alpha
 * JSON response body variant. Payload remains `unknown`.
 */
export interface IPlatformHttpJsonResponseBody {
  readonly variant: 'json';
  readonly data: unknown;
}

/**
 * @alpha
 * Finite binary response body variant using `Uint8Array`.
 */
export interface IPlatformHttpBinaryResponseBody {
  readonly variant: 'binary';
  readonly data: Uint8Array;
  readonly contentType: string;
  readonly contentDisposition?: IPlatformHttpContentDisposition;
}

/**
 * @alpha
 * Streaming response body variant using standard `ReadableStream<Uint8Array>`.
 * Framework-neutral — no Fastify/Node types in SDK.
 */
export interface IPlatformHttpStreamResponseBody {
  readonly variant: 'stream';
  readonly stream: ReadableStream<Uint8Array>;
  readonly contentType: string;
  readonly contentLength?: number;
  readonly contentDisposition?: IPlatformHttpContentDisposition;
}

/**
 * @alpha
 * Empty response body variant (e.g., for 204/304).
 */
export interface IPlatformHttpEmptyResponseBody {
  readonly variant: 'empty';
}

/**
 * @alpha
 * Discriminated union of response body variants.
 */
export type PlatformHttpResponseBodyType =
  | IPlatformHttpEmptyResponseBody
  | IPlatformHttpJsonResponseBody
  | IPlatformHttpBinaryResponseBody
  | IPlatformHttpStreamResponseBody;
