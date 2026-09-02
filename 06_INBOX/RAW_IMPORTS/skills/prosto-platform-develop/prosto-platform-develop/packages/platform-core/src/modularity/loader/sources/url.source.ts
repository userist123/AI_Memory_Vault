import {
  ArtifactCacheKeyGenerator,
  type IArtifactCache,
  type IArtifactCacheEntryMetadata,
  NoOpArtifactCache,
} from '@/caching/index.js';
import { RuntimeErrorCodes } from '@/common/index.js';
import { createHash } from 'node:crypto';
import { writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import {
  ModuleArtifactPackaging,
  ModuleArtifactSource,
  ModuleState,
} from '../constants/index.js';
import type {
  ArtifactSourceValidationResultType,
  IModuleCandidateArtifact,
  IModuleUrlArtifactSource,
  IRejectedModuleArtifact,
} from '../interfaces/index.js';
import {
  ArtifactExtractor,
  ArtifactFetcher,
  cleanupTempDir,
  createTempDir,
  DynamicModuleLoader,
  type IModuleArtifactHttpClient,
} from '../utils/index.js';
import { ArtifactBaseSource } from './artifact.base-source.js';

/**
 * @alpha
 * HTTPS URL-based artifact source.
 */
export class UrlSource extends ArtifactBaseSource {
  constructor(
    private readonly _descriptor: IModuleUrlArtifactSource,
    private readonly _httpClient: IModuleArtifactHttpClient = new ArtifactFetcher(),
    private readonly _cache: IArtifactCache = new NoOpArtifactCache(),
  ) {
    super(ModuleArtifactSource.Url);
  }

  override validate(): ArtifactSourceValidationResultType {
    if (!this._descriptor.url.trim()) {
      return {
        ok: false,
        error: {
          reasonCode: RuntimeErrorCodes.SourceDescriptorInvalid,
          message: 'Artifact URL source descriptor is empty.',
          remediationHint: 'Provide non-empty HTTPS URL for url source.',
        },
      };
    }

    const isHttps = this._descriptor.url.startsWith('https://');

    if (!isHttps) {
      return {
        ok: false,
        error: {
          reasonCode: RuntimeErrorCodes.SourceUrlInvalid,
          message: 'Artifact URL must use HTTPS.',
          remediationHint: 'Use HTTPS artifact URL in source descriptor.',
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
      const safeUrl = this._redactUrl(this._descriptor.url);

      const candidateArtifact: IModuleCandidateArtifact = {
        moduleId: manifest.id,
        moduleVersion: manifest.version,
        moduleEnvelope: {
          module,
          manifest,
          fullPhysicalPath: extractionResult.extractPath,
          state: ModuleState.ReadyForInitialization,
        },
        orderingKey: `url:${safeUrl}`,
        sourceType: ModuleArtifactSource.Url,
        sourceRef: safeUrl,
        packaging: extractionResult.packaging,
      };

      return candidateArtifact;
    } catch (error) {
      return this.createRejected('discover', {
        reasonCode: RuntimeErrorCodes.SourceEntryResolveFailed,
        message: `Failed to load URL artifact: ${error instanceof Error ? error.message : 'unknown'}`,
        remediationHint: 'Ensure URL points to a valid module artifact.',
      });
    } finally {
      await cleanupTempDir(extractionResult.tempDir);
    }
  }

  protected override getModuleIdHint(): string | undefined {
    return this._descriptor.moduleIdHint;
  }

  protected override getSourceRef(): string {
    return this._descriptor.url;
  }

  private async _fetchArtifact(): Promise<Buffer> {
    return this._httpClient.fetch(this._descriptor.url);
  }

  private _redactUrl(url: string): string {
    try {
      const parsed = new URL(url);
      const sensitiveParams = [
        'token',
        'key',
        'secret',
        'auth',
        'password',
        'api_key',
      ];

      for (const param of sensitiveParams) {
        parsed.searchParams.delete(param);
      }

      return parsed.toString();
    } catch {
      return '[invalid-url]';
    }
  }

  private _buildCacheMetadata(payload: Buffer): IArtifactCacheEntryMetadata {
    return {
      sourceRef: this._redactUrl(this._descriptor.url),
      sourceType: ModuleArtifactSource.Url,
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
    const cacheKey = ArtifactCacheKeyGenerator.forUrl(this._descriptor);
    const cached = await this._cache.get(cacheKey);
    let payload: Buffer;

    if (cached) {
      payload = cached;
    } else {
      try {
        payload = await this._fetchArtifact();
        await this._cache.set(
          cacheKey,
          payload,
          this._buildCacheMetadata(payload),
        );
      } catch (error) {
        return {
          error: {
            reasonCode: RuntimeErrorCodes.SourceFetchFailed,
            message: `Failed to fetch artifact from URL "${this._descriptor.url}": ${error instanceof Error ? error.message : 'unknown'}`,
            remediationHint:
              'Ensure URL is accessible and runtime has network permissions.',
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
          message: 'Unsupported checksum format for URL source.',
          remediationHint: 'Use sha256:<hex> or <hex> checksum format.',
        },
      };
    }

    if (parsed.algorithm !== 'sha256') {
      return {
        ok: false,
        error: {
          reasonCode: RuntimeErrorCodes.SourceIntegrityMismatch,
          message: `Unsupported checksum algorithm "${parsed.algorithm}" for URL source.`,
          remediationHint: 'Use sha256 checksum for URL source.',
        },
      };
    }

    const actual = createHash('sha256').update(payload).digest('hex');

    if (actual !== parsed.value) {
      return {
        ok: false,
        error: {
          reasonCode: RuntimeErrorCodes.SourceIntegrityMismatch,
          message: 'URL source checksum mismatch.',
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
    const tempDir = await createTempDir('prosto-url');
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
              message: `Unsupported packaging format "${packaging}" for URL source.`,
              remediationHint: 'Use zip or tgz packaging for URL artifacts.',
            },
          };
      }
    } catch (error) {
      return {
        error: {
          reasonCode: RuntimeErrorCodes.SourceExtractionFailed,
          message: `Failed to extract artifact from URL "${this._descriptor.url}": ${
            error instanceof Error ? error.message : 'unknown'
          }`,
          remediationHint:
            'Ensure artifact packaging is correct and supported by the platform.',
        },
      };
    }

    return { packaging, tempDir, extractPath };
  }
}
