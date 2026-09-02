import type {
  ADMIN_COMPATIBILITY_CONTRACT_VERSION,
  ADMIN_PLUGIN_COMPATIBILITY_REASON_CODES,
} from './admin-compatibility.constants.js';

/**
 * @alpha
 * Versioned admin compatibility evaluator contract discriminator.
 */
export type AdminCompatibilityContractVersionType =
  typeof ADMIN_COMPATIBILITY_CONTRACT_VERSION;

/**
 * @alpha
 * Stable reason code taxonomy for admin plugin compatibility decisions.
 */
export type AdminPluginCompatibilityReasonCodeType =
  (typeof ADMIN_PLUGIN_COMPATIBILITY_REASON_CODES)[number];
