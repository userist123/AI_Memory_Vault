import type {
  ADMIN_BFF_HEALTH_STATUSES,
  ADMIN_BFF_REJECTION_REASON_CODES,
} from './admin-bff.constants.js';

/**
 * @alpha
 * Admin BFF rejection reason code union type.
 */
export type AdminBffRejectionReasonCodeType =
  (typeof ADMIN_BFF_REJECTION_REASON_CODES)[number];

/**
 * @alpha
 * Admin BFF health status union type.
 */
export type AdminBffHealthStatusType =
  (typeof ADMIN_BFF_HEALTH_STATUSES)[number];
