import type {
  ArtifactSourceValidationResultType,
  IModuleCandidateArtifact,
  IModulePathArtifactSource,
  IRejectedModuleArtifact,
} from '../interfaces/index.js';
import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import {
  ArtifactCacheKeyGenerator,
  type IArtifactCache,
  type IArtifactCacheEntryMetadata,
  NoOpArtifactCache,
} from '@/caching/index.js';
import { RuntimeErrorCodes } from '@/common/index.js';
import {
  ModuleArtifactPackaging,
  ModuleArtifactSource,
  ModuleState,
} from '../constants/index.js';
import {
  ArtifactExtractor,
  cleanupTempDir,
  createTempDir,
  DynamicModuleLoader,
} from '../utils/index.js';
import { ArtifactBaseSource } from './artifact.base-source.js';

/**
 * @alpha
 * File system path-based artifact source.
 */
export class PathSource extends ArtifactBaseSource {
  constructor(
    private readonly _descriptor: IModulePathArtifactSource,
    private readonly _cache: IArtifactCache = new NoOpArtifactCache(),
  ) {
    super(ModuleArtifactSource.Path);
  }

  override validate(): ArtifactSourceValidationResultType {
    if (!this._descriptor.path.trim()) {
      return {
        ok: false,
        error: {
          reasonCode: RuntimeErrorCodes.SourceDescriptorInvalid,
          message: 'Artifact path source descriptor is empty.',
          remediationHint: 'Provide non-empty filesystem path for path source.',
        },
      };
    }

    return { ok: true };
  }

  override async load(): Promise<
    IModuleCandidateArtifact | IRejectedModuleArtifact
  > {
    const validation = this.validate();

    if (!validation.ok) {
      return this.createRejected('discover', validation.error);
    }

    const artifact = await this._getArtifact();

    if ('error' in artifact) {
      return this.createRejected('discover', artifact.error);
    }

    const expectedChecksum = this._descriptor.integrity?.checksum;

    if (expectedChecksum) {
      const checksumResult = this._verifyChecksum(artifact, expectedChecksum);

      if (!checksumResult.ok) {
        return this.createRejected('validate', checksumResult.error);
      }
    }

    const extractionResult = await this._extract(artifact);

    if ('error' in extractionResult) {
      return this.createRejected('discover', extractionResult.error);
    }

    try {
      const manifestPath = await this.resolveManifestPath(
        extractionResult.extractPath,
      );
      const entryPath = await this.resolveEntryPath(
        extractionResult.extractPath,
      );
      const [manifest, module] = await Promise.all([
        await DynamicModuleLoader.loadModuleManifest(manifestPath),
        await DynamicModuleLoader.loadModuleEntry(entryPath),
      ]);

      const candidateArtifact: IModuleCandidateArtifact = {
        moduleId: manifest.id,
        moduleVersion: manifest.version,
        moduleEnvelope: {
          module,
          manifest,
          fullPhysicalPath: extractionResult.extractPath,
          state: ModuleState.ReadyForInitialization,
        },
        orderingKey: `path:${this._descriptor.path}`,
        sourceType: ModuleArtifactSource.Path,
        sourceRef: this._descriptor.path,
        packaging: extractionResult.packaging,
      };

      return candidateArtifact;
    } catch (error) {
      return this.createRejected('discover', {
        reasonCode: RuntimeErrorCodes.SourceEntryResolveFailed,
        message: `Failed to resolve module entry: ${error instanceof Error ? error.message : 'unknown'}`,
        remediationHint: 'Ensure artifact contains a valid module entry point.',
      });
    } finally {
      await cleanupTempDir(extractionResult.tempDir);
    }
  }

  protected override getModuleIdHint(): string | undefined {
    return this._descriptor.moduleIdHint;
  }

  protected override getSourceRef(): string {
    return this._descriptor.path;
  }

  private _buildCacheMetadata(payload: Buffer): IArtifactCacheEntryMetadata {
    return {
      sourceRef: this._descriptor.path,
      sourceType: ModuleArtifactSource.Path,
      timestamp: Date.now(),
      size: payload.length,
      checksum: this._descriptor.integrity?.checksum ?? '',
    };
  }

