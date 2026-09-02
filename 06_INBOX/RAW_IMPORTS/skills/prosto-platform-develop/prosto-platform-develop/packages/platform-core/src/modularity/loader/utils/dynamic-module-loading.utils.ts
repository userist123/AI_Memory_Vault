import { pathToFileURL } from 'node:url';
import type {
  IPlatformModule,
  IPlatformModuleManifest,
} from '@prosto/platform-sdk';

type UnknownFunctionType = (...args: unknown[]) => unknown;
type UnknownConstructorType = new (...args: unknown[]) => unknown;

/**
 * @alpha
 * ESM module loading with multi-format export resolution.
 */
export class DynamicModuleLoader {
  static async loadModuleManifest(manifestPath: string) {
    const nameSpace = await import(pathToFileURL(manifestPath).href);

    const defaultResult = this._tryResolveManifestDefaultExport(nameSpace);
    if (defaultResult) return defaultResult;

    throw new Error('No valid IPlatformModuleManifest export found');
  }

  static async loadModuleEntry(entryPath: string): Promise<IPlatformModule> {
    const nameSpace = await import(pathToFileURL(entryPath).href);

    const defaultResult = this._tryResolveModuleDefaultExport(nameSpace);
    if (defaultResult) return defaultResult;

    const namedResult = this._tryResolveModuleNamedExports(nameSpace);
    if (namedResult) return namedResult;

    throw new Error('No valid IPlatformModule export found');
  }

  private static _tryResolveManifestDefaultExport(
    nameSpace: Record<string, unknown>,
  ): IPlatformModuleManifest | null {
    const defaultExport = nameSpace.default;

    if (!defaultExport) return null;

    if (this._isPlatformModuleManifest(defaultExport)) return defaultExport;

    return null;
  }

  private static _tryResolveModuleDefaultExport(
    nameSpace: Record<string, unknown>,
  ): IPlatformModule | null {
    const defaultExport = nameSpace.default;

    if (!defaultExport) return null;

    if (this._isPlatformModule(defaultExport)) return defaultExport;

    if (this._isPlatformModuleClass(defaultExport)) return new defaultExport();

    return null;
  }

  private static _tryResolveModuleNamedExports(
    nameSpace: Record<string, unknown>,
  ): IPlatformModule | null {
    for (const key of Object.keys(nameSpace)) {
      if (key === 'default') continue;

      const exportValue = nameSpace[key];

      if (this._isPlatformModule(exportValue)) return exportValue;

      if (this._isPlatformModuleClass(exportValue)) return new exportValue();

      if (this._isFactoryFunction(exportValue)) {
        const result = exportValue();

        if (this._isPlatformModule(result)) return result;
      }
    }

    return null;
  }

  private static _isPlatformModuleManifest(
    obj: unknown,
  ): obj is IPlatformModuleManifest {
    return (
      typeof obj === 'object' &&
      obj !== null &&
      'id' in obj &&
      'sdkVersion' in obj &&
      'dependencies' in obj &&
      Array.isArray(obj.dependencies)
    );
  }

  private static _isPlatformModule(obj: unknown): obj is IPlatformModule {
    return (
      typeof obj === 'object' &&
      obj !== null &&
      'init' in obj &&
      typeof obj.init === 'function' &&
      'start' in obj &&
      typeof obj.start === 'function' &&
      'stop' in obj &&
      typeof obj.stop === 'function'
    );
  }

  private static _isPlatformModuleClass(
    fn: unknown,
  ): fn is new () => IPlatformModule {
    if (typeof fn !== 'function') return false;

    const proto = (fn as UnknownConstructorType).prototype;

    if (!proto || typeof proto !== 'object') return false;

    return (
      'init' in proto &&
      typeof proto.init === 'function' &&
      'start' in proto &&
      typeof proto.start === 'function' &&
      'stop' in proto &&
      typeof proto.stop === 'function'
    );
  }

  private static _isFactoryFunction(fn: unknown): fn is UnknownFunctionType {
    if (typeof fn !== 'function') return false;

    const name = (fn as UnknownFunctionType).name || '';

    return /^(create|init|factory|build|make)/i.test(name);
  }
}
