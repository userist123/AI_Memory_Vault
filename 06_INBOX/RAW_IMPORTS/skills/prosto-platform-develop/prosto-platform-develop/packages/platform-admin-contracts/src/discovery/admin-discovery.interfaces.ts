import type {
  AdminShellCompatibilityRangeType,
  AdminUIPluginIdentifierType,
  AdminUIPluginReviewStatusType,
  AdminUIPluginTrustClassType,
  AdminUIPluginVersionType,
} from '../manifests/index.js';
import type {
  AdminDiscoveryActionTargetType,
  AdminDiscoveryComponentKeyType,
  AdminDiscoveryDescriptorIdentifierType,
  AdminDiscoveryPayloadSchemaVersionType,
  AdminDiscoveryRejectionReasonCodeType,
  AdminDiscoveryRouteType,
  AdminDiscoveryWidgetSlotType,
} from './admin-discovery.types.js';

/**
 * @alpha
 * Shared metadata for extension descriptors exposed through admin discovery.
 */
export interface IAdminExtensionDescriptorMetadata {
  readonly id: AdminDiscoveryDescriptorIdentifierType;
  readonly pluginId: AdminUIPluginIdentifierType;
  readonly order?: number;
  readonly metadata?: Readonly<Record<string, string>>;
}

/**
 * @alpha
 * Navigation extension descriptor rendered by the admin shell navigation registry.
 */
export interface IAdminNavigationExtensionDescriptor extends IAdminExtensionDescriptorMetadata {
  readonly label: string;
  readonly icon?: string;
  readonly parentId?: AdminDiscoveryDescriptorIdentifierType;
  readonly pageId?: AdminDiscoveryDescriptorIdentifierType;
}

/**
 * @alpha
 * Page extension descriptor resolved through the admin shell page registry.
 */
export interface IAdminPageExtensionDescriptor extends IAdminExtensionDescriptorMetadata {
  readonly route: AdminDiscoveryRouteType;
  readonly title: string;
  readonly componentKey: AdminDiscoveryComponentKeyType;
  readonly navigationId?: AdminDiscoveryDescriptorIdentifierType;
}

/**
 * @alpha
 * Widget extension descriptor resolved through the admin shell widget registry.
 */
export interface IAdminWidgetExtensionDescriptor extends IAdminExtensionDescriptorMetadata {
  readonly slot: AdminDiscoveryWidgetSlotType;
  readonly componentKey: AdminDiscoveryComponentKeyType;
  readonly title?: string;
}

/**
 * @alpha
 * Action extension descriptor resolved through the admin shell action registry.
 */
export interface IAdminActionExtensionDescriptor extends IAdminExtensionDescriptorMetadata {
  readonly target: AdminDiscoveryActionTargetType;
  readonly label: string;
  readonly actionKey: AdminDiscoveryComponentKeyType;
  readonly confirmationRequired?: boolean;
}

/**
 * @alpha
 * Grouped extension descriptors contributed by a discovered admin UI plugin.
 */
export interface IAdminPluginDiscoveryExtensions {
  readonly navigation: readonly IAdminNavigationExtensionDescriptor[];
  readonly pages: readonly IAdminPageExtensionDescriptor[];
  readonly widgets: readonly IAdminWidgetExtensionDescriptor[];
  readonly actions: readonly IAdminActionExtensionDescriptor[];
}

/**
 * @alpha
 * Plugin descriptor admitted into the admin discovery payload.
 */
export interface IAdminDiscoveredPluginDescriptor {
  readonly id: AdminUIPluginIdentifierType;
  readonly version: AdminUIPluginVersionType;
  readonly displayName?: string;
  readonly shellCompatibility: AdminShellCompatibilityRangeType;
  readonly trustClass: AdminUIPluginTrustClassType;
  readonly reviewStatus: AdminUIPluginReviewStatusType;
  readonly extensions: IAdminPluginDiscoveryExtensions;
  readonly metadata?: Readonly<Record<string, string>>;
}

/**
 * @alpha
 * Diagnostic entry for an admin UI plugin rejected before shell exposure.
 */
export interface IAdminRejectedPluginDiagnostic {
  readonly id?: AdminUIPluginIdentifierType;
  readonly version?: AdminUIPluginVersionType;
  readonly reasonCode: AdminDiscoveryRejectionReasonCodeType;
  readonly message: string;
  readonly remediationHint: string;
  readonly details?: Readonly<Record<string, string>>;
}

/**
 * @alpha
 * Versioned discovery payload consumed by the separate admin shell.
 */
export interface IAdminDiscoveryPayload {
  readonly schemaVersion: AdminDiscoveryPayloadSchemaVersionType;
  readonly generatedAt: string;
  readonly plugins: readonly IAdminDiscoveredPluginDescriptor[];
  readonly rejected: readonly IAdminRejectedPluginDiagnostic[];
}

/**
 * @alpha
 * A single validation issue produced by admin discovery payload parsing.
 */
export interface IAdminDiscoveryPayloadValidationIssue {
  readonly code: string;
  readonly message: string;
  readonly path: string;
}

/**
 * @alpha
 * Successful admin discovery payload validation result.
 */
export interface IAdminDiscoveryPayloadValidationSuccess {
  readonly success: true;
  readonly payload: IAdminDiscoveryPayload;
}

/**
 * @alpha
 * Failed admin discovery payload validation result.
 */
export interface IAdminDiscoveryPayloadValidationFailure {
  readonly success: false;
  readonly error: Error & {
    readonly issues: readonly IAdminDiscoveryPayloadValidationIssue[];
  };
}

/**
 * @alpha
 * Discriminated validation result for admin discovery payloads.
 */
export type AdminDiscoveryPayloadValidationResultType =
  | IAdminDiscoveryPayloadValidationSuccess
  | IAdminDiscoveryPayloadValidationFailure;

/**
 * @alpha
 * Validation contract for admin discovery payload implementations.
 */
export interface IAdminDiscoveryPayloadValidator {
  validate(payload: unknown): AdminDiscoveryPayloadValidationResultType;
  parse(payload: unknown): IAdminDiscoveryPayload;
}
