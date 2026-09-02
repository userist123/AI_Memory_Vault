import type {
  IPolicyEvaluationInput,
  IPolicyEvaluationResult,
} from '../interfaces/index.js';
import { PolicyBaseStrategy } from './policy.base-strategy.js';

/**
 * @alpha
 * Best-effort policy strategy - continues startup even on module failures.
 * This policy allows the runtime to start in a degraded state,
 * skipping failed non-critical modules.
 */
export class BestEffortPolicyStrategy extends PolicyBaseStrategy {
  readonly policyMode = 'best-effort' as const;

  /**
   * Evaluate the policy for the given input.
   * In best-effort mode, only critical failures cause abort.
   */
  evaluate(input: IPolicyEvaluationInput): IPolicyEvaluationResult {
    if (input.critical) {
      return this.createAbortResult(
        `Module "${input.moduleId}" is critical and startup must abort on failure.`,
      );
    }

    return this.createContinueResult(input.moduleId);
  }
}
