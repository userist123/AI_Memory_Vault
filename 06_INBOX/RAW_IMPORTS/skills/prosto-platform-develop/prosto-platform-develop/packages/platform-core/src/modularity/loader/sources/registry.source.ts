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
  IModuleRegistryArtifactSource,
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

interface INpmPackageMetadata {
  readonly name: string;
  readonly versions: Record<
    string,
    {
      readonly dist: {
        readonly tarball: string;
        readonly integrity?: string;
        readonly shasum?: string;
      };
    }
  >;
}

/**
 * @alpha
 * Package registry-based artifact source (npm, etc.).
 */
export class RegistrySource extends ArtifactBaseSource {
  constructor(
    private readonly _descriptor: IModuleRegistryArtifactSource,
    private readonly _httpClient: IModuleArtifactHttpClient = new ArtifactFetcher(),
    private readonly _cache: IArtifactCache = new NoOpArtifactCache(),
  ) {
    super(ModuleArtifactSource.Registry);
  }

  override validate(): ArtifactSourceValidationResultType {
    if (
      !this._descriptor.packageName.trim() ||
      !this._descriptor.version.trim()
    ) {
      return {
        ok: false,
        error: {
          reasonCode: RuntimeErrorCodes.SourceDescriptorInvalid,
          message:
            'Registry source requires non-empty packageName and version.',
          remediationHint:
            'Provide registry package coordinates for registry source.',
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

    const registryUrl =
      this._descriptor.registryUrl ?? 'https://registry.npmjs.org';
    const metadata = await this._fetchPackageMetadata(registryUrl);

    if (!metadata) {
      return this.createRejected('discover', {
        reasonCode: RuntimeErrorCodes.SourceFetchFailed,
        message: `Package "${this._descriptor.packageName}@${this._descriptor.version}" not found.`,
        remediationHint:
          'Verify package name and version exist in the registry.',
      });
    }

    const versionData = metadata.versions?.[this._descriptor.version];

    if (!versionData?.dist?.tarball) {
      return this.createRejected('discover', {
        reasonCode: RuntimeErrorCodes.SourceFetchFailed,
        message: `No tarball found for "${this._descriptor.packageName}@${this._descriptor.version}".`,
        remediationHint: 'Version may have been unpublished.',
      });
    }

    const tarballUrl = versionData.dist.tarball;
    const artifact = await this._getArtifact(tarballUrl);

    if ('error' in artifact) {
      return this.createRejected('discover', artifact.error);
    }

    const registryIntegrity = versionData.dist.integrity;
    const expectedChecksum =
      this._descriptor.integrity?.checksum ?? registryIntegrity;

    if (expectedChecksum) {
      const verified = this._verifyChecksum(artifact, expectedChecksum);

      if (!verified) {
        return this.createRejected('validate', {
          reasonCode: RuntimeErrorCodes.SourceIntegrityMismatch,
          message: 'Registry artifact integrity verification failed.',
          remediationHint:
            'Checksum mismatch — artifact may have been tampered with.',
        });
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
      const registryRef = `${this._descriptor.packageName}@${this._descriptor.version}`;

      const candidateArtifact: IModuleCandidateArtifact = {
        moduleId: manifest.id,
        moduleVersion: manifest.version,
        moduleEnvelope: {
          module,
          manifest,
          fullPhysicalPath: extractionResult.extractPath,
          state: ModuleState.ReadyForInitialization,
        },
        orderingKey: `registry:${registryRef}`,
        sourceType: ModuleArtifactSource.Registry,
        sourceRef: registryRef,
        packaging: extractionResult.packaging,
      };

      return candidateArtifact;
    } catch (error) {
      return this.createRejected('discover', {
        reasonCode: RuntimeErrorCodes.SourceEntryResolveFailed,
        message: `Failed to load registry artifact: ${error instanceof Error ? error.message : 'unknown'}`,
        remediationHint: 'Ensure package contains a valid module entry point.',
      });
    } finally {
      await cleanupTempDir(extractionResult.tempDir);
    }
  }

  protected override getModuleIdHint(): string | undefined {
    return this._descriptor.moduleIdHint;
  }

  protected override getSourceRef(): string {
    return `${this._descriptor.packageName}@${this._descriptor.version}`;
  }

  private async _fetchPackageMetadata(
    registryUrl: string,
  ): Promise<INpmPackageMetadata | null> {
    const metadataUrl = `${registryUrl.replace(/\/$/, '')}/${this._descriptor.packageName}`;

    try {
      const response = await this._httpClient.fetch(metadataUrl, {
        headers: { Accept: 'application/json' },
        authType: this._descriptor.authType,
        authToken: this._descriptor.authToken,
        timeoutMs: 15_000,
      });

      const metadata: INpmPackageMetadata = JSON.parse(
        response.toString('utf8'),
      );

      return metadata;
    } catch {
      return null;
    }
  }

  private async _fetchTarball(tarballUrl: string): Promise<Buffer> {
    return await this._httpClient.fetch(tarballUrl, {
      authType: this._descriptor.authType,
      authToken: this._descriptor.authToken,
    });
  }

  private _buildCacheMetadata(payload: Buffer): IArtifactCacheEntryMetadata {
    return {
      sourceRef: `${this._descriptor.packageName}@${this._descriptor.version}`,
      sourceType: ModuleArtifactSource.Registry,
      timestamp: Date.now(),
      size: payload.length,
      checksum: this._descriptor.integrity?.checksum ?? '',
    };
  }

  private async _getArtifact(tarballUrl: string): Promise<
    | Buffer
    | {
        error: {
          reasonCode: RuntimeErrorCodes;
          message: string;
          remediationHint: string;
        };
      }
  > {
    const cacheKey = ArtifactCacheKeyGenerator.forRegistry(this._descriptor);
    const cached = await this._cache.get(cacheKey);
    let payload: Buffer;

    if (cached) {
      payload = cached;
    } else {
      try {
        payload = await this._fetchTarball(tarballUrl);
        await this._cache.set(
          cacheKey,
          payload,
          this._buildCacheMetadata(payload),
        );
      } catch (error) {
        return {
          error: {
            reasonCode: RuntimeErrorCodes.SourceFetchFailed,
            message: `Failed to fetch tarball from "${tarballUrl}": ${
              error instanceof Error ? error.message : 'unknown'
            }`,
            remediationHint:
              'Ensure registry is accessible and runtime has network permissions.',
          },
        };
      }
    }

    return payload;
  }

  private _verifyChecksum(payload: Buffer, expectedChecksum: string): boolean {
    const parsed = this.parseChecksum(expectedChecksum);

    if (!parsed) return false;

    if (parsed.algorithm === 'sha512') {
      const actual = createHash('sha512').update(payload).digest('base64');
      const expectedValue = parsed.value.replace(/^sha512-/, '');
      return actual === expectedValue;
    }

    if (parsed.algorithm === 'sha256') {
      const actual = createHash('sha256').update(payload).digest('hex');
      return actual === parsed.value;
    }

    return false;
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
    const tempDir = await createTempDir('prosto-registry');
    const tempFilePath = join(tempDir, 'package.tgz');
    const extractPath = join(tempDir, 'extracted');

    try {
      await writeFile(tempFilePath, artifact);
      await ArtifactExtractor.extractTgz(tempFilePath, extractPath);
    } catch (error) {
      return {
        error: {
          reasonCode: RuntimeErrorCodes.SourceExtractionFailed,
          message: `Failed to extract registry artifact: ${
            error instanceof Error ? error.message : 'unknown'
          }`,
          remediationHint: 'Ensure artifact is a valid tarball.',
        },
      };
    }

    return {
      tempDir,
      packaging: ModuleArtifactPackaging.Tgz,
      extractPath: join(extractPath, 'package'),
    };
  }
}
