import { describe, test, expect } from 'vitest';
import {
  sanitizeImportFilename,
  validateImportSize,
  detectMagicMime,
  validateImportMime,
  assertSafeDecompressionRatio,
  SecureImportError,
  DEFAULT_IMPORT_LIMITS,
} from '../lib/secure-import';

describe('Secure Import Validator (Section 17.1)', () => {
  test('sanitizeImportFilename blocks path traversal sequences', () => {
    expect(() => sanitizeImportFilename('../../etc/passwd')).toThrow(SecureImportError);
    expect(() => sanitizeImportFilename('..\\windows\\system32')).toThrow(SecureImportError);
    expect(() => sanitizeImportFilename('/root/plan.pdf')).toThrow(SecureImportError);
    expect(sanitizeImportFilename('plan-apartament-etaj-1.pdf')).toBe('plan-apartament-etaj-1.pdf');
    expect(sanitizeImportFilename('plan cu spații și (caractere) speciale!.png')).toBe('plan_cu_spatii_si_caractere_speciale.png');
  });

  test('validateImportSize enforces byte limits', () => {
    expect(() => validateImportSize(500 * 1024, 1024 * 1024)).not.toThrow();
    expect(() => validateImportSize(2 * 1024 * 1024, 1024 * 1024)).toThrow(SecureImportError);
  });

  test('detectMagicMime identifies authentic file headers', () => {
    const png = new Uint8Array([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A]);
    const pdf = new Uint8Array([0x25, 0x50, 0x44, 0x46, 0x2D, 0x31]);
    const zip = new Uint8Array([0x50, 0x4B, 0x03, 0x04]);
    const glb = new Uint8Array([0x67, 0x6C, 0x54, 0x46]);

    expect(detectMagicMime(png)).toBe('image/png');
    expect(detectMagicMime(pdf)).toBe('application/pdf');
    expect(detectMagicMime(zip)).toBe('application/zip');
    expect(detectMagicMime(glb)).toBe('model/gltf-binary');
    expect(detectMagicMime(new Uint8Array([0x00, 0x00, 0x00, 0x00]))).toBeNull();
  });

  test('validateImportMime filters disallowed MIME types and spoofs', () => {
    const png = new Uint8Array([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A]);
    expect(validateImportMime(png, 'image/png', ['image/png', 'image/jpeg', 'application/pdf'])).toBe('image/png');
    expect(() => validateImportMime(png, 'image/png', ['application/pdf'])).toThrow(SecureImportError);
  });

  test('assertSafeDecompressionRatio protects against decompression bombs', () => {
    expect(() => assertSafeDecompressionRatio(1024, 5 * 1024, 10)).not.toThrow();
    // 1 KB compressed expanding to 20 MB is a 20000:1 ratio bomb
    expect(() => assertSafeDecompressionRatio(1024, 20 * 1024 * 1024, 10)).toThrow(SecureImportError);
  });
});
