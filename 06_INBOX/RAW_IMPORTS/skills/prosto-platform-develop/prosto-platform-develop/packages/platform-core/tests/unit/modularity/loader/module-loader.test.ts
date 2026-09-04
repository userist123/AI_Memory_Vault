import { describe, expect, it } from 'vitest';
import type {
  IModuleArtifactHttpClient,
  IArtifactSourceFactory,
} from '@/modularity/index.js';
import { ArtifactSourceFactory, ModuleLoader } from '@/modularity/index.js';
import { RuntimeErrorCodes } from '@/common/index.js';
import { createManifest, TestModule } from '@/tests/fixtures/index.js';

describe('ModuleLoader', () => {
  const httpClient: IModuleArtifactHttpClient = {
    fetch: async () => {
      throw new Error('Network unavailable');
    },
  };
  const artifactSourceFactory: IArtifactSourceFactory =
    new ArtifactSourceFactory(httpClient);
  const loader = new ModuleLoader(artifactSourceFactory);

  it('loads memory candidates', async () => {
    const result = await loader.load([
      {
        type: 'memory',
        manifest: createManifest({ id: 'module-a' }),
        module: new TestModule(),
      },
    ]);

    expect(result.loaded).toHaveLength(1);
    expect(result.rejected).toEqual([]);
    expect(result.loaded[0]?.moduleId).toBe('module-a');
  });

  it('rejects url candidates on fetch failure', async () => {
    const result = await loader.load([
      {
        type: 'url',
        moduleIdHint: 'module-url',
        url: 'https://example.invalid/module.zip',
      },
    ]);

    expect(result.loaded).toEqual([]);
    expect(result.rejected).toHaveLength(1);
    expect(result.rejected[0]?.reasonCode).toBe(
      RuntimeErrorCodes.SourceFetchFailed,
    );
    expect(result.rejected[0]?.phase).toBe('discover');
  });

  it('rejects insecure url source at discover phase', async () => {
    const result = await loader.load([
      {
        type: 'url',
        moduleIdHint: 'module-url',
        url: 'http://example.invalid/module.zip',
      },
    ]);

    expect(result.loaded).toEqual([]);
    expect(result.rejected).toHaveLength(1);
    expect(result.rejected[0]?.reasonCode).toBe(
      RuntimeErrorCodes.SourceUrlInvalid,
    );
    expect(result.rejected[0]?.phase).toBe('discover');
  });
});
