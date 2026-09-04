import type {
  IAdminActionGateDecision,
  IAdminActionGateEvaluator,
  IAdminPermissionPolicy,
} from '@prosto/platform-admin-contracts';
import { AdminActionGateEvaluator } from '@prosto/platform-admin-contracts';
import type {
  IAdminActionEvaluationResult,
  IAdminPermissionMappingService,
} from '../admin-bff.interfaces.js';
import type { IPlatformDelegatedIdentity } from '@prosto/platform-sdk';

/**
 * @alpha
 * Configuration for the admin permission mapping service.
 */
export interface IAdminPermissionMappingServiceConfig {
  readonly policy: IAdminPermissionPolicy;
  readonly evaluator?: IAdminActionGateEvaluator;
}

/**
 * @alpha
 * Default implementation of the admin permission mapping service.
 *
 * Maps operator roles to allowed extension actions using a versioned
 * permission policy. Evaluates action gates and returns allow/deny
 * decisions with remediation metadata.
 *
 * This service also supports filtering discovery payloads by operator
 * permissions, ensuring plugins requiring unavailable permissions
 * are excluded from the response.
 */
export class AdminPermissionMappingService implements IAdminPermissionMappingService {
  private readonly _evaluator: IAdminActionGateEvaluator;

  constructor(private readonly _config: IAdminPermissionMappingServiceConfig) {
    this._evaluator = _config.evaluator ?? new AdminActionGateEvaluator();
  }

  /**
   * Evaluates whether an operator can execute a specific action.
   *
   * @param actionId - The action identifier to evaluate.
   * @param identity - The delegated identity with roles and permissions.
   * @returns Evaluation result with allow/deny decision and remediation metadata.
   */
  evaluateAction(
    actionId: string,
    identity: IPlatformDelegatedIdentity,
  ): IAdminActionEvaluationResult {
    const decision = this._evaluator.evaluate(this._config.policy, {
      actionId,
      roleIds: [...identity.roles],
      additionalPermissions: [...identity.permissions],
    });

    return this._mapDecisionToResult(decision);
  }

  /**
   * Evaluates whether an operator can execute a specific action.
   * Alias for {@link evaluateAction} for consistency with the contract.
   */
  evaluate(
    actionId: string,
    identity: IPlatformDelegatedIdentity,
  ): IAdminActionEvaluationResult {
    return this.evaluateAction(actionId, identity);
  }

  /**
   * Checks if an operator has a specific permission.
   *
   * @param permission - The permission token to check.
   * @param identity - The delegated identity.
   * @returns true if the operator has the permission.
   */
  hasPermission(
    permission: string,
    identity: IPlatformDelegatedIdentity,
  ): boolean {
    const grantedPermissions = this.collectGrantedPermissions(identity);
    return grantedPermissions.has(permission);
  }

  /**
   * Collects all permissions granted to an operator based on their roles.
   *
   * @param identity - The delegated identity.
   * @returns Set of granted permission tokens.
   */
  collectGrantedPermissions(
    identity: IPlatformDelegatedIdentity,
  ): ReadonlySet<string> {
    const grantedPermissions = new Set<string>([...identity.permissions]);
    const requestedRoles = new Set(identity.roles);

    for (const mapping of this._config.policy.roleMappings) {
      if (!requestedRoles.has(mapping.roleId)) {
        continue;
      }

      for (const permission of mapping.permissions) {
        grantedPermissions.add(permission);
      }
    }

    return grantedPermissions;
  }

  /**
   * Filters a list of required permissions against the operator's granted permissions.
   *
   * @param requiredPermissions - Permissions required by a plugin or action.
   * @param identity - The delegated identity.
   * @returns Object with allowed flag and list of missing permissions.
   */
  filterPermissions(
    requiredPermissions: readonly string[],
    identity: IPlatformDelegatedIdentity,
  ): {
    readonly allowed: boolean;
    readonly missingPermissions: readonly string[];
  } {
    const grantedPermissions = this.collectGrantedPermissions(identity);
    const missingPermissions = requiredPermissions.filter(
      (permission) => !grantedPermissions.has(permission),
    );

    return {
      allowed: missingPermissions.length === 0,
      missingPermissions,
    };
  }

  /**
   * Returns the permission policy used by this service.
   */
  getPolicy(): IAdminPermissionPolicy {
    return this._config.policy;
  }

  private _mapDecisionToResult(
    decision: IAdminActionGateDecision,
  ): IAdminActionEvaluationResult {
    return {
      allowed: decision.allowed,
      actionId: decision.actionId,
      reasonCode: decision.reasonCode,
      remediationHint: decision.remediationHint,
    };
  }
}
