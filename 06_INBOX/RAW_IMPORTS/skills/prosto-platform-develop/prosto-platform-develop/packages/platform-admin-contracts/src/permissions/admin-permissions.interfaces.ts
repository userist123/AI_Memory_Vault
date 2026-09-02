import type {
  AdminActionGatingEffectType,
  AdminActionIdentifierType,
  AdminPermissionMatchStrategyType,
  AdminPermissionPolicySchemaVersionType,
  AdminPermissionTokenType,
  AdminRoleIdentifierType,
} from './admin-permissions.types.js';

/**
 * @alpha
 * Maps a role to the permissions granted by an upstream identity provider or BFF.
 */
export interface IAdminRolePermissionMapping {
  readonly roleId: AdminRoleIdentifierType;
  readonly permissions: readonly AdminPermissionTokenType[];
  readonly description?: string;
}

/**
 * @alpha
 * Defines the permission requirements and effect for a gated admin action.
 */
export interface IAdminActionGatePolicy {
  readonly actionId: AdminActionIdentifierType;
  readonly requiredPermissions: readonly AdminPermissionTokenType[];
  readonly match: AdminPermissionMatchStrategyType;
  readonly effect: AdminActionGatingEffectType;
  readonly remediationHint?: string;
}

/**
 * @alpha
 * Versioned, framework-neutral admin permission policy contract.
 */
export interface IAdminPermissionPolicy {
  readonly schemaVersion: AdminPermissionPolicySchemaVersionType;
  readonly roleMappings: readonly IAdminRolePermissionMapping[];
  readonly actionGates: readonly IAdminActionGatePolicy[];
  readonly metadata?: Readonly<Record<string, string>>;
}

/**
 * @alpha
 * Request context used to evaluate an action gate without binding to a UI framework.
 */
export interface IAdminActionGateEvaluationContext {
  readonly actionId: AdminActionIdentifierType;
  readonly roleIds: readonly AdminRoleIdentifierType[];
  readonly additionalPermissions?: readonly AdminPermissionTokenType[];
}

/**
 * @alpha
 * Decision produced by evaluating a gated admin action against role mappings.
 */
export interface IAdminActionGateDecision {
  readonly actionId: AdminActionIdentifierType;
  readonly allowed: boolean;
  readonly effect: AdminActionGatingEffectType;
  readonly missingPermissions: readonly AdminPermissionTokenType[];
  readonly reasonCode?:
    | 'ACTION_GATE_NOT_FOUND'
    | 'PERMISSION_REQUIREMENT_NOT_MET';
  readonly remediationHint?: string;
}

/**
 * @alpha
 * A single validation issue produced by admin permission policy parsing.
 */
export interface IAdminPermissionPolicyValidationIssue {
  readonly code: string;
  readonly message: string;
  readonly path: string;
}

/**
 * @alpha
 * Successful admin permission policy validation result.
 */
export interface IAdminPermissionPolicyValidationSuccess {
  readonly success: true;
  readonly policy: IAdminPermissionPolicy;
}

/**
 * @alpha
 * Failed admin permission policy validation result.
 */
export interface IAdminPermissionPolicyValidationFailure {
  readonly success: false;
  readonly error: Error & {
    readonly issues: readonly IAdminPermissionPolicyValidationIssue[];
  };
}

/**
 * @alpha
 * Discriminated validation result for admin permission policies.
 */
export type AdminPermissionPolicyValidationResultType =
  | IAdminPermissionPolicyValidationSuccess
  | IAdminPermissionPolicyValidationFailure;

/**
 * @alpha
 * Validation contract for admin permission policy implementations.
 */
export interface IAdminPermissionPolicyValidator {
  validate(policy: unknown): AdminPermissionPolicyValidationResultType;
  parse(policy: unknown): IAdminPermissionPolicy;
}

/**
 * @alpha
 * Evaluation contract for admin action gate decisions.
 */
export interface IAdminActionGateEvaluator {
  evaluate(
    policy: IAdminPermissionPolicy,
    context: IAdminActionGateEvaluationContext,
  ): IAdminActionGateDecision;
}
