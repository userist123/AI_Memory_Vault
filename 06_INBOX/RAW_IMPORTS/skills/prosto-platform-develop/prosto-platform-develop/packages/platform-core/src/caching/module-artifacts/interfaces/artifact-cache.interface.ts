import type { IArtifactCacheEntryMetadata } from './artifact-cache-entry-metadata.interface.js';

/**
 * @alpha
 * Contract for artifact caching layer.
 */
export interface IArtifactCache {
  get(key: string): Promise<Buffer | null>;
  set(
    key: string,
    data: Buffer,
    metadata: IArtifactCacheEntryMetadata,
  ): Promise<void>;
  has(key: string): Promise<boolean>;
  evict(key: string): Promise<void>;
  clear(): Promise<void>;
}
