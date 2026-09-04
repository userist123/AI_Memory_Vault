import type {
  AdminDiscoveryExtensionKindType,
  IAdminActionExtensionDescriptor,
  IAdminNavigationExtensionDescriptor,
  IAdminPageExtensionDescriptor,
  IAdminPluginDiscoveryExtensions,
  IAdminWidgetExtensionDescriptor,
} from '@prosto/platform-admin-contracts';
import type {
  IExtensionDescriptorBase,
  ExtensionConflictReasonType,
  IExtensionConflict,
  IPluginExtensionRegistrationResult,
} from './extension-registry.types.js';
import { sortByOrder } from '@/shared/lib/ordering';

type ExtensionDescriptorUnionType =
  | IAdminNavigationExtensionDescriptor
  | IAdminPageExtensionDescriptor
  | IAdminWidgetExtensionDescriptor
  | IAdminActionExtensionDescriptor;

type ExtensionRegistryMapType<T extends IExtensionDescriptorBase> = Map<
  string,
  T
>;

type ConflictDetectorType<T extends IExtensionDescriptorBase> = (
  descriptor: T,
) => IExtensionConflict | undefined;

/**
 * @alpha
 * Shell-side registry for extension points contributed by discovered plugins.
 *
 * Maintains separate registries per extension kind, enforces deterministic
 * ordering, and detects conflicts (duplicate IDs, routes, slots, action targets).
 *
 * @example
 * ```typescript
 * const registry = new ExtensionRegistry();
 * const result = registry.registerPluginExtensions('my-plugin', {
 *   navigation: [{ id: 'nav-1', pluginId: 'my-plugin', label: 'Dashboard' }],
 *   pages: [],
 *   widgets: [],
 *   actions: [],
 * });
 *
 * if (!result.registered) {
 *   console.error('Conflicts:', result.conflicts);
 * }
 * ```
 */
export class ExtensionRegistry {
  private readonly navigation = new Map<
    string,
    IAdminNavigationExtensionDescriptor
  >();
  private readonly pages = new Map<string, IAdminPageExtensionDescriptor>();
  private readonly widgets = new Map<string, IAdminWidgetExtensionDescriptor>();
  private readonly actions = new Map<string, IAdminActionExtensionDescriptor>();
  private readonly registrationOrderByDescriptorId = new Map<string, number>();

  private nextRegistrationOrder = 0;

  /**
   * Register all extensions from a discovered plugin.
   * Returns a result indicating success or conflicts detected.
   *
   * Current behavior intentionally keeps non-conflicting descriptors from the
   * same batch even when other descriptors conflict; callers use the returned
   * conflict list to decide whether to reject the plugin at a higher level.
   */
  registerPluginExtensions(
    _pluginId: string,
    extensions: IAdminPluginDiscoveryExtensions,
  ): IPluginExtensionRegistrationResult {
    const registrationResults = [
      this._registerDescriptors(
        extensions.navigation,
        this.navigation,
        this._detectNavigationConflict.bind(this),
      ),
      this._registerDescriptors(
        extensions.pages,
        this.pages,
        this._detectPageConflict.bind(this),
      ),
      this._registerDescriptors(
        extensions.widgets,
        this.widgets,
        this._detectWidgetConflict.bind(this),
      ),
      this._registerDescriptors(
        extensions.actions,
        this.actions,
        this._detectActionConflict.bind(this),
      ),
    ];

    const conflicts = registrationResults.flatMap((result) => result.conflicts);
    const registeredDescriptorIds = registrationResults.flatMap(
      (result) => result.registeredDescriptorIds,
    );

    return {
      registered: conflicts.length === 0,
      conflicts,
      registeredDescriptorIds,
    };
  }

  /**
   * Get all navigation extensions sorted by order.
   */
  getNavigationExtensions(): readonly IAdminNavigationExtensionDescriptor[] {
    return this._getAllSorted(this.navigation);
  }

  /**
   * Get all page extensions sorted by order.
   */
  getPageExtensions(): readonly IAdminPageExtensionDescriptor[] {
    return this._getAllSorted(this.pages);
  }

  /**
   * Get all widget extensions sorted by order.
   */
  getWidgetExtensions(): readonly IAdminWidgetExtensionDescriptor[] {
    return this._getAllSorted(this.widgets);
  }

