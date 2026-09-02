import type { ZodIssue, ZodType } from 'zod';
import type {
  AdminUIPluginManifestValidationResultType,
  IAdminUIPluginManifest,
  IAdminUIPluginManifestValidationIssue,
  IAdminUIPluginManifestValidator,
} from './admin-ui-plugin-manifest.interfaces.js';
import { AdminUIPluginManifestValidationError } from './admin-ui-plugin-manifest.error.js';
import { AdminUIPluginManifestSchema } from './admin-ui-plugin-manifest.schema.js';

/**
 * @alpha
 * The default implementation of admin UI plugin manifest validation.
 */
export class AdminUIPluginManifestValidator implements IAdminUIPluginManifestValidator {
  constructor(
    protected readonly manifestSchema: ZodType<IAdminUIPluginManifest> = AdminUIPluginManifestSchema,
  ) {}

  validate(manifest: unknown): AdminUIPluginManifestValidationResultType {
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
        error: new AdminUIPluginManifestValidationError(semanticIssues),
      };
    }

    return schemaResult;
  }

  parse(manifest: unknown): IAdminUIPluginManifest {
    const result = this.validate(manifest);

    if (!result.success) {
      throw result.error;
    }

    return result.manifest;
  }

  protected _toManifestValidationIssue(
    issue: ZodIssue,
  ): IAdminUIPluginManifestValidationIssue {
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
    manifestSchema: ZodType<IAdminUIPluginManifest>,
    manifest: unknown,
  ): AdminUIPluginManifestValidationResultType {
    const parsed = manifestSchema.safeParse(manifest);

    if (!parsed.success) {
      const issues = parsed.error.issues.map((issue) =>
        this._toManifestValidationIssue(issue),
      );

      return {
        success: false,
        error: new AdminUIPluginManifestValidationError(issues),
      };
    }

    return {
      success: true,
      manifest: parsed.data,
    };
  }

  protected _validateManifestSemantics(
    manifest: IAdminUIPluginManifest,
  ): IAdminUIPluginManifestValidationIssue[] {
    const issues: IAdminUIPluginManifestValidationIssue[] = [];

    this._appendDuplicateIssues(
      issues,
      'requiredPermissions',
      'duplicate_permission',
      manifest.requiredPermissions,
    );
    this._appendDuplicateIssues(
      issues,
      'requiredCapabilities',
      'duplicate_capability',
      manifest.requiredCapabilities,
    );
    this._appendDuplicateIssues(
      issues,
      'extensionPoints',
      'duplicate_extension_point',
      manifest.extensionPoints,
    );

    return issues;
  }

  protected _appendDuplicateIssues(
    issues: IAdminUIPluginManifestValidationIssue[],
    path: string,
    code: string,
    values: readonly string[],
  ): void {
    const duplicateValues = this._collectDuplicates(values);

    for (const value of duplicateValues) {
      issues.push({
        code,
        message: `Value "${value}" is declared more than once.`,
        path,
      });
    }
  }
}
