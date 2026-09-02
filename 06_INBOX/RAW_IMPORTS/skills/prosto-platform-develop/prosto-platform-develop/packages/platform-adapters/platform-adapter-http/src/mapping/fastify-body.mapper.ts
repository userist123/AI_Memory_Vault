import type { PlatformHttpRequestBodyType } from '@prosto/platform-sdk';
import { PlatformHttpBodyParseError } from '../http-server.errors.js';

/**
 * @internal
 * Converts the finite buffers accepted by the transport into framework-neutral
 * SDK body variants. Streaming and arbitrary encodings are intentionally not
 * supported by this mapper.
 */
export class FastifyBodyMapper {
  private static readonly _utf8Decoder = new TextDecoder('utf-8', {
    fatal: true,
  });

  static empty(): PlatformHttpRequestBodyType {
    return { variant: 'empty' };
  }

  static json(payload: Buffer): PlatformHttpRequestBodyType {
    if (!payload.byteLength) {
      return this.empty();
    }

    try {
      return {
        variant: 'json',
        data: JSON.parse(this._decodeUtf8(payload)),
      };
    } catch (error) {
      if (error instanceof PlatformHttpBodyParseError) {
        throw error;
      }

      throw new PlatformHttpBodyParseError(
        'INVALID_REQUEST_BODY',
        'Request JSON body is malformed.',
      );
    }
  }

  static text(payload: Buffer): PlatformHttpRequestBodyType {
    if (!payload.byteLength) {
      return this.empty();
    }

    return {
      variant: 'text',
      data: this._decodeUtf8(payload),
    };
  }

  static binary(payload: Buffer): PlatformHttpRequestBodyType {
    if (!payload.byteLength) {
      return this.empty();
    }

    return {
      variant: 'binary',
      data: new Uint8Array(payload),
      contentType: 'application/octet-stream',
    };
  }

  private static _decodeUtf8(payload: Buffer): string {
    try {
      return this._utf8Decoder.decode(payload);
    } catch {
      throw new PlatformHttpBodyParseError(
        'INVALID_REQUEST_BODY',
        'Request text body is not valid UTF-8.',
      );
    }
  }
}
