import type { ZodIssue, ZodType } from 'zod';
import { AdminPermissionPolicyValidationError } from './admin-permissions.error.js';
import type {
  AdminPermissionPolicyValidationResultType,
  IAdminPermissionPolicy,
  IAdminPermissionPolicyValidationIssue,
  IAdminPermissionPolicyValidator,
} from './admin-permissions.interfaces.js';
import { AdminPermissionPolicySchema } from './admin-permissions.schema.js';

/**
 * @alpha
 * The default implementation of admin permission policy validation.
 */
export class AdminPermissionPolicyValidator implements IAdminPermissionPolicyValidator {
  constructor(
    protected readonly policySchema: ZodType<IAdminPermissionPolicy> = AdminPermissionPolicySchema,
  ) {}

  validate(policy: unknown): AdminPermissionPolicyValidationResultType {
    const schemaResult = this._validatePolicySchema(this.policySchema, policy);

    if (!schemaResult.success) {
      return schemaResult;
    }

    const semanticIssues = this._validatePolicySemantics(schemaResult.policy);

    if (semanticIssues.length) {
      return {
        success: false,
        error: new AdminPermissionPolicyValidationError(semanticIssues),
      };
    }

    return schemaResult;
  }

  parse(policy: unknown): IAdminPermissionPolicy {
    const result = this.validate(policy);

    if (!result.success) {
      throw result.error;
    }

    return result.policy;
  }

  protected _toPolicyValidationIssue(
    issue: ZodIssue,
  ): IAdminPermissionPolicyValidationIssue {
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

  protected _validatePolicySchema(
    policySchema: ZodType<IAdminPermissionPolicy>,
    policy: unknown,
  ): AdminPermissionPolicyValidationResultType {
    const parsed = policySchema.safeParse(policy);

    if (!parsed.success) {
      const issues = parsed.error.issues.map((issue) =>
        this._toPolicyValidationIssue(issue),
      );

      return {
        success: false,
        error: new AdminPermissionPolicyValidationError(issues),
      };
    }

    return {
      success: true,
      policy: parsed.data,
    };
  }

  protected _validatePolicySemantics(
    policy: IAdminPermissionPolicy,
  ): IAdminPermissionPolicyValidationIssue[] {
    const issues: IAdminPermissionPolicyValidationIssue[] = [];

    this._appendDuplicateIssues(
      issues,
      'roleMappings',
      'duplicate_role_mapping',
      policy.roleMappings.map((mapping) => mapping.roleId),
    );
    this._appendDuplicateIssues(
      issues,
      'actionGates',
      'duplicate_action_gate',
      policy.actionGates.map((gate) => gate.actionId),
    );

    for (const [mappingIndex, mapping] of policy.roleMappings.entries()) {
      this._appendDuplicateIssues(
        issues,
        `roleMappings.${mappingIndex.toString()}.permissions`,
        'duplicate_role_permission',
        mapping.permissions,
      );
    }

    for (const [gateIndex, gate] of policy.actionGates.entries()) {
      this._appendDuplicateIssues(
        issues,
        `actionGates.${gateIndex.toString()}.requiredPermissions`,
        'duplicate_gate_permission',
        gate.requiredPermissions,
      );

      if (gate.requiredPermissions.length === 0 && gate.effect === 'allow') {
        issues.push({
          code: 'empty_allow_gate_permissions',
          message: `Allow gate "${gate.actionId}" must declare at least one required permission.`,
          path: `actionGates.${gateIndex.toString()}.requiredPermissions`,
        });
      }
    }

    return issues;
  }

  protected _appendDuplicateIssues(
    issues: IAdminPermissionPolicyValidationIssue[],
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
