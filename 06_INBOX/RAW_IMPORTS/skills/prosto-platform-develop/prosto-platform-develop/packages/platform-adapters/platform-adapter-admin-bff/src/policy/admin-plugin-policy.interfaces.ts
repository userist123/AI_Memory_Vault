import type {
  AdminUIPluginReviewStatusType,
  AdminUIPluginTrustClassType,
} from '@prosto/platform-admin-contracts';

/**
 * @alpha
 * Policy evaluation result for a single admin UI plugin.
 */
export type AdminPluginPolicyResultType =
  | { allowed: true }
  | {
      allowed: false;
      reasonCode: string;
      message: string;
      remediationHint: string;
    };

/**
 * @alpha
 * Configuration entry for a single allowlist pattern.
 */
export interface IAdminPluginAllowlistEntry {
  readonly pluginIdPattern: string;
  readonly versionPattern?: string;
}

/**
 * @alpha
 * Evaluator contract for admin plugin allowlist policy.
 *
 * Determines whether a plugin is permitted by the environment-level
 * allowlist before admission into discovery payloads.
 */
export interface IAdminPluginAllowlistEvaluator {
  evaluate(
    pluginId: string,
    pluginVersion: string,
  ): AdminPluginPolicyResultType;
}

/**
 * @alpha
 * Configuration for admin plugin trust class filtering.
 */
export interface IAdminPluginTrustClassPolicyConfig {
  readonly allowedTrustClasses: readonly AdminUIPluginTrustClassType[];
  readonly environment?: string;
}

/**
 * @alpha
 * Evaluator contract for admin plugin trust class policy.
 *
 * Filters plugins based on their declared trust class against
 * environment-specific admission rules.
 */
export interface IAdminPluginTrustClassFilter {
  evaluate(
    trustClass: AdminUIPluginTrustClassType,
  ): AdminPluginPolicyResultType;
}

/**
 * @alpha
 * Configuration for admin plugin review status filtering.
 */
export interface IAdminPluginReviewStatusPolicyConfig {
  readonly allowedReviewStatuses: readonly AdminUIPluginReviewStatusType[];
}

/**
 * @alpha
 * Evaluator contract for admin plugin review status policy.
 *
 * Gates plugin admission based on the review lifecycle state.
 * Only plugins with an approved review status pass in production.
 */
export interface IAdminPluginReviewStatusFilter {
  evaluate(
    reviewStatus: AdminUIPluginReviewStatusType,
  ): AdminPluginPolicyResultType;
}