  private async _getArtifact(): Promise<
    | Buffer
    | {
        error: {
          reasonCode: RuntimeErrorCodes;
          message: string;
          remediationHint: string;
        };
      }
  > {
    const cacheKey = ArtifactCacheKeyGenerator.forPath(this._descriptor);
    const cached = await this._cache.get(cacheKey);
    let payload: Buffer;

    if (cached) {
      payload = cached;
    } else {
      try {
        payload = await readFile(this._descriptor.path);
        await this._cache.set(
          cacheKey,
          payload,
          this._buildCacheMetadata(payload),
        );
      } catch (error) {
        return {
          error: {
            reasonCode: RuntimeErrorCodes.SourceFetchFailed,
            message: `Failed to read artifact from path "${this._descriptor.path}": ${
              error instanceof Error ? error.message : 'unknown'
            }`,
            remediationHint:
              'Ensure the specified path exists and is readable by the platform.',
          },
        };
      }
    }

    return payload;
  }

  private _verifyChecksum(
    payload: Buffer,
    expectedChecksum: string,
  ):
    | { ok: true }
    | {
        ok: false;
        error: {
          reasonCode: RuntimeErrorCodes;
          message: string;
          remediationHint: string;
        };
      } {
    const parsed = this.parseChecksum(expectedChecksum);

    if (!parsed) {
      return {
        ok: false,
        error: {
          reasonCode: RuntimeErrorCodes.SourceIntegrityMismatch,
          message: 'Unsupported checksum format for path source.',
          remediationHint: 'Use sha256:<hex> or <hex> checksum format.',
        },
      };
    }

    if (parsed.algorithm !== 'sha256') {
      return {
        ok: false,
        error: {
          reasonCode: RuntimeErrorCodes.SourceIntegrityMismatch,
          message: `Unsupported checksum algorithm "${parsed.algorithm}" for path source.`,
          remediationHint: 'Use sha256 checksum for path source.',
        },
      };
    }

    const actual = createHash('sha256').update(payload).digest('hex');

    if (actual !== parsed.value) {
      return {
        ok: false,
        error: {
          reasonCode: RuntimeErrorCodes.SourceIntegrityMismatch,
          message: 'Path source checksum mismatch.',
          remediationHint:
            'Update checksum metadata or artifact payload to match expected integrity.',
        },
      };
    }

    return { ok: true };
  }

  private async _extract(artifact: Buffer): Promise<
    | {
        packaging: `${ModuleArtifactPackaging}`;
        tempDir: string;
        extractPath: string;
      }
    | {
        error: {
          reasonCode: RuntimeErrorCodes;
          message: string;
          remediationHint: string;
        };
      }
  > {
    const packaging = this._descriptor.packaging ?? ModuleArtifactPackaging.Zip;
    const tempDir = await createTempDir('prosto-path');
    const tempFilePath = join(tempDir, `artifact.${packaging}`);
    const extractPath = join(tempDir, 'extracted');

    try {
      await writeFile(tempFilePath, artifact);

      switch (packaging) {
        case ModuleArtifactPackaging.Zip:
          await ArtifactExtractor.extractZip(tempFilePath, extractPath);
          break;

        case ModuleArtifactPackaging.Tgz:
          await ArtifactExtractor.extractTgz(tempFilePath, extractPath);
          break;

        default:
          return {
            error: {
              reasonCode: RuntimeErrorCodes.SourceExtractionFailed,
              message: `Unsupported packaging format "${packaging}" for path source.`,
              remediationHint: 'Use zip or tgz packaging for path artifacts.',
            },
          };
      }
    } catch (error) {
      return {
        error: {
          reasonCode: RuntimeErrorCodes.SourceExtractionFailed,
          message: `Failed to extract artifact from path source "${this._descriptor.path}": ${
            error instanceof Error ? error.message : 'unknown'
          }`,
          remediationHint:
            'Ensure the artifact is a valid archive and not corrupted.',
        },
      };
    }

    return { packaging, tempDir, extractPath };
  }
}
