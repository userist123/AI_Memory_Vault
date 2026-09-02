import type { PlatformStartupPolicyType } from '@prosto/platform-sdk';
import type {
  IPolicyEvaluationInput,
  IPolicyEvaluationResult,
  IPolicyStrategy,
  IStartupPolicyEvaluator,
} from './interfaces/index.js';
import {
  BestEffortPolicyStrategy,
  StrictPolicyStrategy,
} from './strategies/index.js';

/**
 * @alpha
 * Startup policy evaluator class.
 * Manages policy strategies and evaluates startup policies.
 */
export class StartupPolicyEvaluator implements IStartupPolicyEvaluator {
  private readonly _strategies = new Map<
    PlatformStartupPolicyType,
    IPolicyStrategy
  >();

  constructor(
    policyStrategies: IPolicyStrategy[] = [
      new StrictPolicyStrategy(),
      new BestEffortPolicyStrategy(),
    ],
  ) {
    for (const strategy of policyStrategies) {
      this._strategies.set(strategy.policyMode, strategy);
    }
  }

  /**
   * Evaluate the policy for the given input.
   * Delegates to the appropriate strategy based on policy mode.
   */
  evaluate(input: IPolicyEvaluationInput): IPolicyEvaluationResult {
    const strategy = this.getStrategy(input.policyMode);

    if (!strategy) {
      return {
        action: 'abort',
        reason: `Unknown policy mode: "${input.policyMode}".`,
      };
    }

    return strategy.evaluate(input);
  }

  /**
   * Get the strategy for the given policy mode.
   */
  getStrategy(
    policyMode: PlatformStartupPolicyType,
  ): IPolicyStrategy | undefined {
    return this._strategies.get(policyMode);
  }

  /**
   * Register a new policy strategy.
   */
  registerStrategy(strategy: IPolicyStrategy): void {
    this._strategies.set(strategy.policyMode, strategy);
  }
}
