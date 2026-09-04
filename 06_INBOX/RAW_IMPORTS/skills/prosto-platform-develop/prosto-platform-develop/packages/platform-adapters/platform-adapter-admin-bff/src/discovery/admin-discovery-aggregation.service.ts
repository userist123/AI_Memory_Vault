import type {
  AdminCompatibilityContractVersionType,
  IAdminDiscoveredPluginDescriptor,
  IAdminPluginCompatibilityEvaluator,
  IAdminPluginCompatibilityInput,
  IAdminPluginDiscoveryExtensions,
  IAdminRejectedPluginDiagnostic,
  IAdminUIPluginManifest,
  IAdminUIPluginManifestValidator,
} from '@prosto/platform-admin-contracts';
import {
  ADMIN_COMPATIBILITY_CONTRACT_VERSION,
  ADMIN_DISCOVERY_PAYLOAD_SCHEMA_VERSION,
} from '@prosto/platform-admin-contracts';
import type {
  IAdminDiscoveryAggregationService,
  IAdminDiscoveryResult,
  IAdminPermissionMappingService,
  IAdminPluginCatalogSource,
} from '../admin-bff.interfaces.js';
import type { IPlatformDelegatedIdentity } from '@prosto/platform-sdk';
import { ADMIN_BFF_REJECTION_REASON_CODES } from '../admin-bff.constants.js';
import type {
  IAdminPluginAllowlistEvaluator,
  IAdminPluginReviewStatusFilter,
  IAdminPluginTrustClassFilter,
} from '../policy/admin-plugin-policy.interfaces.js';

/**
 * @alpha
 * Configuration options for the discovery aggregation service.
 */
export interface IAdminDiscoveryAggregationServiceConfig {
  readonly shellVersion: string;
  readonly supportedContractVersion: AdminCompatibilityContractVersionType;
}

/**
 * @alpha
 * Optional policy evaluators injected into the discovery pipeline.
 * All three are optional to preserve backward compatibility.
 */
export interface IAdminDiscoveryPolicyEvaluators {
  readonly allowlistEvaluator?: IAdminPluginAllowlistEvaluator;
  readonly trustClassFilter?: IAdminPluginTrustClassFilter;
  readonly reviewStatusFilter?: IAdminPluginReviewStatusFilter;
  readonly permissionService?: IAdminPermissionMappingService;
}

const EMPTY_EXTENSIONS: IAdminPluginDiscoveryExtensions = {
  navigation: [],
  pages: [],
  widgets: [],
  actions: [],
};

/**
 * @alpha
 * Default implementation of the admin plugin discovery aggregation service.
 *
 * Pulls plugin manifests from catalog sources, validates them against
 * admin contracts, evaluates compatibility, and applies policy checks
 * (allowlist, trust class, review status) before admitting plugins
 * into the discovery payload.
 */
export class AdminDiscoveryAggregationService implements IAdminDiscoveryAggregationService {
  private readonly _allowlistEvaluator?: IAdminPluginAllowlistEvaluator;
  private readonly _trustClassFilter?: IAdminPluginTrustClassFilter;
  private readonly _reviewStatusFilter?: IAdminPluginReviewStatusFilter;
  private readonly _permissionService?: IAdminPermissionMappingService;

  constructor(
    private readonly _catalogSource: IAdminPluginCatalogSource,
    private readonly _manifestValidator: IAdminUIPluginManifestValidator,
    private readonly _compatibilityEvaluator: IAdminPluginCompatibilityEvaluator,
    private readonly _config: IAdminDiscoveryAggregationServiceConfig,
    policyEvaluators?: IAdminDiscoveryPolicyEvaluators,
  ) {
    this._allowlistEvaluator = policyEvaluators?.allowlistEvaluator;
    this._trustClassFilter = policyEvaluators?.trustClassFilter;
    this._reviewStatusFilter = policyEvaluators?.reviewStatusFilter;
    this._permissionService = policyEvaluators?.permissionService;
  }

