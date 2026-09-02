import {
  type AdminPermissionMatchStrategyType,
  type IAdminActionExtensionDescriptor,
  type IAdminExtensionDescriptorMetadata,
  type IAdminNavigationExtensionDescriptor,
  type IAdminPageExtensionDescriptor,
  type IAdminPluginDiscoveryExtensions,
  type IAdminUIPluginManifest,
  type IAdminWidgetExtensionDescriptor,
  PERMISSION_MATCH_METADATA_KEY,
  PERMISSION_METADATA_KEY,
} from '@prosto/platform-admin-contracts';

/**
 * @alpha
 * Result of evaluating a plugin's permission requirements against
 * the current user's permissions.
 */
export interface IPluginPermissionDecision {
  readonly pluginId: string;
  readonly allowed: boolean;
  readonly missingPermissions: readonly string[];
}

/**
 * @alpha
 * Filtered subset of plugin extensions that the user is allowed to render.
 */
export interface IPermissionFilteredExtensions {
  readonly navigation: readonly IAdminNavigationExtensionDescriptor[];
  readonly pages: readonly IAdminPageExtensionDescriptor[];
  readonly widgets: readonly IAdminWidgetExtensionDescriptor[];
  readonly actions: readonly IAdminActionExtensionDescriptor[];
}

/**
 * @alpha
 * Permission-aware guard service for evaluating plugin-level and
 * extension-level permission requirements from discovery payload policy metadata.
 *
 * Uses the `requiredPermissions` field on `IAdminUIPluginManifest` for
 * plugin-level gates, and the `metadata` map on extension descriptors for
 * per-extension permission requirements (encoded as JSON arrays).
 *
 * @example
 * ```typescript
 * const guard = new PermissionGuardService(['admin', 'plugins.read']);
 *
 * const decision = guard.evaluatePluginAccess(manifest);
 * if (!decision.allowed) {
 *   console.warn('Missing:', decision.missingPermissions);
 * }
 *
 * const filtered = guard.filterExtensions(pluginExtensions);
 * ```
 */
export class PermissionGuardService {
  private readonly userPermissions: ReadonlySet<string>;

  constructor(userPermissions: readonly string[]) {
    this.userPermissions = new Set(userPermissions);
  }

  /**
   * Get the current user permissions (returns a defensive copy).
   */
  getUserPermissions(): readonly string[] {
    return [...this.userPermissions];
  }

  /**
   * Evaluate whether the current user can render a plugin based on
   * the plugin manifest's `requiredPermissions` access requirements.
   */
  evaluatePluginAccess(
    manifest: IAdminUIPluginManifest,
  ): IPluginPermissionDecision {
    const missingPermissions = manifest.requiredPermissions.filter(
      (permission) => !this.userPermissions.has(permission),
    );

    return {
      pluginId: manifest.id,
      allowed: missingPermissions.length === 0,
      missingPermissions,
    };
  }

  /**
   * Check if a user holds all required permissions (strict match).
   */
  hasPermission(requiredPermissions: readonly string[]): boolean {
    return requiredPermissions.every((permission) =>
      this.userPermissions.has(permission),
    );
  }

  /**
   * Check if a user holds any of the required permissions (any-match).
   */
  hasPermissionAny(requiredPermissions: readonly string[]): boolean {
    if (!requiredPermissions.length) {
      return true;
    }

    return requiredPermissions.some((permission) =>
      this.userPermissions.has(permission),
    );
  }

  /**
   * Check a generic permission gate with the given required tokens
   * and match strategy against the current user's permissions.
   */
  checkPermission(
    requiredPermissions: string[],
    strategy: AdminPermissionMatchStrategyType = 'all',
  ): boolean {
    if (!requiredPermissions.length) return true;

    return strategy === 'any'
      ? this.hasPermissionAny(requiredPermissions)
      : this.hasPermission(requiredPermissions);
  }

  /**
   * Retrieve the parsed permission metadata from a descriptor's
   * generic metadata map (for inspection or diagnostics).
   */
  parseDescriptorMetadata(
    metadata: Readonly<Record<string, string>> | undefined,
  ): {
    readonly requiredPermissions: string[];
    readonly strategy: AdminPermissionMatchStrategyType;
  } {
    if (!metadata) {
      return { requiredPermissions: [], strategy: 'all' };
    }

    return {
      requiredPermissions: this._parseRequiredPermissions(
        metadata[PERMISSION_METADATA_KEY],
      ),
      strategy: this._parsePermissionMatchStrategy(
        metadata[PERMISSION_MATCH_METADATA_KEY],
      ),
    };
  }

  /**
   * Evaluate whether a single extension descriptor's permission
   * requirements are satisfied by the current user.
   *
   * Permission data is extracted from the descriptor's `metadata` map
   * using the `requiredPermissions` and `permissionMatchStrategy` keys.
   */
  evaluateDescriptorAccess(
    descriptorMetadata: Readonly<Record<string, string>> | undefined,
  ): boolean {
    const { requiredPermissions, strategy } =
      this.parseDescriptorMetadata(descriptorMetadata);

    return this.checkPermission(requiredPermissions, strategy);
  }

  /**
   * Filter a full set of plugin extensions, returning only those
   * whose per-extension permission requirements are met by the user.
   */
  filterExtensions(
    extensions: IAdminPluginDiscoveryExtensions,
  ): IPermissionFilteredExtensions {
    const filterCallback = (descriptor: IAdminExtensionDescriptorMetadata) =>
      this.evaluateDescriptorAccess(descriptor.metadata);

    return {
      navigation: extensions.navigation.filter(filterCallback),
      pages: extensions.pages.filter(filterCallback),
      widgets: extensions.widgets.filter(filterCallback),
      actions: extensions.actions.filter(filterCallback),
    };
  }

  private _parseRequiredPermissions(raw: string | undefined): string[] {
    if (!raw) {
      return [];
    }

    try {
      const parsed: unknown = JSON.parse(raw);

      return Array.isArray(parsed)
        ? parsed.filter((item): item is string => typeof item === 'string')
        : [];
    } catch {
      return [];
    }
  }

  private _parsePermissionMatchStrategy(
    rawStrategy: string | undefined,
  ): AdminPermissionMatchStrategyType {
    return rawStrategy === 'any' || rawStrategy === 'all' ? rawStrategy : 'all';
  }
}
