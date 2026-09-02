/**
 * @alpha
 * Module identifier for structured logging in the admin shell.
 */
export const ADMIN_SHELL_MODULE_ID = 'platform-admin-shell' as const;

/**
 * @alpha
 * Lifecycle phase identifiers for admin shell observability.
 */
export const AdminShellPhase = {
  SHELL_STARTUP: 'shell_startup',
  DISCOVERY: 'discovery',
  DISCOVERY_FETCH: 'discovery_fetch',
  DISCOVERY_VALIDATE: 'discovery_validate',
  PLUGIN_COMPATIBILITY: 'plugin_compatibility',
  PLUGIN_PERMISSIONS: 'plugin_permissions',
  PLUGIN_REGISTRATION: 'plugin_registration',
  PLUGIN_LOAD: 'plugin_load',
  EXTENSION_REGISTRATION: 'extension_registration',
  DEGRADED_MODE: 'degraded_mode',
  TELEMETRY_FLUSH: 'telemetry_flush',
} as const;

/**
 * @alpha
 * Log event names for admin shell observability.
 */
export const AdminShellLogEvents = {
  SHELL_STARTUP_STARTED: 'shell_startup_started',
  SHELL_STARTUP_COMPLETED: 'shell_startup_completed',
  SHELL_STARTUP_FAILED: 'shell_startup_failed',
  DISCOVERY_STARTED: 'discovery_started',
  DISCOVERY_COMPLETED: 'discovery_completed',
  DISCOVERY_FAILED: 'discovery_failed',
  PLUGIN_COMPATIBILITY_CHECKED: 'plugin_compatibility_checked',
  PLUGIN_PERMISSION_GRANTED: 'plugin_permission_granted',
  PLUGIN_PERMISSION_DENIED: 'plugin_permission_denied',
  PLUGIN_REGISTERED: 'plugin_registered',
  PLUGIN_LOAD_STARTED: 'plugin_load_started',
  PLUGIN_LOAD_COMPLETED: 'plugin_load_completed',
  PLUGIN_LOAD_FAILED: 'plugin_load_failed',
  PLUGIN_REJECTED: 'plugin_rejected',
  EXTENSION_REGISTERED: 'extension_registered',
  EXTENSION_REGISTRATION_CONFLICT: 'extension_registration_conflict',
  EXTENSION_REMOVED: 'extension_removed',
  DEGRADED_MODE_ENTERED: 'degraded_mode_entered',
  DEGRADED_MODE_EXITED: 'degraded_mode_exited',
  TELEMETRY_SNAPSHOT_RECORDED: 'telemetry_snapshot_recorded',
} as const;

/**
 * @alpha
 * Error code taxonomy for admin shell observability.
 */
export const AdminShellErrorCodes = {
  DISCOVERY_NETWORK_ERROR: 'ADMIN_SHELL_DISCOVERY_NETWORK_ERROR',
  DISCOVERY_TIMEOUT: 'ADMIN_SHELL_DISCOVERY_TIMEOUT',
  DISCOVERY_HTTP_ERROR: 'ADMIN_SHELL_DISCOVERY_HTTP_ERROR',
  DISCOVERY_VALIDATION_FAILED: 'ADMIN_SHELL_DISCOVERY_VALIDATION_FAILED',
  PLUGIN_COMPATIBILITY_FAILED: 'ADMIN_SHELL_PLUGIN_COMPATIBILITY_FAILED',
  PLUGIN_PERMISSION_DENIED: 'ADMIN_SHELL_PLUGIN_PERMISSION_DENIED',
  PLUGIN_LOAD_FAILED: 'ADMIN_SHELL_PLUGIN_LOAD_FAILED',
  PLUGIN_NO_ENTRY_POINT: 'ADMIN_SHELL_PLUGIN_NO_ENTRY_POINT',
  EXTENSION_CONFLICT: 'ADMIN_SHELL_EXTENSION_CONFLICT',
  SHELL_STARTUP_FAILED: 'ADMIN_SHELL_STARTUP_FAILED',
} as const;

/**
 * @alpha
 * Telemetry metric names for admin shell plugin load outcomes.
 */
export const AdminShellTelemetryMetrics = {
  PLUGIN_LOAD_DURATION_MS: 'admin_shell_plugin_load_duration_ms',
  PLUGIN_LOAD_OUTCOME: 'admin_shell_plugin_load_outcome',
  DISCOVERY_DURATION_MS: 'admin_shell_discovery_duration_ms',
  DISCOVERY_OUTCOME: 'admin_shell_discovery_outcome',
  EXTENSION_COUNT: 'admin_shell_extension_count',
  SHELL_STARTUP_DURATION_MS: 'admin_shell_startup_duration_ms',
  SHELL_STARTUP_OUTCOME: 'admin_shell_startup_outcome',
  DEGRADED_MODE_ACTIVE: 'admin_shell_degraded_mode_active',
  UI_EXTENSION_USED: 'admin_shell_ui_extension_used',
} as const;