  async discover(
    identity: IPlatformDelegatedIdentity,
  ): Promise<IAdminDiscoveryResult> {
    const startTime = Date.now();

    const rawManifests = await this._catalogSource.fetchUIPluginManifests();

    const accepted: IAdminDiscoveredPluginDescriptor[] = [];
    const rejected: IAdminRejectedPluginDiagnostic[] = [];

    for (const rawManifest of rawManifests) {
      const validationResult = this._manifestValidator.validate(rawManifest);

      if (!validationResult.success) {
        rejected.push({
          reasonCode: ADMIN_BFF_REJECTION_REASON_CODES[0],
          message: validationResult.error.message,
          remediationHint: 'Fix manifest validation errors and republish.',
          details: validationResult.error.issues.reduce(
            (acc, issue) => {
              acc[issue.path] = issue.message;
              return acc;
            },
            {} as Record<string, string>,
          ),
        });
        continue;
      }

      const manifest = validationResult.manifest;

      const compatibilityInput: IAdminPluginCompatibilityInput = {
        shellVersion: this._config.shellVersion,
        supportedContractVersion: this._config.supportedContractVersion,
        pluginContractVersion: ADMIN_COMPATIBILITY_CONTRACT_VERSION,
        manifest,
      };

      const compatibilityResult =
        this._compatibilityEvaluator.evaluate(compatibilityInput);

      if (!compatibilityResult.allowed) {
        rejected.push({
          id: manifest.id,
          version: manifest.version,
          reasonCode: compatibilityResult.reasonCode,
          message: compatibilityResult.message,
          remediationHint: compatibilityResult.remediationHint,
        });
        continue;
      }

      const policyRejection = this._evaluatePolicyChecks(manifest);

      if (policyRejection) {
        rejected.push({
          id: manifest.id,
          version: manifest.version,
          ...policyRejection,
        });
        continue;
      }

      const permissionRejection = this._evaluatePermissionChecks(
        manifest,
        identity,
      );

      if (permissionRejection) {
        rejected.push({
          id: manifest.id,
          version: manifest.version,
          ...permissionRejection,
        });
        continue;
      }

      accepted.push(this._mapManifestToDescriptor(manifest));
    }

    const duration = Date.now() - startTime;

    return {
      payload: {
        schemaVersion: ADMIN_DISCOVERY_PAYLOAD_SCHEMA_VERSION,
        generatedAt: new Date().toISOString(),
        plugins: accepted,
        rejected,
      },
      diagnostics: {
        acceptedCount: accepted.length,
        rejectedCount: rejected.length,
        duration,
      },
    };
  }

  /**
   * Evaluates permission requirements against the identity's granted permissions.
   * Returns rejection metadata if the identity lacks required permissions, or undefined if all pass.
   */
  private _evaluatePermissionChecks(
    manifest: IAdminUIPluginManifest,
    identity: IPlatformDelegatedIdentity,
  ): Omit<IAdminRejectedPluginDiagnostic, 'id' | 'version'> | undefined {
    if (!this._permissionService) {
      return undefined;
    }

    const requiredPermissions = manifest.requiredPermissions;

    if (!requiredPermissions.length) {
      return undefined;
    }

    const { allowed, missingPermissions } =
      this._permissionService.filterPermissions(requiredPermissions, identity);

    if (!allowed) {
      return {
        reasonCode: ADMIN_BFF_REJECTION_REASON_CODES[5],
        message: `Operator lacks required permissions: ${missingPermissions.join(', ')}.`,
        remediationHint:
          'Request the required permissions from an administrator.',
        details: {
          requiredPermissions: requiredPermissions.join(', '),
          missingPermissions: missingPermissions.join(', '),
        },
      };
    }

    return undefined;
  }

  /**
   * Runs all configured policy checks against a validated manifest.
   * Returns the first rejection encountered, or undefined if all checks pass.
   */
  private _evaluatePolicyChecks(
    manifest: IAdminUIPluginManifest,
  ): Omit<IAdminRejectedPluginDiagnostic, 'id' | 'version'> | undefined {
    if (this._allowlistEvaluator) {
      const allowlistResult = this._allowlistEvaluator.evaluate(
        manifest.id,
        manifest.version,
      );

      if (!allowlistResult.allowed) {
        return {
          reasonCode: allowlistResult.reasonCode,
          message: allowlistResult.message,
          remediationHint: allowlistResult.remediationHint,
        };
      }
    }

    if (this._trustClassFilter) {
      const trustResult = this._trustClassFilter.evaluate(manifest.trustClass);

      if (!trustResult.allowed) {
        return {
          reasonCode: trustResult.reasonCode,
          message: trustResult.message,
          remediationHint: trustResult.remediationHint,
        };
      }
    }

    if (this._reviewStatusFilter) {
      const reviewResult = this._reviewStatusFilter.evaluate(
        manifest.reviewStatus,
      );

      if (!reviewResult.allowed) {
        return {
          reasonCode: reviewResult.reasonCode,
          message: reviewResult.message,
          remediationHint: reviewResult.remediationHint,
        };
      }
    }

    return undefined;
  }

  private _mapManifestToDescriptor(
    manifest: IAdminUIPluginManifest,
  ): IAdminDiscoveredPluginDescriptor {
    return {
      id: manifest.id,
      version: manifest.version,
      displayName: manifest.displayName,
      shellCompatibility: manifest.shellCompatibility,
      trustClass: manifest.trustClass,
      reviewStatus: manifest.reviewStatus,
      extensions: EMPTY_EXTENSIONS,
      metadata: manifest.metadata,
    };
  }
}
