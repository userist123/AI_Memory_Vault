import type { AdminDiscoveryRejectionReasonCodeType } from '@prosto/platform-admin-contracts';
import type { IPlatformDelegatedIdentity } from '@prosto/platform-sdk';
import type { IAdminDiscoveryResult } from '../admin-bff.interfaces.js';
import type {
  IAdminDiagnosticsPayload,
  IAdminDiagnosticsPluginEntry,
} from './admin-diagnostics.types.js';

/**
 * @alpha
 * Configuration for the diagnostics service.
 */
export interface IAdminDiagnosticsServiceConfig {
  readonly environment?: string;
  readonly shellVersion?: string;
  readonly discoveryPipelineVersion: string;
  readonly enableDetailedLogging: boolean;
}

/**
 * @alpha
 * Request context for diagnostics generation.
 */
export interface IAdminDiagnosticsRequestContext {
  readonly correlationId: string;
  readonly identity: IPlatformDelegatedIdentity;
  readonly requestPath: string;
  readonly userAgent?: string;
  readonly clientIp?: string;
}

/**
 * @alpha
 * Service contract for generating structured diagnostics payloads.
 *
 * Produces detailed diagnostics with correlation metadata for
 * incident triage and operational analysis.
 */
export interface IAdminDiagnosticsService {
  /**
   * Generates a complete diagnostics payload from a discovery result.
   */
  generateDiagnosticsPayload(
    discoveryResult: IAdminDiscoveryResult,
    requestContext: IAdminDiagnosticsRequestContext,
  ): IAdminDiagnosticsPayload;

  /**
   * Creates a diagnostics entry for a single plugin.
   */
  createPluginEntry(
    pluginId: string,
    pluginVersion: string | undefined,
    status: 'accepted' | 'rejected' | 'filtered',
    reasonCode: AdminDiscoveryRejectionReasonCodeType | undefined,
    message: string | undefined,
    remediationHint: string | undefined,
    correlationId: string,
    subjectId: string,
  ): IAdminDiagnosticsPluginEntry;

  /**
   * Aggregates diagnostics from multiple discovery results.
   */
  aggregateDiagnostics(
    results: readonly IAdminDiscoveryResult[],
    requestContext: IAdminDiagnosticsRequestContext,
  ): IAdminDiagnosticsPayload;
}

/**
 * @alpha
 * Interface for persisting diagnostics entries for post-incident analysis.
 */
export interface IAdminDiagnosticsPersistence {
  /**
   * Persists a diagnostics payload for later retrieval.
   */
  persist(payload: IAdminDiagnosticsPayload): Promise<void>;

  /**
   * Retrieves diagnostics by correlation ID.
   */
  getByCorrelationId(
    correlationId: string,
  ): Promise<IAdminDiagnosticsPayload | undefined>;
}

/**
 * @alpha
 * Interface for filtering diagnostics based on operational policies.
 */
export interface IAdminDiagnosticsFilter {
  /**
   * Filters diagnostics entries based on configured criteria.
   */
  filter(
    entries: readonly IAdminDiagnosticsPluginEntry[],
  ): IAdminDiagnosticsPluginEntry[];
}
