/**
 * @alpha
 * Version identifier for the first admin compatibility evaluation contract.
 */
export const ADMIN_COMPATIBILITY_CONTRACT_VERSION =
  'admin-compatibility.v1' as const;

/**
 * @alpha
 * Stable taxonomy for admin plugin compatibility mismatch reasons.
 */
export const ADMIN_PLUGIN_COMPATIBILITY_REASON_CODES = [
  'SHELL_VERSION_MISMATCH',
  'CONTRACT_VERSION_MISMATCH',
  'PLUGIN_MANIFEST_INVALID',
  'PLUGIN_VERSION_INVALID',
  'SHELL_VERSION_INVALID',
] as const;
