import { createHash, createVerify } from 'node:crypto';

/**
 * Integrity evidence types supported by the verifier.
 */
export type IntegrityEvidenceType = 'checksum' | 'signature';

/**
 * Supported hash algorithms for checksum verification.
 */
export type ChecksumAlgorithmType = 'sha256' | 'sha512' | 'sha1';

/**
 * Supported signature algorithms.
 */
export type SignatureAlgorithmType =
  | 'rsa-sha256'
  | 'rsa-sha512'
  | 'ecdsa-sha256';

/**
 * Checksum integrity evidence.
 */
export interface IChecksumEvidence {
  readonly type: 'checksum';
  readonly algorithm: ChecksumAlgorithmType;
  readonly value: string;
}

/**
 * Signature integrity evidence.
 */
export interface ISignatureEvidence {
  readonly type: 'signature';
  readonly algorithm: SignatureAlgorithmType;
  readonly value: string;
  readonly publicKey: string;
  readonly keyId?: string;
}

/**
 * Combined integrity evidence.
 */
export type IntegrityEvidencePayloadType =
  | IChecksumEvidence
  | ISignatureEvidence;

/**
 * Result of integrity verification.
 */
export interface IIntegrityVerificationResult {
  /**
   * Whether verification succeeded.
   */
  readonly verified: boolean;
  /**
   * Type of evidence that was verified.
   */
  readonly evidenceType: IntegrityEvidenceType;
  /**
   * Reason code if verification failed.
   */
  readonly reasonCode?: IntegrityReasonCode;
  /**
   * Human-readable message.
   */
  readonly message: string;
}

/**
 * Reason codes for integrity verification failures.
 */
export enum IntegrityReasonCode {
  /**
   * Verification succeeded.
   */
  Verified = 'VERIFIED',
  /**
   * Checksum mismatch between expected and actual.
   */
  ChecksumMismatch = 'CHECKSUM_MISMATCH',
  /**
   * Unsupported checksum algorithm.
   */
  UnsupportedChecksumAlgorithm = 'UNSUPPORTED_CHECKSUM_ALGORITHM',
  /**
   * Invalid checksum format.
   */
  InvalidChecksumFormat = 'INVALID_CHECKSUM_FORMAT',
  /**
   * Signature verification failed.
   */
  SignatureVerificationFailed = 'SIGNATURE_VERIFICATION_FAILED',
  /**
   * Unsupported signature algorithm.
   */
  UnsupportedSignatureAlgorithm = 'UNSUPPORTED_SIGNATURE_ALGORITHM',
  /**
   * Invalid signature format.
   */
  InvalidSignatureFormat = 'INVALID_SIGNATURE_FORMAT',
  /**
   * Public key is required for signature verification.
   */
  MissingPublicKey = 'MISSING_PUBLIC_KEY',
  /**
   * Payload is empty or null.
   */
  EmptyPayload = 'EMPTY_PAYLOAD',
}

/**
 * Parsed checksum with algorithm and value.
 */
export interface IParsedChecksum {
  algorithm: ChecksumAlgorithmType;
  value: string;
}

/**
 * @alpha
 * Centralized integrity verifier for module artifacts.
 * Supports checksum and signature verification.
 */
export class IntegrityVerifier {
  private readonly _supportedChecksumAlgorithms: readonly ChecksumAlgorithmType[] =
    ['sha256', 'sha512', 'sha1'];

  private readonly _supportedSignatureAlgorithms: readonly SignatureAlgorithmType[] =
    ['rsa-sha256', 'rsa-sha512', 'ecdsa-sha256'];

  /**
   * Verify integrity of a module artifact payload.
   */
  verify(
    payload: Buffer,
    evidence: IntegrityEvidencePayloadType,
  ): IIntegrityVerificationResult {
    if (!payload || payload.length === 0) {
      return {
        verified: false,
        evidenceType: evidence.type,
        reasonCode: IntegrityReasonCode.EmptyPayload,
        message: 'Cannot verify integrity: payload is empty.',
      };
    }

    switch (evidence.type) {
      case 'checksum':
        return this._verifyChecksum(payload, evidence);

      case 'signature':
        return this._verifySignature(payload, evidence);

      default: {
        const _exhaustiveCheck: never = evidence;
        return _exhaustiveCheck;
      }
    }
  }

  /**
   * Parse a checksum string in various formats.
   * Supports:
   * - `sha256:<hex>` or `sha256-<base64>` (npm-style)
   * - `<hex>` (plain hex, assumes sha256)
   */
  parseChecksum(checksum: string): IParsedChecksum | null {
    if (!checksum || typeof checksum !== 'string') {
      return null;
    }

    // npm-style integrity: sha256-<base64> or sha512-<base64>
    const npmMatch = checksum.match(/^(sha256|sha512|sha1)-(.+)$/i);

    if (npmMatch) {
      return {
        algorithm: npmMatch[1]?.toLowerCase() as ChecksumAlgorithmType,
        value: npmMatch[2] as string,
      };
    }

    // Colon-separated: sha256:<hex>
    const colonMatch = checksum.match(/^(sha256|sha512|sha1):(.+)$/i);

    if (colonMatch) {
      return {
        algorithm: colonMatch[1]?.toLowerCase() as ChecksumAlgorithmType,
        value: colonMatch[2] as string,
      };
    }

    // Plain hex (assume sha256)
    if (/^[a-fA-F0-9]{64}$/.test(checksum)) {
      return { algorithm: 'sha256', value: checksum };
    }

    if (/^[a-fA-F0-9]{128}$/.test(checksum)) {
      return { algorithm: 'sha512', value: checksum };
    }

    if (/^[a-fA-F0-9]{40}$/.test(checksum)) {
      return { algorithm: 'sha1', value: checksum };
    }

    return null;
  }

