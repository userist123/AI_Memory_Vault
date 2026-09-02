import type {
  IModulePathArtifactSource,
  IModuleUrlArtifactSource,
  IModuleRegistryArtifactSource,
} from '@/modularity/index.js';
import { createHash } from 'node:crypto';

/**
 * @alpha
 * Utility class for generating cache keys for module artifacts.
 */
export class ArtifactCacheKeyGenerator {
  static forPath(descriptor: IModulePathArtifactSource): string {
    const content = `${descriptor.path}:${descriptor.integrity?.checksum ?? ''}`;
    return `path:${createHash('sha256').update(content).digest('hex')}`;
  }

  static forUrl(descriptor: IModuleUrlArtifactSource): string {
    const url = this._stripQueryParams(descriptor.url);
    const content = `${url}:${descriptor.integrity?.checksum ?? ''}`;
    return `url:${createHash('sha256').update(content).digest('hex')}`;
  }

  static forRegistry(descriptor: IModuleRegistryArtifactSource): string {
    const registryUrl = descriptor.registryUrl ?? 'https://registry.npmjs.org';
    const content = `${registryUrl}:${descriptor.packageName}:${descriptor.version}:${descriptor.integrity?.checksum ?? ''}`;
    return `registry:${createHash('sha256').update(content).digest('hex')}`;
  }

  private static _stripQueryParams(url: string): string {
    try {
      const parsed = new URL(url);
      parsed.search = '';
      return parsed.toString();
    } catch {
      return url;
    }
  }
}
