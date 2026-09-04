import type { PlatformStartupPolicyType } from '@prosto/platform-sdk';
import type {
  IPolicyEvaluationInput,
  IPolicyEvaluationResult,
  IPolicyStrategy,
} from './policy-strategy.interface.js';

/**
 * @alpha
 * Interface for the startup policy evaluator.
 */
export interface IStartupPolicyEvaluator {
  /**
   * Evaluate the policy for the given input.
   */
  evaluate(input: IPolicyEvaluationInput): IPolicyEvaluationResult;

  /**
   * Get the strategy for the given policy mode.
   */
  getStrategy(
    policyMode: PlatformStartupPolicyType,
  ): IPolicyStrategy | undefined;
}
