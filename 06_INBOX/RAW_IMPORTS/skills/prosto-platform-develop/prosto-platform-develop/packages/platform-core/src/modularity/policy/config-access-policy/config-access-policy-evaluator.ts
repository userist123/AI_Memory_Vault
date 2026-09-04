import type { IPlatformConfig } from '@/runtime/index.js';
import type {
  IConfigAccessEvaluationInput,
  IConfigAccessEvaluationResult,
  IConfigAccessPolicy,
  IConfigAccessPolicyEvaluator,
  IConfigAccessPolicyStrategy,
} from './interfaces/index.js';
import { ConfigAccessPolicyStrategy } from './strategies/index.js';

/**
 * @alpha
 * Evaluator for module configuration access policies.
 * Determines which configuration sections a module is allowed to access
 * based on its declared capabilities, security class, and runtime policy.
 */
export class ConfigAccessPolicyEvaluator implements IConfigAccessPolicyEvaluator {
  constructor(
    private readonly _strategy: IConfigAccessPolicyStrategy = new ConfigAccessPolicyStrategy(),
  ) {}

  evaluate(
    input: IConfigAccessEvaluationInput,
    policy: IConfigAccessPolicy,
    config: IPlatformConfig,
  ): IConfigAccessEvaluationResult {
    return this._strategy.evaluate(input, policy, config);
  }
}
