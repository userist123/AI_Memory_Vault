import type { RuntimeErrorCodes } from '@/common/index.js';
import type { ModuleArtifactSource } from '../constants/index.js';
import type {
  ArtifactSourceValidationResultType,
  IArtifactSource,
  IModuleCandidateArtifact,
  IRejectedModuleArtifact,
} from '../interfaces/index.js';
import { readFile } from 'node:fs/promises';
import { join } from 'node:path';
import { fileExists } from '../utils/index.js';

/**
 * @alpha
 * Abstract base class for artifact sources.
 */
export abstract class ArtifactBaseSource implements IArtifactSource {
  constructor(private readonly _sourceType: ModuleArtifactSource) {}

  /**
   * Source type identifier (public for interface compliance).
   */
  get type(): ModuleArtifactSource {
    return this._sourceType;
  }

  /**
   * Validate source configuration before loading.
   */
  abstract validate(): ArtifactSourceValidationResultType;

  /**
   * Load module artifact from the source.
   */
  abstract load(): Promise<IModuleCandidateArtifact | IRejectedModuleArtifact>;

  /**
   * Get module ID hint from source.
   */
  protected abstract getModuleIdHint(): string | undefined;

  /**
   * Get a source reference string.
   */
  protected abstract getSourceRef(): string;

  /**
   * Helper to create a rejected artifact with common fields.
   */
  protected createRejected(
    phase: IRejectedModuleArtifact['phase'],
    details: {
      reasonCode: RuntimeErrorCodes;
      message: string;
      remediationHint: string;
    },
  ): IRejectedModuleArtifact {
    return {
      phase,
      moduleId: this.getModuleIdHint() || 'unknown',
      sourceType: this._sourceType,
      sourceRef: this.getSourceRef(),
      reasonCode: details.reasonCode,
      message: details.message,
      remediationHint: details.remediationHint,
    };
  }

  protected parseChecksum(input: string): {
    algorithm: string;
    value: string;
  } | null {
    const normalized = input.trim();

    if (!normalized) return null;

    if (normalized.startsWith('sha512-')) {
      return { algorithm: 'sha512', value: normalized };
    }

    const [algorithm, value] = normalized.split(':');

    if (algorithm && value) {
      return {
        algorithm: algorithm.toLowerCase(),
        value: value.toLowerCase(),
      };
    }

    if (/^[0-9a-fA-F]{64}$/.test(normalized)) {
      return {
        algorithm: 'sha256',
        value: normalized.toLowerCase(),
      };
    }

    return null;
  }

  protected async resolveManifestPath(packageDir: string): Promise<string> {
    for (const candidate of ['manifest.json', 'dist/manifest.json']) {
      const candidatePath = join(packageDir, candidate);

      if (await fileExists(candidatePath)) {
        return candidatePath;
      }
    }

    throw new Error('Manifest was not found in artifact');
  }

  protected async resolveEntryPath(packageDir: string): Promise<string> {
    const pkgPath = join(packageDir, 'package.json');

    if (await fileExists(pkgPath)) {
      const pkg = JSON.parse(await readFile(pkgPath, 'utf8'));

      if (pkg.exports) {
        let entry = pkg.exports['./platform'];

        if (!entry) {
          entry =
            typeof pkg.exports === 'string'
              ? pkg.exports
              : (pkg.exports['.']?.import ?? pkg.exports['.']?.default);
        }

        if (entry) {
          return join(packageDir, entry);
        }
      }

      if (pkg.main) {
        return join(packageDir, pkg.main);
      }
    }

    for (const candidate of [
      'dist/platform/platform.module.js',
      'dist/platform/index.js',
      'dist/index.js',
      'index.js',
    ]) {
      const candidatePath = join(packageDir, candidate);

      if (await fileExists(candidatePath)) {
        return candidatePath;
      }
    }

    throw new Error('No entry point found in artifact');
  }
}
