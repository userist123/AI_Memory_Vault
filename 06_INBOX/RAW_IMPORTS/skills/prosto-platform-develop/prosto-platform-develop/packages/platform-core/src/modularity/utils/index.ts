import type { IPlatformModuleManifest } from '@prosto/platform-sdk';

export function isModuleCritical(moduleManifest: IPlatformModuleManifest) {
  return !moduleManifest.optional;
}
