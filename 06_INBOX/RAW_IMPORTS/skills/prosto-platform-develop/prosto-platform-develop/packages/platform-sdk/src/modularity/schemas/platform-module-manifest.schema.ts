import { z } from 'zod';
import { isSemverRange, isSemverVersion } from '@/utils/index.js';
import { MODULE_ID_PATTERN } from '../constants/index.js';

/**
 * @alpha
 * Zod schema for semver version.
 */
export const SemverVersionSchema = z.string().refine(isSemverVersion, {
  message: 'Value must be a valid semver version.',
});

/**
 * @alpha
 * Zod schema for semver range.
 */
export const SemverRangeSchema = z.string().refine(isSemverRange, {
  message: 'Value must be a valid semver range.',
});

/**
 * @alpha
 * Zod schema for module dependency declarations.
 */
export const PlatformModuleDependencySchema = z
  .object({
    id: z.string().regex(MODULE_ID_PATTERN, {
      message: 'Dependency id must match module id pattern.',
    }),
    version: SemverRangeSchema,
    optional: z.boolean().optional(),
  })
  .strict();

/**
 * @alpha
 * Zod schema for module incompatibilities declarations.
 */
export const PlatformModuleIncompatibilitySchema = z
  .object({
    id: z.string().regex(MODULE_ID_PATTERN, {
      message: 'Incompatibility id must match module id pattern.',
    }),
    version: SemverRangeSchema,
  })
  .strict();

/**
 * @alpha
 * Zod schema for platform module manifests.
 */
export const PlatformModuleManifestSchema = z
  .object({
    id: z.string().regex(MODULE_ID_PATTERN, {
      message: 'Module id must match module id pattern.',
    }),
    version: SemverVersionSchema,
    sdkVersion: SemverRangeSchema,
    nodeVersion: SemverRangeSchema.optional(),
    title: z
      .string()
      .trim()
      .min(1, { message: 'Module title must not be empty.' }),
    description: z.string().optional(),
    optional: z.boolean().optional(),
    iconUrl: z.string().optional(),
    projectUrl: z.string().optional(),
    dependencies: z.array(PlatformModuleDependencySchema).default([]),
    incompatibilities: z.array(PlatformModuleIncompatibilitySchema).optional(),
    groups: z.array(z.string()).optional(),
    tags: z.array(z.string()).optional(),
    authors: z.array(z.string()).optional(),
    owners: z.array(z.string()).optional(),
    copyright: z.string().optional(),
  })
  .strict();

/**
 * @alpha
 * Runtime input type accepted by manifest schema validation.
 */
export type PlatformModuleManifestInputType = z.input<
  typeof PlatformModuleManifestSchema
>;

/**
 * @alpha
 * Runtime output type produced by manifest schema validation.
 */
export type PlatformModuleManifestOutputType = z.output<
  typeof PlatformModuleManifestSchema
>;
