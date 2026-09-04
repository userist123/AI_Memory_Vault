import type { ExtensionConflictReasonType } from './extension-registry.types.js';

/**
 * @alpha
 * Rejection reason codes for shell plugin registry failures.
 */
export const SHELL_REJECTION_REASON_CODES = [
  'SHELL_VERSION_MISMATCH',
  'PLUGIN_LOAD_FAILED',
  'PERMISSION_DENIED',
  'CAPABILITY_DENIED',
  'EXTENSION_DUPLICATE_ID',
  'EXTENSION_DUPLICATE_ROUTE',
  'EXTENSION_DUPLICATE_SLOT',
  'EXTENSION_DUPLICATE_ACTION',
] as const;

export type ShellRejectionReasonCodeType =
  (typeof SHELL_REJECTION_REASON_CODES)[number];

/**
 * @alpha
 * Maps extension registry conflict reasons to shell rejection reason codes.
 */
export const EXTENSION_CONFLICT_REASON_MAP = {
  DUPLICATE_ID: 'EXTENSION_DUPLICATE_ID',
  DUPLICATE_ROUTE: 'EXTENSION_DUPLICATE_ROUTE',
  DUPLICATE_SLOT: 'EXTENSION_DUPLICATE_SLOT',
  DUPLICATE_ACTION: 'EXTENSION_DUPLICATE_ACTION',
} as const satisfies Record<
  ExtensionConflictReasonType,
  ShellRejectionReasonCodeType
>;

/**
 * @alpha
 * Map an extension conflict reason to a shell rejection reason code.
 */
export function mapExtensionConflictReason(
  reason: ExtensionConflictReasonType,
): ShellRejectionReasonCodeType {
  return EXTENSION_CONFLICT_REASON_MAP[reason];
}
