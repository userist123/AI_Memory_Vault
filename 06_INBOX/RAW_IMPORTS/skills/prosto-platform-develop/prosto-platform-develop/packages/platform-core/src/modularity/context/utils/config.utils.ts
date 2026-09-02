import type { IPlatformConfig } from '@/runtime/index.js';
import { resolveNestedValue, setNestedValue } from '@prosto/platform-sdk';

/**
 * @alpha
 * Utility functions for building scoped configuration projections.
 */

/**
 * Extract module-scoped configuration from full config object.
 * Returns only the modules.<moduleId> subtree.
 */
export function extractModuleScopedConfig(
  fullConfig: IPlatformConfig,
  moduleId: string,
): Record<string, unknown> {
  const modulesConfig = fullConfig.modules;

  if (!modulesConfig || typeof modulesConfig !== 'object') {
    return {};
  }

  const moduleConfig = modulesConfig[moduleId];

  if (!moduleConfig || typeof moduleConfig !== 'object') {
    return {};
  }

  return { ...moduleConfig };
}

/**
 * Extract configuration sections from full config.
 * Only includes sections that are in the allowlist.
 */
export function extractAllowedSections(
  fullConfig: IPlatformConfig,
  allowedSections: readonly string[],
): Record<string, unknown> {
  const result: Record<string, unknown> = {};

  for (const section of allowedSections) {
    const value = resolveNestedValue(fullConfig, section);

    if (value !== undefined) {
      setNestedValue(result, section, value, { pathSeparator: '.' });
    }
  }

  return result;
}

/**
 * Build a scoped configuration projection for a module.
 * Combines module-scoped config with allowed global sections.
 */
export function buildScopedConfigProjection(
  fullConfig: IPlatformConfig,
  moduleId: string,
  allowedSections: readonly string[],
): Record<string, unknown> {
  const projection: Record<string, any> = {}; // eslint-disable-line @typescript-eslint/no-explicit-any

  // Include allowed global sections
  if (allowedSections.length) {
    const globalSections = extractAllowedSections(fullConfig, allowedSections);

    Object.assign(projection, globalSections);
  }

  // Always include module-scoped config
  if (!projection.modules?.[moduleId]) {
    projection.modules = {
      ...projection.modules,
      [moduleId]: extractModuleScopedConfig(fullConfig, moduleId),
    };
  }

  return createReadonlyObject(projection);
}

/**
 * Create a read-only wrapper around a configuration object.
 * Prevents accidental mutation of the original config.
 */
export function createReadonlyObject(
  config: Record<string, unknown>,
): Readonly<Record<string, unknown>> {
  return Object.freeze(structuredClone(config));
}
