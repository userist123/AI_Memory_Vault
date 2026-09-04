/* eslint-disable @typescript-eslint/no-empty-function */
import type {
  IArtifactCache,
  IArtifactCacheEntryMetadata,
} from './interfaces/index.js';

/**
 * @alpha
 * No-op cache implementation — Null Object pattern.
 * Used when caching is disabled.
 */
export class NoOpArtifactCache implements IArtifactCache {
  async get(_key: string): Promise<Buffer | null> {
    return null;
  }

  async set(
    _key: string,
    _data: Buffer,
    _metadata: IArtifactCacheEntryMetadata,
  ): Promise<void> {}

  async has(_key: string): Promise<boolean> {
    return false;
  }

  async evict(_key: string): Promise<void> {}

  async clear(): Promise<void> {}
}
