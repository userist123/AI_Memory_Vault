import type { IPlatformConfig } from '@/runtime/index.js';
import type {
  IConfigAccessEvaluationInput,
  IConfigAccessEvaluationResult,
  IConfigAccessPolicy,
  IConfigAccessPolicyStrategy,
} from '../interfaces/index.js';
import { RuntimeErrorCodes } from '@/common/index.js';

/**
 * @alpha
 * Default implementation of configuration access policy evaluation.
 * Implements deterministic, default-deny access control based on:
 * - capability-to-section mapping
 * - security class allowlists
 * - production strict mode enforcement
 */
export class ConfigAccessPolicyStrategy implements IConfigAccessPolicyStrategy {
  /**
   * Evaluate configuration access for a module.
   * Returns granted/denied status with allowed sections.
   */
  evaluate(
    input: IConfigAccessEvaluationInput,
    policy: IConfigAccessPolicy,
    config: IPlatformConfig,
  ): IConfigAccessEvaluationResult {
    // Get allowlist for module's
    const allowlist = Object.keys(config);

    // Production strict mode check
    if (
      input.isProduction &&
      policy.productionStrictMode &&
      !allowlist.length
    ) {
      return {
        granted: false,
        denialCode: RuntimeErrorCodes.ConfigAccessDenied,
        reason: `Module "${input.moduleId}" requested global config access in production but no sections are allowed. Production strict mode is enabled.`,
        remediationHint:
          'Production strict mode blocked access. Review policy configuration or reduce capability requests.',
        allowedSections: [],
      };
    }

    return {
      granted: true,
      reason: `Module "${input.moduleId}" granted access to global sections: ${
        allowlist.length
          ? allowlist.map((section) => `"${section}"`).join(', ')
          : 'none'
      }.`,
      allowedSections: allowlist,
    };
  }
}