  /**
   * Compute checksum for a payload.
   */
  computeChecksum(
    payload: Buffer,
    algorithm: ChecksumAlgorithmType = 'sha256',
    encoding: 'hex' | 'base64' = 'hex',
  ): string {
    return createHash(algorithm).update(payload).digest(encoding);
  }

  /**
   * Compute npm-style integrity string (algorithm-value).
   */
  computeIntegrityString(
    payload: Buffer,
    algorithm: ChecksumAlgorithmType = 'sha256',
  ): string {
    const value = this.computeChecksum(payload, algorithm, 'base64');

    return `${algorithm}-${value}`;
  }

  /**
   * Verify checksum evidence against payload.
   */
  private _verifyChecksum(
    payload: Buffer,
    evidence: IChecksumEvidence,
  ): IIntegrityVerificationResult {
    if (!this._supportedChecksumAlgorithms.includes(evidence.algorithm)) {
      return {
        verified: false,
        evidenceType: 'checksum',
        reasonCode: IntegrityReasonCode.UnsupportedChecksumAlgorithm,
        message: `Unsupported checksum algorithm: ${evidence.algorithm}. Supported: ${this._supportedChecksumAlgorithms.join(', ')}.`,
      };
    }

    try {
      const actualHash = createHash(evidence.algorithm)
        .update(payload)
        .digest('hex');

      // Normalize expected value (could be hex or base64)
      const expectedValue = evidence.value;

      // If the expected value looks like base64, compute base64 comparison
      if (this._isBase64(expectedValue)) {
        const actualBase64 = createHash(evidence.algorithm)
          .update(payload)
          .digest('base64');

        if (actualBase64 === expectedValue) {
          return {
            verified: true,
            evidenceType: 'checksum',
            reasonCode: IntegrityReasonCode.Verified,
            message: 'Checksum verification succeeded.',
          };
        }

        return {
          verified: false,
          evidenceType: 'checksum',
          reasonCode: IntegrityReasonCode.ChecksumMismatch,
          message: 'Checksum mismatch (base64 comparison).',
        };
      }

      // Hex comparison (case-insensitive)
      if (actualHash.toLowerCase() === expectedValue.toLowerCase()) {
        return {
          verified: true,
          evidenceType: 'checksum',
          reasonCode: IntegrityReasonCode.Verified,
          message: 'Checksum verification succeeded.',
        };
      }

      return {
        verified: false,
        evidenceType: 'checksum',
        reasonCode: IntegrityReasonCode.ChecksumMismatch,
        message: `Checksum mismatch. Expected: ${expectedValue}, Actual: ${actualHash}.`,
      };
    } catch (error) {
      return {
        verified: false,
        evidenceType: 'checksum',
        reasonCode: IntegrityReasonCode.InvalidChecksumFormat,
        message: `Failed to verify checksum: ${error instanceof Error ? error.message : 'unknown error'}.`,
      };
    }
  }

  /**
   * Verify signature evidence against payload.
   */
  private _verifySignature(
    payload: Buffer,
    evidence: ISignatureEvidence,
  ): IIntegrityVerificationResult {
    if (!evidence.publicKey) {
      return {
        verified: false,
        evidenceType: 'signature',
        reasonCode: IntegrityReasonCode.MissingPublicKey,
        message: 'Signature verification requires a public key.',
      };
    }

    const nodeAlgorithm = this._mapSignatureAlgorithm(evidence.algorithm);

    if (!nodeAlgorithm) {
      return {
        verified: false,
        evidenceType: 'signature',
        reasonCode: IntegrityReasonCode.UnsupportedSignatureAlgorithm,
        message: `Unsupported signature algorithm: ${evidence.algorithm}. Supported: ${this._supportedSignatureAlgorithms.join(', ')}.`,
      };
    }

    try {
      const verifier = createVerify(nodeAlgorithm);

      verifier.update(payload);
      verifier.end();

      const signatureBuffer = Buffer.from(evidence.value, 'base64');
      const verified = verifier.verify(evidence.publicKey, signatureBuffer);

      if (verified) {
        return {
          verified: true,
          evidenceType: 'signature',
          reasonCode: IntegrityReasonCode.Verified,
          message: 'Signature verification succeeded.',
        };
      }

      return {
        verified: false,
        evidenceType: 'signature',
        reasonCode: IntegrityReasonCode.SignatureVerificationFailed,
        message:
          'Signature verification failed. The signature does not match the payload.',
      };
    } catch (error) {
      return {
        verified: false,
        evidenceType: 'signature',
        reasonCode: IntegrityReasonCode.InvalidSignatureFormat,
        message: `Failed to verify signature: ${error instanceof Error ? error.message : 'unknown error'}.`,
      };
    }
  }

  /**
   * Map signature algorithm to Node.js crypto algorithm string.
   */
  private _mapSignatureAlgorithm(
    algorithm: SignatureAlgorithmType,
  ): string | null {
    const mapping: Record<SignatureAlgorithmType, string> = {
      'rsa-sha256': 'RSA-SHA256',
      'rsa-sha512': 'RSA-SHA512',
      'ecdsa-sha256': 'ecdsa-sha256',
    };

    return mapping[algorithm] ?? null;
  }

  /**
   * Check if a string looks like base64 encoding (not hex).
   * Hex strings are only [a-fA-F0-9], base64 contains +/= and uppercase letters.
   */
  private _isBase64(value: string): boolean {
    // If it's a valid hex string (only hex chars), it's not base64
    if (/^[a-fA-F0-9]+$/.test(value)) {
      return false;
    }

    // Base64 pattern (including npm integrity format with -)
    const base64Pattern = /^[A-Za-z0-9+/=-]+$/;
    return base64Pattern.test(value) && value.length % 4 === 0;
  }
}
