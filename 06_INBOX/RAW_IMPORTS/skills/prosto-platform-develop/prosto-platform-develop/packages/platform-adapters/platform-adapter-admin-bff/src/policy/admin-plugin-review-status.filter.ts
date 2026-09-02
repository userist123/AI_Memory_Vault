import type { AdminUIPluginReviewStatusType } from '@prosto/platform-admin-contracts';
import { ADMIN_UI_PLUGIN_REVIEW_STATUSES } from '@prosto/platform-admin-contracts';
import type {
  AdminPluginPolicyResultType,
  IAdminPluginReviewStatusFilter,
  IAdminPluginReviewStatusPolicyConfig,
} from './admin-plugin-policy.interfaces.js';

/**
 * @alpha
 * Default review statuses allowed for plugin admission.
 *
 * Only `approved` plugins pass by default; `pending`, `rejected`,
 * and `revoked` are blocked per ADR-0009 admission requirements.
 */
export const ADMIN_PLUGIN_DEFAULT_ALLOWED_REVIEW_STATUSES: readonly AdminUIPluginReviewStatusType[] =
  ['approved'];

/**
 * @alpha
 * Admin UI plugin review status filter.
 *
 * Gates plugin admission based on the review lifecycle state.
 * Only plugins with an approved review status are admitted by default.
 */
export class AdminPluginReviewStatusFilter implements IAdminPluginReviewStatusFilter {
  private readonly _config: IAdminPluginReviewStatusPolicyConfig;

  constructor(config: IAdminPluginReviewStatusPolicyConfig) {
    this._config = config;
  }

  evaluate(
    reviewStatus: AdminUIPluginReviewStatusType,
  ): AdminPluginPolicyResultType {
    if (!ADMIN_UI_PLUGIN_REVIEW_STATUSES.includes(reviewStatus)) {
      return {
        allowed: false,
        reasonCode: 'REVIEW_STATUS_REJECTED',
        message: `Unknown review status "${reviewStatus}".`,
        remediationHint:
          'Use a valid review status: pending, approved, rejected, or revoked.',
      };
    }

    if (!this._config.allowedReviewStatuses.includes(reviewStatus)) {
      return {
        allowed: false,
        reasonCode: 'REVIEW_STATUS_REJECTED',
        message: `Review status "${reviewStatus}" does not permit plugin admission.`,
        remediationHint: `Plugin must have one of the following review statuses: ${this._config.allowedReviewStatuses.join(', ')}.`,
      };
    }

    return { allowed: true };
  }
}
