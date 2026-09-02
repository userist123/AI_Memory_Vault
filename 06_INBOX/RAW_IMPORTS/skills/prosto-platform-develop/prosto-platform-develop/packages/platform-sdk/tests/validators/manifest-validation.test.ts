import { describe, expect, it } from 'vitest';
import {
  type IPlatformModuleManifest,
  PlatformModuleManifestValidationError,
  PlatformModuleManifestValidator,
} from '@/index.js';

const validManifest: IPlatformModuleManifest = {
  id: 'module-health',
  version: '1.2.3',
  sdkVersion: '^0.1.0',
  title: 'Health Module',
  dependencies: [{ id: 'module-auth', version: '^1.0.0' }],
  optional: true,
  groups: ['Group 1'],
};

describe('manifest validation', () => {
  const manifestValidator = new PlatformModuleManifestValidator();

  it('accepts a valid manifest', () => {
    const parsedManifest = manifestValidator.parse(validManifest);

    expect(parsedManifest.id).toBe(validManifest.id);
    expect(parsedManifest.version).toBe(validManifest.version);
  });

  it('returns failure for schema violations', () => {
    const result = manifestValidator.validate({
      ...validManifest,
      groups: 'Group 1',
    });

    expect(result.success).toBe(false);

    if (result.success) {
      throw new Error('Expected validation failure.');
    }

    expect(result.error).toBeInstanceOf(PlatformModuleManifestValidationError);
    expect(result.error.issues.some((issue) => issue.path === 'groups')).toBe(
      true,
    );
  });

  it('returns failure for duplicate groups', () => {
    const result = manifestValidator.validate({
      ...validManifest,
      groups: ['health', 'health'],
    });

    expect(result.success).toBe(false);

    if (result.success) {
      throw new Error('Expected validation failure.');
    }

    expect(
      result.error.issues.some((issue) => issue.code === 'duplicate_group'),
    ).toBe(true);
  });
});
