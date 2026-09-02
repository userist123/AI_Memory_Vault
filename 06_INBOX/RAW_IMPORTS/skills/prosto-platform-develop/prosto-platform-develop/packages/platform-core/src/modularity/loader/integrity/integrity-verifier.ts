import type {
  IDiscoveredModuleArtifact,
  IIntegrityVerifier,
  IntegrityVerificationResultType,
} from '../interfaces/index.js';
import { RuntimeErrorCodes } from '@/common/index.js';

/**
 * @deprecated
 * Default integrity verifier.
 *
 * Validates integrity metadata (checksum format, signature presence) at the
 * pre-load stage. Actual content integrity verification (checksum match,
 * signature verification) is delegated to the artifact source (e.g. PathSource).
 */
export class IntegrityVerifier implements IIntegrityVerifier {
  async verify(
    artifact: IDiscoveredModuleArtifact,
  ): Promise<IntegrityVerificationResultType> {
    const integrity = artifact.integrity;

    // No integrity metadata — skip verification (not all sources require it)
    if (!integrity) {
      return { ok: true };
    }

    const { checksum, signature } = integrity;

    // Empty integrity block is not a hard failure at this stage
    if (!checksum && !signature) {
      return { ok: true };
    }

    // Validate checksum format if present
    if (checksum) {
      const validationError = this.validateChecksum(checksum);

      if (validationError) {
        return {
          ok: false,
          error: {
            reasonCode: RuntimeErrorCodes.IntegrityCheckFailed,
            message: validationError,
            remediationHint:
              'Provide a valid checksum in the format sha256:<hex> or sha512:<hex>',
          },
        };
      }
    }

    // Validate signature is a non-empty string if present
    if (
      signature !== undefined &&
      (typeof signature !== 'string' || signature.length === 0)
    ) {
      return {
        ok: false,
        error: {
          reasonCode: RuntimeErrorCodes.IntegrityCheckFailed,
          message: 'Signature must be a non-empty string',
          remediationHint: 'Provide a valid signature in the artifact manifest',
        },
      };
    }

    return { ok: true };
  }

  private validateChecksum(checksum: string): string | null {
    const checksumPattern = /^(sha256|sha512):[a-f0-9]{64,128}$/i;

    if (!checksumPattern.test(checksum)) {
      return `Invalid checksum format: "${checksum}". Expected sha256:<hex> or sha512:<hex>`;
    }

    return null;
  }
}