  /**
   * Get all action extensions sorted by order.
   */
  getActionExtensions(): readonly IAdminActionExtensionDescriptor[] {
    return this._getAllSorted(this.actions);
  }

  /**
   * Get all extensions for a given kind.
   */
  getExtensionsByKind(
    kind: AdminDiscoveryExtensionKindType,
  ): readonly ExtensionDescriptorUnionType[] {
    const map = this._getRegistryByKind(kind);

    return this._getAllSorted(map);
  }

  /**
   * Find an extension by its descriptor ID across all kinds.
   */
  findExtensionById(
    descriptorId: string,
  ): ExtensionDescriptorUnionType | undefined {
    return (
      this.navigation.get(descriptorId) ??
      this.pages.get(descriptorId) ??
      this.widgets.get(descriptorId) ??
      this.actions.get(descriptorId)
    );
  }

  /**
   * Get all extensions contributed by a specific plugin.
   */
  getExtensionsByPluginId(pluginId: string): IAdminPluginDiscoveryExtensions {
    const filterByPlugin = <T extends IExtensionDescriptorBase>(
      map: ExtensionRegistryMapType<T>,
    ): T[] => Array.from(map.values()).filter((d) => d.pluginId === pluginId);

    return {
      navigation: filterByPlugin(this.navigation),
      pages: filterByPlugin(this.pages),
      widgets: filterByPlugin(this.widgets),
      actions: filterByPlugin(this.actions),
    };
  }

  /**
   * Remove all extensions contributed by a specific plugin.
   * Returns the count of removed descriptors.
   */
  removePluginExtensions(pluginId: string): number {
    let removed = 0;

    for (const map of [
      this.navigation,
      this.pages,
      this.widgets,
      this.actions,
    ]) {
      for (const [id, descriptor] of map) {
        if (descriptor.pluginId === pluginId) {
          map.delete(id);
          this.registrationOrderByDescriptorId.delete(id);
          removed++;
        }
      }
    }

    return removed;
  }

  /**
   * Get total count of registered extensions across all kinds.
   */
  getTotalExtensionCount(): number {
    return (
      this.navigation.size +
      this.pages.size +
      this.widgets.size +
      this.actions.size
    );
  }

  /**
   * Clear all registered extensions.
   */
  clear(): void {
    this.navigation.clear();
    this.pages.clear();
    this.widgets.clear();
    this.actions.clear();
    this.registrationOrderByDescriptorId.clear();
    this.nextRegistrationOrder = 0;
  }

  private _registerDescriptors<T extends IExtensionDescriptorBase>(
    descriptors: readonly T[],
    registry: ExtensionRegistryMapType<T>,
    detectConflict: ConflictDetectorType<T>,
  ): IPluginExtensionRegistrationResult {
    const conflicts: IExtensionConflict[] = [];
    const registeredDescriptorIds: string[] = [];

    for (const descriptor of this._sortIncomingDescriptors(descriptors)) {
      const conflict = detectConflict(descriptor);

      if (conflict) {
        conflicts.push(conflict);
        continue;
      }

      registry.set(descriptor.id, descriptor);
      this.registrationOrderByDescriptorId.set(
        descriptor.id,
        this.nextRegistrationOrder,
      );

      this.nextRegistrationOrder++;
      registeredDescriptorIds.push(descriptor.id);
    }

    return {
      registered: conflicts.length === 0,
      conflicts,
      registeredDescriptorIds,
    };
  }

  private _getRegistryByKind(
    kind: AdminDiscoveryExtensionKindType,
  ): Map<string, ExtensionDescriptorUnionType> {
    switch (kind) {
      case 'navigation':
        return this.navigation;

      case 'page':
        return this.pages;

      case 'widget':
        return this.widgets;

      case 'action':
        return this.actions;

      default: {
        const _exhaustiveCheck: never = kind;
        return _exhaustiveCheck;
      }
    }
  }

  private _sortIncomingDescriptors<T extends IExtensionDescriptorBase>(
    extensions: readonly T[],
  ): T[] {
    return sortByOrder(
      extensions.map((descriptor, index) => ({ descriptor, index })),
      (entry) => entry.descriptor.order ?? 0,
      (entry) => entry.index,
    ).map((entry) => entry.descriptor);
  }

