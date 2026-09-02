/**
 * @alpha
 * Rejection reason codes for admin BFF plugin discovery.
 */
export const ADMIN_BFF_REJECTION_REASON_CODES = [
  'MANIFEST_VALIDATION_FAILED',
  'COMPATIBILITY_REJECTED',
  'ALLOWLIST_REJECTED',
  'TRUST_CLASS_REJECTED',
  'REVIEW_STATUS_REJECTED',
  'PERMISSION_FILTERED',
] as const;

/**
 * @alpha
 * Admin BFF route path constants.
 */
export const ADMIN_BFF_ROUTES = {
  DISCOVERY: '/admin/api/v1/discovery',
  ACTION: '/admin/api/v1/action/:actionId',
  HEALTH: '/admin/api/v1/health',
  DIAGNOSTICS: '/admin/api/v1/diagnostics',
} as const;

/**
 * @alpha
 * Admin BFF health status constants.
 */
export const ADMIN_BFF_HEALTH_STATUSES = [
  'healthy',
  'degraded',
  'unhealthy',
] as const;
