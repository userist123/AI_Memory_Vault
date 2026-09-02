import { describe, it, expect } from 'vitest';
import { IntegrityVerifier, IntegrityReasonCode } from '@/index.js';

describe('IntegrityVerifier', () => {
  describe('parseChecksum', () => {
    it('should parse npm-style sha256 integrity string', () => {
      const verifier = new IntegrityVerifier();
      const result = verifier.parseChecksum('sha256-abc123==');

      expect(result).not.toBeNull();
      expect(result?.algorithm).toBe('sha256');
      expect(result?.value).toBe('abc123==');
    });

    it('should parse colon-separated checksum', () => {
      const verifier = new IntegrityVerifier();
      const result = verifier.parseChecksum('sha256:abc123');

      expect(result).not.toBeNull();
      expect(result?.algorithm).toBe('sha256');
      expect(result?.value).toBe('abc123');
    });

    it('should parse plain hex checksum (64 chars for sha256)', () => {
      const verifier = new IntegrityVerifier();
      const hex64 = 'a'.repeat(64);
      const result = verifier.parseChecksum(hex64);

      expect(result).not.toBeNull();
      expect(result?.algorithm).toBe('sha256');
      expect(result?.value).toBe(hex64);
    });

    it('should parse plain hex checksum (128 chars for sha512)', () => {
      const verifier = new IntegrityVerifier();
      const hex128 = 'b'.repeat(128);
      const result = verifier.parseChecksum(hex128);

      expect(result).not.toBeNull();
      expect(result?.algorithm).toBe('sha512');
      expect(result?.value).toBe(hex128);
    });

    it('should return null for invalid checksum format', () => {
      const verifier = new IntegrityVerifier();
      const result = verifier.parseChecksum('invalid');

      expect(result).toBeNull();
    });

    it('should return null for empty string', () => {
      const verifier = new IntegrityVerifier();
      const result = verifier.parseChecksum('');

      expect(result).toBeNull();
    });
  });

  describe('computeChecksum', () => {
    it('should compute sha256 checksum in hex', () => {
      const verifier = new IntegrityVerifier();
      const payload = Buffer.from('test payload');
      const checksum = verifier.computeChecksum(payload, 'sha256', 'hex');

      expect(checksum).toHaveLength(64);
      expect(/^[a-f0-9]+$/.test(checksum)).toBe(true);
    });

    it('should compute sha256 checksum in base64', () => {
      const verifier = new IntegrityVerifier();
      const payload = Buffer.from('test payload');
      const checksum = verifier.computeChecksum(payload, 'sha256', 'base64');

      expect(checksum).toBeTruthy();
    });

    it('should produce consistent results', () => {
      const verifier = new IntegrityVerifier();
      const payload = Buffer.from('consistent test');

      const checksum1 = verifier.computeChecksum(payload, 'sha256', 'hex');
      const checksum2 = verifier.computeChecksum(payload, 'sha256', 'hex');

      expect(checksum1).toBe(checksum2);
    });
  });

  describe('computeIntegrityString', () => {
    it('should produce npm-style integrity string', () => {
      const verifier = new IntegrityVerifier();
      const payload = Buffer.from('test payload');
      const integrity = verifier.computeIntegrityString(payload, 'sha256');

      expect(integrity).toMatch(/^sha256-/);
    });
  });

  describe('verify', () => {
    it('should verify matching checksum', () => {
      const verifier = new IntegrityVerifier();
      const payload = Buffer.from('test payload');
      // Compute checksum and use it directly
      const checksum = verifier.computeChecksum(payload, 'sha256', 'hex');

      const result = verifier.verify(payload, {
        type: 'checksum',
        algorithm: 'sha256',
        value: checksum,
      });

      expect(result.verified).toBe(true);
      expect(result.reasonCode).toBe(IntegrityReasonCode.Verified);
    });

    it('should reject mismatching checksum', () => {
      const verifier = new IntegrityVerifier();
      const payload = Buffer.from('test payload');

      const result = verifier.verify(payload, {
        type: 'checksum',
        algorithm: 'sha256',
        value:
          '0000000000000000000000000000000000000000000000000000000000000000',
      });

      expect(result.verified).toBe(false);
      expect(result.reasonCode).toBe(IntegrityReasonCode.ChecksumMismatch);
    });

    it('should reject unsupported algorithm', () => {
      const verifier = new IntegrityVerifier();
      const payload = Buffer.from('test payload');

      const result = verifier.verify(payload, {
        type: 'checksum',
        algorithm: 'md5' as 'sha256',
        value: 'abc123',
      });

      expect(result.verified).toBe(false);
      expect(result.reasonCode).toBe(
        IntegrityReasonCode.UnsupportedChecksumAlgorithm,
      );
    });

    it('should reject empty payload', () => {
      const verifier = new IntegrityVerifier();
      const payload = Buffer.alloc(0);

      const result = verifier.verify(payload, {
        type: 'checksum',
        algorithm: 'sha256',
        value: 'abc123',
      });

      expect(result.verified).toBe(false);
      expect(result.reasonCode).toBe(IntegrityReasonCode.EmptyPayload);
    });

    it('should verify base64 checksum', () => {
      const verifier = new IntegrityVerifier();
      const payload = Buffer.from('test payload');
      const checksum = verifier.computeChecksum(payload, 'sha256', 'base64');

      const result = verifier.verify(payload, {
        type: 'checksum',
        algorithm: 'sha256',
        value: checksum,
      });

      expect(result.verified).toBe(true);
    });
  });
});
