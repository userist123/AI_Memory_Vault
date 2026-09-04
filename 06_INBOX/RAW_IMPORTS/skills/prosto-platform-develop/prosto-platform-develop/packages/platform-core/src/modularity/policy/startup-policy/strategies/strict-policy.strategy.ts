import type {
  IPolicyEvaluationInput,
  IPolicyEvaluationResult,
} from '../interfaces/index.js';
import { PolicyBaseStrategy } from './policy.base-strategy.js';

/**
 * @alpha
 * Strict policy strategy - aborts startup on any module failure.
 * This is the safest policy, ensuring all modules start successfully
 * or the entire runtime aborts.
 */
export class StrictPolicyStrategy extends PolicyBaseStrategy {
  readonly policyMode = 'strict' as const;

  /**
   * Evaluate the policy for the given input.
   * In strict mode, any failure results in abort.
   */
  evaluate(input: IPolicyEvaluationInput): IPolicyEvaluationResult {
    if (input.critical) {
      return this.createAbortResult(
        `Module "${input.moduleId}" is critical and startup must abort on failure.`,
      );
    }

    return this.createAbortResult(
      `Startup policy is strict and module "${input.moduleId}" failed.`,
    );
  }
}
