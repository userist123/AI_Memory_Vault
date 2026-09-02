import type { AdminDiscoveryRejectionReasonCodeType } from '@prosto/platform-admin-contracts';

/**
 * @alpha
 * Diagnostics entry status for discovered plugins.
 */
export type AdminDiagnosticsPluginStatusType =
  | 'accepted'
  | 'rejected'
  | 'filtered';

/**
 * @alpha
 * Plugin diagnostics entry with correlation metadata.
 */
export interface IAdminDiagnosticsPluginEntry {
  readonly pluginId: string;
  readonly pluginVersion?: string;
  readonly status: AdminDiagnosticsPluginStatusType;
  readonly reasonCode?: AdminDiscoveryRejectionReasonCodeType;
  readonly message?: string;
  readonly remediationHint?: string;
  readonly timestamp: string;
  readonly correlationId: string;
  readonly subjectId: string;
  readonly environment?: string;
  readonly shellVersion?: string;
  readonly discoveryDuration?: number;
}

/**
 * @alpha
 * Aggregated diagnostics summary for incident triage.
 */
export interface IAdminDiagnosticsSummary {
  readonly acceptedCount: number;
  readonly rejectedCount: number;
  readonly filteredCount: number;
  readonly totalCount: number;
  readonly duration: number;
  readonly timestamp: string;
  readonly correlationId: string;
  readonly environment?: string;
}

/**
 * @alpha
 * Complete diagnostics payload for admin discovery.
 */
export interface IAdminDiagnosticsPayload {
  readonly schemaVersion: string;
  readonly generatedAt: string;
  readonly correlationId: string;
  readonly environment?: string;
  readonly shellVersion?: string;
  readonly plugins: readonly IAdminDiagnosticsPluginEntry[];
  readonly summary: IAdminDiagnosticsSummary;
  readonly metadata: IAdminDiagnosticsMetadata;
}

/**
 * @alpha
 * Additional metadata for diagnostics context.
 */
export interface IAdminDiagnosticsMetadata {
  readonly subjectId: string;
  readonly roles: readonly string[];
  readonly requestPath: string;
  readonly userAgent?: string;
  readonly clientIp?: string;
  readonly discoveryPipelineVersion: string;
}
