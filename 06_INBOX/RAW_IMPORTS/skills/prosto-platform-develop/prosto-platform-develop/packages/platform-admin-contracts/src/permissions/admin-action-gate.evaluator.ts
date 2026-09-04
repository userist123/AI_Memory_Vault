import type {
  IAdminActionGateDecision,
  IAdminActionGateEvaluationContext,
  IAdminActionGateEvaluator,
  IAdminPermissionPolicy,
} from './admin-permissions.interfaces.js';
import type { AdminPermissionTokenType } from './admin-permissions.types.js';

/**
 * @alpha
 * Framework-neutral evaluator for admin action gate policies.
 */
export class AdminActionGateEvaluator implements IAdminActionGateEvaluator {
  evaluate(
    policy: IAdminPermissionPolicy,
    context: IAdminActionGateEvaluationContext,
  ): IAdminActionGateDecision {
    const gate = policy.actionGates.find(
      (candidate) => candidate.actionId === context.actionId,
    );

    if (!gate) {
      return {
        actionId: context.actionId,
        allowed: false,
        effect: 'deny',
        missingPermissions: [],
        reasonCode: 'ACTION_GATE_NOT_FOUND',
        remediationHint: 'Declare an action gate policy for this admin action.',
      };
    }

    if (gate.effect === 'deny') {
      return {
        actionId: context.actionId,
        allowed: false,
        effect: gate.effect,
        missingPermissions: [],
        remediationHint: gate.remediationHint,
      };
    }

    const grantedPermissions = this._collectGrantedPermissions(policy, context);
    const missingPermissions = gate.requiredPermissions.filter(
      (permission) => !grantedPermissions.has(permission),
    );
    const requirementMet =
      gate.match === 'all'
        ? missingPermissions.length === 0
        : gate.requiredPermissions.some((permission) =>
            grantedPermissions.has(permission),
          );

    return {
      actionId: context.actionId,
      allowed: requirementMet,
      effect: gate.effect,
      missingPermissions: requirementMet ? [] : missingPermissions,
      reasonCode: requirementMet ? undefined : 'PERMISSION_REQUIREMENT_NOT_MET',
      remediationHint: requirementMet ? undefined : gate.remediationHint,
    };
  }

  protected _collectGrantedPermissions(
    policy: IAdminPermissionPolicy,
    context: IAdminActionGateEvaluationContext,
  ): ReadonlySet<AdminPermissionTokenType> {
    const grantedPermissions = new Set<AdminPermissionTokenType>(
      context.additionalPermissions ?? [],
    );
    const requestedRoles = new Set(context.roleIds);

    for (const mapping of policy.roleMappings) {
      if (!requestedRoles.has(mapping.roleId)) {
        continue;
      }

      for (const permission of mapping.permissions) {
        grantedPermissions.add(permission);
      }
    }

    return grantedPermissions;
  }
}
