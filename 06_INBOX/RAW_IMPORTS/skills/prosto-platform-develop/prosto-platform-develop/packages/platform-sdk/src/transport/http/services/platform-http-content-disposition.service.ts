import type { IPlatformHttpContentDisposition } from '../interfaces/index.js';
import { PlatformHttpError } from '../errors/index.js';

/**
 * @alpha
 * Immutable structured Content-Disposition value object.
 * Forbids CR/LF/control chars in filename and builds safe ASCII fallback / RFC 5987 encoding.
 */
export class PlatformHttpContentDisposition implements IPlatformHttpContentDisposition {
  readonly type: 'inline' | 'attachment';
  readonly filename?: string;
  readonly safeFilename?: string;
  readonly rfc5987Filename?: string;

  private readonly FILENAME_FORBIDDEN_CHARS = /\p{Cc}/u;

  constructor(input: IPlatformHttpContentDisposition) {
    if (!['inline', 'attachment'].includes(input.type)) {
      throw new PlatformHttpError(
        'INVALID_BODY_METADATA',
        'Content-Disposition type must be "inline" or "attachment".',
        { type: input.type },
      );
    }

    this.type = input.type;

    if (input.filename) {
      if (this.FILENAME_FORBIDDEN_CHARS.test(input.filename)) {
        throw new PlatformHttpError(
          'INVALID_BODY_METADATA',
          'Content-Disposition filename contains forbidden characters.',
          { filename: input.filename },
        );
      }

      this.filename = input.filename;
      this.safeFilename = this._buildSafeFilename(input.filename);
      this.rfc5987Filename = this._encodeRfc5987(input.filename);
    }

    Object.freeze(this);
  }

  private _buildSafeFilename(filename: string): string {
    const ASCII_SAFE = /^[\x20-\x7E]+$/;

    if (ASCII_SAFE.test(filename)) return filename;

    return filename.replace(this.FILENAME_FORBIDDEN_CHARS, '_');
  }

  private _encodeRfc5987(filename: string): string {
    const encoded = encodeURIComponent(filename);
    return `UTF-8''${encoded}`;
  }
}
