import type { IPlatformConfig } from '@/runtime/index.js';
import type { IConfigAccessPolicy } from './config-access-policy.interfaces.js';
import type {
  IConfigAccessEvaluationInput,
  IConfigAccessEvaluationResult,
} from './config-access-policy-strategy.interface.js';

/**
 * @alpha
 * Contract for evaluating configuration access policies.
 */
export interface IConfigAccessPolicyEvaluator {
  /**
   * Evaluate configuration access for a module.
   */
  evaluate(
    input: IConfigAccessEvaluationInput,
    policy: IConfigAccessPolicy,
    config: IPlatformConfig,
  ): IConfigAccessEvaluationResult;
}
