import { describe, expect, it } from 'vitest';
import {
  ADMIN_PERMISSION_POLICY_SCHEMA_VERSION,
  AdminActionGateEvaluator,
  AdminPermissionPolicyValidationError,
  AdminPermissionPolicyValidator,
  type IAdminPermissionPolicy,
} from '@/index.js';

const validPolicy: IAdminPermissionPolicy = {
  schemaVersion: ADMIN_PERMISSION_POLICY_SCHEMA_VERSION,
  roleMappings: [
    {
      roleId: 'admin.operator',
      permissions: ['health.read', 'health.refresh'],
      description: 'Operational health dashboard role.',
    },
  ],
  actionGates: [
    {
      actionId: 'health.refresh',
      requiredPermissions: ['health.refresh'],
      match: 'all',
      effect: 'allow',
      remediationHint: 'Assign the admin.operator role.',
    },
  ],
};

describe('admin permission policy validation', () => {
  const policyValidator = new AdminPermissionPolicyValidator();

  it('accepts a valid permission policy', () => {
    const parsedPolicy = policyValidator.parse(validPolicy);

    expect(parsedPolicy.roleMappings[0]?.roleId).toBe('admin.operator');
    expect(parsedPolicy.actionGates[0]?.actionId).toBe('health.refresh');
  });

  it('returns failure for schema violations', () => {
    const result = policyValidator.validate({
      ...validPolicy,
      roleMappings: [
        {
          roleId: 'Admin Operator',
          permissions: ['health.read'],
        },
      ],
    });

    expect(result.success).toBe(false);

    if (result.success) {
      throw new Error('Expected validation failure.');
    }

    expect(result.error).toBeInstanceOf(AdminPermissionPolicyValidationError);
    expect(
      result.error.issues.some(
        (issue) => issue.path === 'roleMappings.0.roleId',
      ),
    ).toBe(true);
  });

  it('returns failure for duplicate action gates', () => {
    const result = policyValidator.validate({
      ...validPolicy,
      actionGates: [
        ...validPolicy.actionGates,
        {
          actionId: 'health.refresh',
          requiredPermissions: ['health.read'],
          match: 'all',
          effect: 'allow',
        },
      ],
    });

    expect(result.success).toBe(false);

    if (result.success) {
      throw new Error('Expected validation failure.');
    }

    expect(
      result.error.issues.some(
        (issue) => issue.code === 'duplicate_action_gate',
      ),
    ).toBe(true);
  });
});

describe('admin action gate evaluator', () => {
  const evaluator = new AdminActionGateEvaluator();

  it('allows an action when mapped role grants required permissions', () => {
    const decision = evaluator.evaluate(validPolicy, {
      actionId: 'health.refresh',
      roleIds: ['admin.operator'],
    });

    expect(decision.allowed).toBe(true);
    expect(decision.missingPermissions).toEqual([]);
  });

  it('rejects an action when required permissions are missing', () => {
    const decision = evaluator.evaluate(validPolicy, {
      actionId: 'health.refresh',
      roleIds: [],
    });

    expect(decision.allowed).toBe(false);
    expect(decision.reasonCode).toBe('PERMISSION_REQUIREMENT_NOT_MET');
    expect(decision.missingPermissions).toEqual(['health.refresh']);
  });
});
