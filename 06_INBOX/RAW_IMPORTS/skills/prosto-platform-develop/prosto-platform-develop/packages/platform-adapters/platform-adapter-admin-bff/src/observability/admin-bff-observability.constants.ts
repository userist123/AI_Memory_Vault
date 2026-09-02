/**
 * @alpha
 * Module identifier for structured logging in the admin BFF adapter.
 */
export const ADMIN_BFF_MODULE_ID = 'platform-adapter-admin-bff' as const;

/**
 * @alpha
 * Lifecycle phase identifiers for admin BFF observability.
 */
export const AdminBffPhase = {
  REQUEST: 'request',
  ROUTE_MATCH: 'route_match',
  DISCOVERY: 'discovery',
  DISCOVERY_FETCH: 'discovery_fetch',
  DISCOVERY_VALIDATE: 'discovery_validate',
  DISCOVERY_COMPATIBILITY: 'discovery_compatibility',
  DISCOVERY_POLICY: 'discovery_policy',
  DISCOVERY_PERMISSIONS: 'discovery_permissions',
  ACTION_EVALUATION: 'action_evaluation',
  HEALTH_CHECK: 'health_check',
  DIAGNOSTICS: 'diagnostics',
} as const;

/**
 * @alpha
 * Log event names for admin BFF observability.
 */
export const AdminBffLogEvents = {
  REQUEST_RECEIVED: 'request_received',
  REQUEST_COMPLETED: 'request_completed',
  REQUEST_FAILED: 'request_failed',
  ROUTE_NOT_FOUND: 'route_not_found',
  HANDLER_START: 'handler_start',
  HANDLER_COMPLETED: 'handler_completed',
  HANDLER_FAILED: 'handler_failed',
  DISCOVERY_STARTED: 'discovery_started',
  DISCOVERY_COMPLETED: 'discovery_completed',
  DISCOVERY_FAILED: 'discovery_failed',
  PLUGIN_ACCEPTED: 'plugin_accepted',
  PLUGIN_REJECTED: 'plugin_rejected',
  ACTION_EVALUATED: 'action_evaluated',
  HEALTH_CHECK_RESULT: 'health_check_result',
  DIAGNOSTICS_GENERATED: 'diagnostics_generated',
  CATALOG_FETCH_STARTED: 'catalog_fetch_started',
  CATALOG_FETCH_COMPLETED: 'catalog_fetch_completed',
  CATALOG_FETCH_FAILED: 'catalog_fetch_failed',
} as const;

/**
 * @alpha
 * Error code taxonomy for admin BFF observability.
 */
export const AdminBffErrorCodes = {
  ROUTE_NOT_FOUND: 'ADMIN_BFF_ROUTE_NOT_FOUND',
  HANDLER_FAILED: 'ADMIN_BFF_HANDLER_FAILED',
  DISCOVERY_FAILED: 'ADMIN_BFF_DISCOVERY_FAILED',
  CATALOG_FETCH_FAILED: 'ADMIN_BFF_CATALOG_FETCH_FAILED',
  PERMISSION_DENIED: 'ADMIN_BFF_PERMISSION_DENIED',
  VALIDATION_FAILED: 'ADMIN_BFF_VALIDATION_FAILED',
} as const;
