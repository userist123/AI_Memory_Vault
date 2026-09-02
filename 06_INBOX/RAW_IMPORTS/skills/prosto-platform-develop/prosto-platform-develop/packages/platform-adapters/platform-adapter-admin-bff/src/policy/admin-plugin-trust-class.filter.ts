import type { AdminUIPluginTrustClassType } from '@prosto/platform-admin-contracts';
import { ADMIN_UI_PLUGIN_TRUST_CLASSES } from '@prosto/platform-admin-contracts';
import type {
  AdminPluginPolicyResultType,
  IAdminPluginTrustClassFilter,
  IAdminPluginTrustClassPolicyConfig,
} from './admin-plugin-policy.interfaces.js';

/**
 * @alpha
 * Default trust classes allowed in production environments.
 *
 * `third-party-reviewed` is excluded by default in production
 * per ADR-0003 security classification requirements.
 */
export const ADMIN_PLUGIN_DEFAULT_PRODUCTION_TRUST_CLASSES: readonly AdminUIPluginTrustClassType[] =
  ['trusted', 'internal'];

/**
 * @alpha
 * Admin UI plugin trust class filter.
 *
 * Filters plugins based on their declared trust class against
 * environment-specific admission rules. In production, only
 * `trusted` and `internal` plugins are admitted by default.
 */
export class AdminPluginTrustClassFilter implements IAdminPluginTrustClassFilter {
  private readonly _config: IAdminPluginTrustClassPolicyConfig;

  constructor(config: IAdminPluginTrustClassPolicyConfig) {
    this._config = config;
  }

  evaluate(
    trustClass: AdminUIPluginTrustClassType,
  ): AdminPluginPolicyResultType {
    if (!ADMIN_UI_PLUGIN_TRUST_CLASSES.includes(trustClass)) {
      return {
        allowed: false,
        reasonCode: 'TRUST_CLASS_REJECTED',
        message: `Unknown trust class "${trustClass}".`,
        remediationHint:
          'Use a valid trust class: trusted, internal, or third-party-reviewed.',
      };
    }

    if (!this._config.allowedTrustClasses.includes(trustClass)) {
      const envLabel = this._config.environment ?? 'current';

      return {
        allowed: false,
        reasonCode: 'TRUST_CLASS_REJECTED',
        message: `Trust class "${trustClass}" is not allowed in ${envLabel} environment.`,
        remediationHint: `Use a trust class from the allowed set: ${this._config.allowedTrustClasses.join(', ')}.`,
      };
    }

    return { allowed: true };
  }
}
