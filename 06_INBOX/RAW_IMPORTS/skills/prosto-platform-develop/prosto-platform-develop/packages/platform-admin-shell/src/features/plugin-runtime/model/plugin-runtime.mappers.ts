import type {
  IAdminUIPluginManifest,
  IAdminPluginDiscoveryExtensions,
  IAdminNavigationExtensionDescriptor,
  IAdminPageExtensionDescriptor,
  IAdminWidgetExtensionDescriptor,
  IAdminActionExtensionDescriptor,
  IAdminDiscoveredPluginDescriptor,
} from '@prosto/platform-admin-contracts';

function readMetadataOrder(
  metadata: Readonly<Record<string, string>> | undefined,
): number {
  const order = Number(metadata?.order ?? 0);
  return Number.isFinite(order) ? order : 0;
}

/**
 * @alpha
 * Sort plugin descriptors by metadata order.
 */
export function sortPluginDescriptors(
  pluginDescriptors: readonly IAdminDiscoveredPluginDescriptor[],
): IAdminDiscoveredPluginDescriptor[] {
  return [...pluginDescriptors].sort(
    (a, b) => readMetadataOrder(a.metadata) - readMetadataOrder(b.metadata),
  );
}

function createNavigationDescriptor(
  pluginId: string,
  label: string,
  order: number,
): IAdminNavigationExtensionDescriptor {
  return {
    id: `${pluginId}-nav`,
    pluginId,
    label,
    order,
  };
}

function createPageDescriptor(
  pluginId: string,
  title: string,
  order: number,
): IAdminPageExtensionDescriptor {
  return {
    id: `${pluginId}-page`,
    pluginId,
    route: `/${pluginId}`,
    title,
    componentKey: pluginId,
    order,
  };
}

function createWidgetDescriptor(
  pluginId: string,
  order: number,
): IAdminWidgetExtensionDescriptor {
  return {
    id: `${pluginId}-widget`,
    pluginId,
    slot: 'default',
    componentKey: pluginId,
    order,
  };
}

function createActionDescriptor(
  pluginId: string,
  label: string,
  order: number,
): IAdminActionExtensionDescriptor {
  return {
    id: `${pluginId}-action`,
    pluginId,
    target: pluginId,
    label,
    actionKey: pluginId,
    order,
  };
}

/**
 * @alpha
 * Create extension descriptors from plugin manifest.
 */
export function createManifestExtensionDescriptors(
  manifest: IAdminUIPluginManifest,
): IAdminPluginDiscoveryExtensions {
  const extensionPoints = manifest.extensionPoints ?? [];
  const order = readMetadataOrder(manifest.metadata);
  const displayName = manifest.displayName ?? manifest.id;

  return {
    navigation: extensionPoints.includes('nav')
      ? [createNavigationDescriptor(manifest.id, displayName, order)]
      : [],
    pages: extensionPoints.includes('page')
      ? [createPageDescriptor(manifest.id, displayName, order)]
      : [],
    widgets: extensionPoints.includes('widget')
      ? [createWidgetDescriptor(manifest.id, order)]
      : [],
    actions: extensionPoints.includes('action')
      ? [createActionDescriptor(manifest.id, displayName, order)]
      : [],
  };
}

/**
 * @alpha
 * Check if extensions have any entries.
 */
export function hasExtensions(
  extensions: IAdminPluginDiscoveryExtensions,
): boolean {
  return (
    extensions.navigation.length > 0 ||
    extensions.pages.length > 0 ||
    extensions.widgets.length > 0 ||
    extensions.actions.length > 0
  );
}

/**
 * @alpha
 * Get registered descriptors with kind info.
 */
export function getRegisteredDescriptors(
  extensions: IAdminPluginDiscoveryExtensions,
): readonly { readonly id: string; readonly kind: string }[] {
  return [
    ...extensions.navigation.map((descriptor) => ({
      id: descriptor.id,
      kind: 'navigation',
    })),
    ...extensions.pages.map((descriptor) => ({
      id: descriptor.id,
      kind: 'page',
    })),
    ...extensions.widgets.map((descriptor) => ({
      id: descriptor.id,
      kind: 'widget',
    })),
    ...extensions.actions.map((descriptor) => ({
      id: descriptor.id,
      kind: 'action',
    })),
  ];
}
