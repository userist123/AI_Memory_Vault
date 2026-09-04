import type { PlatformStartupPolicyType } from '@prosto/platform-sdk';
import type {
  IPolicyEvaluationInput,
  IPolicyEvaluationResult,
  IPolicyStrategy,
} from '../interfaces/index.js';

/**
 * @alpha
 * Abstract base class for policy strategies.
 * Provides common functionality for evaluating startup policies.
 */
export abstract class PolicyBaseStrategy implements IPolicyStrategy {
  /**
   * The policy mode this strategy handles.
   */
  abstract readonly policyMode: PlatformStartupPolicyType;

  /**
   * Evaluate the policy for the given input.
   */
  abstract evaluate(input: IPolicyEvaluationInput): IPolicyEvaluationResult;

  /**
   * Check if this strategy can handle the given policy mode.
   */
  supports(policyMode: string): boolean {
    return this.policyMode === policyMode;
  }

  /**
   * Create a continue result.
   */
  protected createContinueResult(moduleId: string): IPolicyEvaluationResult {
    return {
      action: 'continue',
      reason: `Module "${moduleId}" can continue.`,
    };
  }

  /**
   * Create an abort result.
   */
  protected createAbortResult(reason: string): IPolicyEvaluationResult {
    return { action: 'abort', reason };
  }
}
