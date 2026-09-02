import type { RuntimeErrorCodes } from '@/common/index.js';
import type { IPlatformConfig } from '@/runtime/index.js';
import type { IConfigAccessPolicy } from './config-access-policy.interfaces.js';

/**
 * @alpha
 * Input for configuration access policy evaluation.
 */
export interface IConfigAccessEvaluationInput {
  /**
   * Module identifier for diagnostics.
   */
  readonly moduleId: string;

  /**
   * Whether running in production environment.
   */
  readonly isProduction: boolean;
}

/**
 * @alpha
 * Result of a configuration access policy evaluation.
 */
export interface IConfigAccessEvaluationResult {
  /**
   * Whether access was granted or denied.
   */
  readonly granted: boolean;

  /**
   * Denial code if access was denied, undefined otherwise.
   */
  readonly denialCode?:
    | RuntimeErrorCodes.ConfigAccessDenied
    | RuntimeErrorCodes.ConfigCapabilityInvalid
    | RuntimeErrorCodes.ConfigSectionNotAllowlisted
    | RuntimeErrorCodes.ConfigWildcardForbidden;

  /**
   * Human-readable explanation for diagnostics.
   */
  readonly reason: string;

  /**
   * Suggested remediation action for the user.
   */
  readonly remediationHint?: string;

  /**
   * The global sections the module is allowed to access.
   * Empty array if access was denied or no global access requested.
   */
  readonly allowedSections: readonly string[];
}

/**
 * @alpha
 * Contract for configuration access policy evaluation strategies.
 */
export interface IConfigAccessPolicyStrategy {
  /**
   * Evaluate configuration access for a module.
   * Returns granted/denied status with allowed sections.
   */
  evaluate(
    input: IConfigAccessEvaluationInput,
    policy: IConfigAccessPolicy,
    config: IPlatformConfig,
  ): IConfigAccessEvaluationResult;
}