  private _getAllSorted<T extends IExtensionDescriptorBase>(
    map: ExtensionRegistryMapType<T>,
  ): T[] {
    return sortByOrder(
      Array.from(map.values()),
      (descriptor) => descriptor.order ?? 0,
      (descriptor) =>
        this.registrationOrderByDescriptorId.get(descriptor.id) ?? 0,
    );
  }

  private _detectDuplicateIdConflict<T extends IExtensionDescriptorBase>(
    kind: AdminDiscoveryExtensionKindType,
    registry: ExtensionRegistryMapType<T>,
    descriptor: T,
    label: string,
  ): IExtensionConflict | undefined {
    const existing = registry.get(descriptor.id);

    if (!existing) {
      return undefined;
    }

    return this._createConflict({
      kind,
      existing,
      descriptor,
      reason: 'DUPLICATE_ID',
      detail: `${label} descriptor "${descriptor.id}" already registered by plugin "${existing.pluginId}"`,
    });
  }

  private _detectNavigationConflict(
    descriptor: IAdminNavigationExtensionDescriptor,
  ): IExtensionConflict | undefined {
    return this._detectDuplicateIdConflict(
      'navigation',
      this.navigation,
      descriptor,
      'Navigation',
    );
  }

  private _detectPageConflict(
    descriptor: IAdminPageExtensionDescriptor,
  ): IExtensionConflict | undefined {
    const duplicateIdConflict = this._detectDuplicateIdConflict(
      'page',
      this.pages,
      descriptor,
      'Page',
    );

    if (duplicateIdConflict) {
      return duplicateIdConflict;
    }

    const duplicateRoute = Array.from(this.pages.values()).find(
      (existing) => existing.route === descriptor.route,
    );

    if (!duplicateRoute) {
      return undefined;
    }

    return this._createConflict({
      kind: 'page',
      existing: duplicateRoute,
      descriptor,
      reason: 'DUPLICATE_ROUTE',
      detail: `Route "${descriptor.route}" already claimed by page "${duplicateRoute.id}" from plugin "${duplicateRoute.pluginId}"`,
    });
  }

  private _detectWidgetConflict(
    descriptor: IAdminWidgetExtensionDescriptor,
  ): IExtensionConflict | undefined {
    const duplicateIdConflict = this._detectDuplicateIdConflict(
      'widget',
      this.widgets,
      descriptor,
      'Widget',
    );

    if (duplicateIdConflict) {
      return duplicateIdConflict;
    }

    const duplicateSlot = Array.from(this.widgets.values()).find(
      (existing) => existing.slot === descriptor.slot,
    );

    if (!duplicateSlot) {
      return undefined;
    }

    return this._createConflict({
      kind: 'widget',
      existing: duplicateSlot,
      descriptor,
      reason: 'DUPLICATE_SLOT',
      detail: `Widget slot "${descriptor.slot}" already claimed by widget "${duplicateSlot.id}" from plugin "${duplicateSlot.pluginId}"`,
    });
  }

  private _detectActionConflict(
    descriptor: IAdminActionExtensionDescriptor,
  ): IExtensionConflict | undefined {
    const duplicateIdConflict = this._detectDuplicateIdConflict(
      'action',
      this.actions,
      descriptor,
      'Action',
    );

    if (duplicateIdConflict) {
      return duplicateIdConflict;
    }

    const duplicateAction = Array.from(this.actions.values()).find(
      (existing) =>
        existing.target === descriptor.target &&
        existing.actionKey === descriptor.actionKey,
    );

    if (!duplicateAction) {
      return undefined;
    }

    return this._createConflict({
      kind: 'action',
      existing: duplicateAction,
      descriptor,
      reason: 'DUPLICATE_ACTION',
      detail: `Action target "${descriptor.target}" with key "${descriptor.actionKey}" already claimed by action "${duplicateAction.id}" from plugin "${duplicateAction.pluginId}"`,
    });
  }

  private _createConflict<T extends IExtensionDescriptorBase>(options: {
    readonly kind: AdminDiscoveryExtensionKindType;
    readonly existing: T;
    readonly descriptor: T;
    readonly reason: ExtensionConflictReasonType;
    readonly detail: string;
  }): IExtensionConflict {
    return {
      kind: options.kind,
      existingDescriptorId: options.existing.id,
      existingPluginId: options.existing.pluginId,
      conflictingDescriptorId: options.descriptor.id,
      conflictingPluginId: options.descriptor.pluginId,
      reason: options.reason,
      detail: options.detail,
    };
  }
}
