import { describe, expect, it } from 'vitest';
import type { IModuleArtifactHttpClient } from '@/modularity/index.js';
import { UrlSource } from '@/modularity/index.js';
import { RuntimeErrorCodes } from '@/common/index.js';

describe('UrlSource', () => {
  it('rejects non-https URL descriptors', async () => {
    const source = new UrlSource({
      type: 'url',
      moduleIdHint: 'module-url',
      url: 'http://example.invalid/module.zip',
    });

    const result = await source.load();

    expect('reasonCode' in result).toBe(true);

    if ('reasonCode' in result) {
      expect(result.reasonCode).toBe(RuntimeErrorCodes.SourceUrlInvalid);
      expect(result.phase).toBe('discover');
    }
  });

  it('rejects on fetch failure for unreachable URL', async () => {
    const httpClient: IModuleArtifactHttpClient = {
      fetch: async () => {
        throw new Error('Network unavailable');
      },
    };
    const source = new UrlSource(
      {
        type: 'url',
        moduleIdHint: 'module-url',
        url: 'https://example.invalid/module.zip',
      },
      httpClient,
    );

    const result = await source.load();

    expect('reasonCode' in result).toBe(true);

    if ('reasonCode' in result) {
      expect(result.reasonCode).toBe(RuntimeErrorCodes.SourceFetchFailed);
      expect(result.phase).toBe('discover');
    }
  });
});
