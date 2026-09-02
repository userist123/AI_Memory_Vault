import { describe, expect, it } from 'vitest';
import {
  ManifestValidationStrategy,
  ModuleArtifactPackaging,
  ModuleArtifactSource,
  ModuleState,
} from '@/modularity/index.js';
import { createManifest, TestModule } from '@/tests/fixtures/index.js';

describe('ManifestValidationStrategy', () => {
  it('passes valid manifest', () => {
    const strategy = new ManifestValidationStrategy();
    const manifest = createManifest({ id: 'module-a' });
    const module = new TestModule();

    const result = strategy.validate({
      artifact: {
        moduleId: manifest.id,
        moduleVersion: manifest.version,
        moduleEnvelope: {
          manifest,
          module,
          fullPhysicalPath: '',
          state: ModuleState.ReadyForInitialization,
        },
        orderingKey: 'memory:module-a@1.0.0',
        sourceType: ModuleArtifactSource.Memory,
        sourceRef: 'memory:module-a@1.0.0',
        packaging: ModuleArtifactPackaging.Esm,
      },
      runtimeVersion: {
        sdkVersion: '0.0.0',
        nodeVersion: process.versions.node,
      },
    });

    expect(result).toEqual({ ok: true });
  });

  it('fails invalid manifest', () => {
    const strategy = new ManifestValidationStrategy();
    const manifest = createManifest({ id: 'INVALID_ID' });
    const module = new TestModule();

    const result = strategy.validate({
      artifact: {
        moduleId: manifest.id,
        moduleVersion: manifest.version,
        moduleEnvelope: {
          manifest,
          module,
          fullPhysicalPath: '',
          state: ModuleState.ReadyForInitialization,
        },
        orderingKey: 'memory:INVALID_ID@1.0.0',
        sourceType: ModuleArtifactSource.Memory,
        sourceRef: 'memory:INVALID_ID@1.0.0',
        packaging: ModuleArtifactPackaging.Esm,
      },
      runtimeVersion: {
        sdkVersion: '0.0.0',
        nodeVersion: process.versions.node,
      },
    });

    expect('error' in result).toBe(true);
  });
});
