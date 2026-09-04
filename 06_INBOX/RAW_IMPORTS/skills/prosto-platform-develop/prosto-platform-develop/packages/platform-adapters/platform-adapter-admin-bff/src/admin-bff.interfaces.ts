import type {
  IAdminDiscoveredPluginDescriptor,
  IAdminRejectedPluginDiagnostic,
} from '@prosto/platform-admin-contracts';
import type {
  IPlatformDelegatedIdentity,
  IPlatformHttpRouteContext,
} from '@prosto/platform-sdk';
import type { IAdminDiagnosticsService } from './diagnostics/index.js';
import type { IAdminBffLogger } from './observability/admin-bff-logger.interface.js';

/**
 * @alpha
 * Execution context injected into every admin BFF route handler.
 * Extends SDK route context and narrows identity to delegated identity.
 */
export interface IAdminBffRouteContext extends IPlatformHttpRouteContext {
  readonly identity: IPlatformDelegatedIdentity;
  readonly discoveryService: IAdminDiscoveryAggregationService;
  readonly permissionService: IAdminPermissionMappingService;
  readonly diagnosticsService: IAdminDiagnosticsService;
  readonly logger: IAdminBffLogger;
}

/**
 * @alpha
 * Aggregation service contract for admin plugin discovery.
 */
export interface IAdminDiscoveryAggregationService {
  discover(
    identity: IPlatformDelegatedIdentity,
  ): Promise<IAdminDiscoveryResult>;
}

/**
 * @alpha
 * Permission filtering result for plugin permission checks.
 */
export interface IAdminPermissionFilterResult {
  readonly allowed: boolean;
  readonly missingPermissions: readonly string[];
}

/**
 * @alpha
 * Permission mapping service contract for admin action gating.
 */
export interface IAdminPermissionMappingService {
  evaluateAction(
    actionId: string,
    identity: IPlatformDelegatedIdentity,
  ): IAdminActionEvaluationResult;

  filterPermissions(
    requiredPermissions: readonly string[],
    identity: IPlatformDelegatedIdentity,
  ): IAdminPermissionFilterResult;
}

/**
 * @alpha
 * Contract for fetching raw UI plugin manifests from catalog sources.
 */
export interface IAdminPluginCatalogSource {
  fetchUIPluginManifests(): Promise<readonly unknown[]>;
}

/**
 * @alpha
 * Result of a discovery aggregation pipeline.
 */
export interface IAdminDiscoveryResult {
  readonly payload: IAdminDiscoveryPayloadResult;
  readonly diagnostics: IAdminDiscoveryDiagnostics;
}

/**
 * @alpha
 * The discovery payload returned to the admin shell.
 */
export interface IAdminDiscoveryPayloadResult {
  readonly schemaVersion: string;
  readonly generatedAt: string;
  readonly plugins: readonly IAdminDiscoveredPluginDescriptor[];
  readonly rejected: readonly IAdminRejectedPluginDiagnostic[];
}

/**
 * @alpha
 * Diagnostics metadata for the discovery operation.
 */
export interface IAdminDiscoveryDiagnostics {
  readonly acceptedCount: number;
  readonly rejectedCount: number;
  readonly duration: number;
}

/**
 * @alpha
 * Result of an action permission evaluation.
 */
export interface IAdminActionEvaluationResult {
  readonly allowed: boolean;
  readonly actionId: string;
  readonly reasonCode?: string;
  readonly remediationHint?: string;
}
