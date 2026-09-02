import type {
  AdminShellCompatibilityRangeType,
  AdminUIPluginCapabilityType,
  AdminUIPluginExtensionPointType,
  AdminUIPluginIdentifierType,
  AdminUIPluginManifestSchemaVersionType,
  AdminUIPluginPermissionType,
  AdminUIPluginReviewStatusType,
  AdminUIPluginTrustClassType,
  AdminUIPluginVersionType,
} from './admin-ui-plugin-manifest.types.js';

/**
 * @alpha
 * Basic identity metadata for an admin UI plugin artifact.
 */
export interface IAdminUIPluginIdentity {
  readonly id: AdminUIPluginIdentifierType;
  readonly version: AdminUIPluginVersionType;
}

/**
 * @alpha
 * Compatibility metadata used by admin shell admission checks.
 */
export interface IAdminUIPluginShellCompatibility {
  readonly shellCompatibility: AdminShellCompatibilityRangeType;
}

/**
 * @alpha
 * Permission and capability declarations required before a plugin can be exposed.
 */
export interface IAdminUIPluginAccessRequirements {
  readonly requiredPermissions: readonly AdminUIPluginPermissionType[];
  readonly requiredCapabilities: readonly AdminUIPluginCapabilityType[];
}

/**
 * @alpha
 * Trust and review metadata for policy-controlled admin plugin discovery.
 */
export interface IAdminUIPluginReviewMetadata {
  readonly trustClass: AdminUIPluginTrustClassType;
  readonly reviewStatus: AdminUIPluginReviewStatusType;
  readonly reviewedAt?: string;
  readonly reviewer?: string;
}

/**
 * @alpha
 * Versioned manifest contract for framework-neutral admin UI plugins.
 */
export interface IAdminUIPluginManifest
  extends
    IAdminUIPluginIdentity,
    IAdminUIPluginShellCompatibility,
    IAdminUIPluginAccessRequirements,
    IAdminUIPluginReviewMetadata {
  readonly schemaVersion: AdminUIPluginManifestSchemaVersionType;
  readonly extensionPoints: readonly AdminUIPluginExtensionPointType[];
  readonly displayName?: string;
  readonly metadata?: Readonly<Record<string, string>>;
}

/**
 * @alpha
 * A single validation issue produced by admin UI plugin manifest parsing.
 */
export interface IAdminUIPluginManifestValidationIssue {
  readonly code: string;
  readonly message: string;
  readonly path: string;
}

/**
 * @alpha
 * Successful admin UI plugin manifest validation result.
 */
export interface IAdminUIPluginManifestValidationSuccess {
  readonly success: true;
  readonly manifest: IAdminUIPluginManifest;
}

/**
 * @alpha
 * Failed admin UI plugin manifest validation result.
 */
export interface IAdminUIPluginManifestValidationFailure {
  readonly success: false;
  readonly error: Error & {
    readonly issues: readonly IAdminUIPluginManifestValidationIssue[];
  };
}

/**
 * @alpha
 * Discriminated validation result for admin UI plugin manifests.
 */
export type AdminUIPluginManifestValidationResultType =
  | IAdminUIPluginManifestValidationSuccess
  | IAdminUIPluginManifestValidationFailure;

/**
 * @alpha
 * Validation contract for admin UI plugin manifest implementations.
 */
export interface IAdminUIPluginManifestValidator {
  validate(manifest: unknown): AdminUIPluginManifestValidationResultType;
  parse(manifest: unknown): IAdminUIPluginManifest;
}
