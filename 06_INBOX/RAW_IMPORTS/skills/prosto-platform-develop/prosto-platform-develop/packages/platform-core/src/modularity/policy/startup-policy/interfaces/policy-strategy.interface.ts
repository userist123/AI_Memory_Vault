import type { PlatformStartupPolicyType } from '@prosto/platform-sdk';

/**
 * @alpha
 * Input for policy evaluation.
 */
export interface IPolicyEvaluationInput {
  readonly policyMode: PlatformStartupPolicyType;
  readonly moduleId: string;
  readonly critical?: boolean;
}

/**
 * @alpha
 * Result of policy evaluation.
 */
export interface IPolicyEvaluationResult {
  readonly action: 'continue' | 'abort';
  readonly reason: string;
}

/**
 * @alpha
 * Policy strategy contract for evaluating startup policies.
 */
export interface IPolicyStrategy {
  /**
   * The policy mode this strategy handles.
   */
  readonly policyMode: PlatformStartupPolicyType;

  /**
   * Evaluate the policy for the given input.
   */
  evaluate(input: IPolicyEvaluationInput): IPolicyEvaluationResult;
}
