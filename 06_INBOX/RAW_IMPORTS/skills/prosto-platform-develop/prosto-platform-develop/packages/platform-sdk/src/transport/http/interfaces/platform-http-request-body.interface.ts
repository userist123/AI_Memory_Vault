/**
 * @alpha
 * Structured Content-Disposition representation.
 * Raw `Content-Disposition` header is forbidden in SDK contracts.
 */
export interface IPlatformHttpContentDisposition {
  readonly type: 'inline' | 'attachment';
  readonly filename?: string;
}

/**
 * @alpha
 * JSON request body variant. Payload remains `unknown`.
 */
export interface IPlatformHttpJsonRequestBody {
  readonly variant: 'json';
  readonly data: unknown;
}

/**
 * @alpha
 * UTF-8 text request body variant.
 */
export interface IPlatformHttpTextRequestBody {
  readonly variant: 'text';
  readonly data: string;
}

/**
 * @alpha
 * Finite binary request body variant using `Uint8Array`.
 */
export interface IPlatformHttpBinaryRequestBody {
  readonly variant: 'binary';
  readonly data: Uint8Array;
  readonly contentType: string;
}

/**
 * @alpha
 * Empty request body variant.
 */
export interface IPlatformHttpEmptyRequestBody {
  readonly variant: 'empty';
}

/**
 * @alpha
 * Discriminated union of finite request body variants.
 */
export type PlatformHttpRequestBodyType =
  | IPlatformHttpEmptyRequestBody
  | IPlatformHttpJsonRequestBody
  | IPlatformHttpTextRequestBody
  | IPlatformHttpBinaryRequestBody;
