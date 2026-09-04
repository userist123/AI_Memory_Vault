import { describe, expect, it } from 'vitest';
import {
  PlatformModuleCompatibilityValidationError,
  type IPlatformModuleManifest,
  PlatformModuleCompatibilityValidator,
  PlatformModuleManifestValidator,
} from '@/index.js';

const validManifest: IPlatformModuleManifest = {
  id: 'module-health',
  version: '1.2.3',
  sdkVersion: '^0.1.0',
  title: 'Health Module',
  dependencies: [{ id: 'module-auth', version: '^1.0.0' }],
};

describe('compatibility validation', () => {
  const manifestValidator = new PlatformModuleManifestValidator();
  const compatibilityValidator = new PlatformModuleCompatibilityValidator();

  it('returns compatible for matching ranges', () => {
    const manifest = manifestValidator.parse(validManifest);
    const result = compatibilityValidator.validate(manifest, {
      sdkVersion: '0.1.5',
    });

    expect(result.compatible).toBe(true);
  });

  it('returns mismatch details for incompatible SDK version', () => {
    const manifest = manifestValidator.parse(validManifest);
    const result = compatibilityValidator.validate(manifest, {
      sdkVersion: '0.2.0',
    });

    expect(result.compatible).toBe(false);

    if (result.compatible) {
      throw new Error('Expected compatibility mismatch.');
    }

    expect(result.issues[0]?.field).toBe('sdkVersion');
    expect(result.issues[0]?.code).toBe('VERSION_RANGE_MISMATCH');
  });

  it('throws CompatibilityValidationError on mismatch', () => {
    const manifest = manifestValidator.parse(validManifest);

    expect(() =>
      compatibilityValidator.assert(manifest, {
        sdkVersion: '1.2.0',
      }),
    ).toThrow(PlatformModuleCompatibilityValidationError);
  });
});
