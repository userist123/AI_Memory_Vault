import type {
  IArtifactCache,
  IArtifactCacheEntryMetadata,
  IArtifactCacheOptions,
} from './interfaces/index.js';
import {
  mkdir,
  readdir,
  readFile,
  stat,
  unlink,
  writeFile,
} from 'node:fs/promises';
import { join } from 'node:path';

/**
 * @alpha
 * File-system backed artifact cache with LRU eviction and TTL.
 */
export class FileSystemArtifactCache implements IArtifactCache {
  private readonly _cachePath: string;
  private readonly _maxAgeMs: number;
  private readonly _maxSizeBytes: number;

  constructor(private readonly _options: IArtifactCacheOptions) {
    this._cachePath = this._options.path;
    this._maxAgeMs = _options.maxAgeMs ?? 14 * 24 * 60 * 60 * 1000; // 14 days
    this._maxSizeBytes = _options.maxSizeBytes ?? 500 * 1024 * 1024; // 500MB
  }

  async get(key: string): Promise<Buffer | null> {
    const metaPath = this._metaPath(key);
    const dataPath = this._dataPath(key);

    try {
      const metaContent = await readFile(metaPath, 'utf8');
      const metadata: IArtifactCacheEntryMetadata = JSON.parse(metaContent);

      if (this._isExpired(metadata)) {
        await this.evict(key);
        return null;
      }

      const data = await readFile(dataPath);

      metadata.lastAccessTimestamp = Date.now();
      await writeFile(metaPath, JSON.stringify(metadata), 'utf8');

      return data;
    } catch {
      return null;
    }
  }

  async set(
    key: string,
    data: Buffer,
    metadata: IArtifactCacheEntryMetadata,
  ): Promise<void> {
    const dataPath = this._dataPath(key);
    const metaPath = this._metaPath(key);

    await mkdir(this._cachePath, { recursive: true });

    await writeFile(dataPath, data);

    const entryMetadata: IArtifactCacheEntryMetadata = {
      ...metadata,
      size: data.length,
      timestamp: metadata.timestamp ?? Date.now(),
      lastAccessTimestamp: Date.now(),
    };

    await writeFile(metaPath, JSON.stringify(entryMetadata), 'utf8');

    await this._evictIfNeeded();
  }

  async has(key: string): Promise<boolean> {
    const metaPath = this._metaPath(key);

    try {
      const metaContent = await readFile(metaPath, 'utf8');
      const metadata: IArtifactCacheEntryMetadata = JSON.parse(metaContent);

      if (this._isExpired(metadata)) {
        await this.evict(key);
        return false;
      }

      return true;
    } catch {
      return false;
    }
  }

  async evict(key: string): Promise<void> {
    const dataPath = this._dataPath(key);
    const metaPath = this._metaPath(key);

    try {
      await unlink(dataPath);
    } catch {
      // File may not exist, ignore
    }

    try {
      await unlink(metaPath);
    } catch {
      // File may not exist, ignore
    }
  }

  async clear(): Promise<void> {
    try {
      const entries = await readdir(this._cachePath);

      for (const entry of entries) {
        const fullPath = join(this._cachePath, entry);
        const entryStat = await stat(fullPath);

        if (entryStat.isFile()) {
          await unlink(fullPath);
        }
      }
    } catch {
      // Directory may not exist, ignore
    }
  }

  private _dataPath(key: string): string {
    return join(this._cachePath, this._safeKey(key));
  }

  private _metaPath(key: string): string {
    return join(this._cachePath, `${this._safeKey(key)}.meta.json`);
  }

  private _safeKey(key: string): string {
    return key.replace(/[^a-zA-Z0-9_-]/g, '_');
  }

  private _isExpired(metadata: IArtifactCacheEntryMetadata): boolean {
    return Date.now() - metadata.timestamp > this._maxAgeMs;
  }

  private async _evictIfNeeded(): Promise<void> {
    let totalSize = await this._calculateTotalSize();

    while (totalSize > this._maxSizeBytes) {
      const oldestKey = await this._findOldestEntry();

      if (!oldestKey) break;

      await this.evict(oldestKey);

      totalSize = await this._calculateTotalSize();
    }
  }

  private async _calculateTotalSize(): Promise<number> {
    try {
      const entries = await readdir(this._cachePath);
      let total = 0;

      for (const entry of entries) {
        if (entry.endsWith('.meta.json')) {
          continue;
        }

        const fullPath = join(this._cachePath, entry);

        try {
          const entryStat = await stat(fullPath);
          total += entryStat.size;
        } catch {
          // File may have been deleted, skip
        }
      }

      return total;
    } catch {
      return 0;
    }
  }

  private async _findOldestEntry(): Promise<string | null> {
    try {
      const entries = await readdir(this._cachePath);
      let oldestKey: string | null = null;
      let oldestTimestamp = Infinity;

      for (const entry of entries) {
        if (!entry.endsWith('.meta.json')) {
          continue;
        }

        const fullPath = join(this._cachePath, entry);

        try {
          const metaContent = await readFile(fullPath, 'utf8');
          const metadata: IArtifactCacheEntryMetadata = JSON.parse(metaContent);
          const accessTime = metadata.lastAccessTimestamp ?? metadata.timestamp;

          if (accessTime < oldestTimestamp) {
            oldestTimestamp = accessTime;
            oldestKey = entry.replace('.meta.json', '');
          }
        } catch {
          // Metadata file may be corrupted, skip
        }
      }

      return oldestKey;
    } catch {
      return null;
    }
  }
}
