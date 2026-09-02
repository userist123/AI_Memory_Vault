import type { ZodIssue, ZodType } from 'zod';
import type {
  IPlatformModuleManifest,
  IPlatformModuleManifestValidator,
  PlatformModuleManifestValidationResultType,
} from '../interfaces/index.js';
import {
  type IPlatformModuleManifestValidationIssue,
  PlatformModuleManifestValidationError,
} from '../errors/index.js';
import { PlatformModuleManifestSchema } from '../schemas/index.js';

/**
 * @alpha
 * The default implementation of platform module manifest validation.
 */
export class PlatformModuleManifestValidator implements IPlatformModuleManifestValidator {
  constructor(
    protected readonly manifestSchema: ZodType<IPlatformModuleManifest> = PlatformModuleManifestSchema,
  ) {}

  validate(manifest: unknown): PlatformModuleManifestValidationResultType {
    const schemaResult = this._validateManifestSchema(
      this.manifestSchema,
      manifest,
    );

    if (!schemaResult.success) {
      return schemaResult;
    }

    const semanticIssues = this._validateManifestSemantics(
      schemaResult.manifest,
    );

    if (semanticIssues.length) {
      return {
        success: false,
        error: new PlatformModuleManifestValidationError(semanticIssues),
      };
    }

    return schemaResult;
  }

  parse(manifest: unknown): IPlatformModuleManifest {
    const result = this.validate(manifest);

    if (!result.success) {
      throw result.error;
    }

    return result.manifest;
  }

  protected _toManifestValidationIssue(
    issue: ZodIssue,
  ): IPlatformModuleManifestValidationIssue {
    return {
      code: issue.code,
      message: issue.message,
      path: !issue.path.length ? '$' : issue.path.join('.'),
    };
  }

  protected _collectDuplicates(values: readonly string[]): string[] {
    const seen = new Set<string>();
    const duplicates = new Set<string>();

    for (const value of values) {
      if (seen.has(value)) {
        duplicates.add(value);
        continue;
      }

      seen.add(value);
    }

    return [...duplicates];
  }

  protected _validateManifestSchema(
    manifestSchema: ZodType<IPlatformModuleManifest>,
    manifest: unknown,
  ): PlatformModuleManifestValidationResultType {
    const parsed = manifestSchema.safeParse(manifest);

    if (!parsed.success) {
      const issues = parsed.error.issues.map((issue) =>
        this._toManifestValidationIssue(issue),
      );

      return {
        success: false,
        error: new PlatformModuleManifestValidationError(issues),
      };
    }

    return {
      success: true,
      manifest: parsed.data,
    };
  }

  protected _validateManifestSemantics(
    manifest: IPlatformModuleManifest,
  ): IPlatformModuleManifestValidationIssue[] {
    const issues: IPlatformModuleManifestValidationIssue[] = [];

    const dependencyIds = manifest.dependencies.map(
      (dependency) => dependency.id,
    );
    const duplicateDependencies = this._collectDuplicates(dependencyIds);

    for (const dependencyId of duplicateDependencies) {
      issues.push({
        code: 'duplicate_dependency',
        message: `Dependency "${dependencyId}" is declared more than once.`,
        path: 'dependencies',
      });
    }

    if (dependencyIds.includes(manifest.id)) {
      issues.push({
        code: 'self_dependency',
        message: 'Manifest dependencies must not reference the module itself.',
        path: 'dependencies',
      });
    }

    const duplicateGroups = this._collectDuplicates(manifest.groups || []);

    for (const group of duplicateGroups) {
      issues.push({
        code: 'duplicate_group',
        message: `Group "${group}" is declared more than once.`,
        path: 'groups',
      });
    }

    return issues;
  }
}
