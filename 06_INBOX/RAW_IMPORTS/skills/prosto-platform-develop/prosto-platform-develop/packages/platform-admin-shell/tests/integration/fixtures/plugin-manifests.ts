import type {
  IAdminActionExtensionDescriptor,
  IAdminDiscoveredPluginDescriptor,
  IAdminDiscoveryPayload,
  IAdminNavigationExtensionDescriptor,
  IAdminPageExtensionDescriptor,
  IAdminUIPluginManifest,
  IAdminWidgetExtensionDescriptor,
} from '@prosto/platform-admin-contracts';

/**
 * Compatible plugin manifest fixture — satisfies current shell version.
 */
export function createCompatiblePlugin(
  id: string,
  extensionPoints: IAdminUIPluginManifest['extensionPoints'] = ['nav'],
): IAdminUIPluginManifest {
  return {
    id,
    version: '1.0.0',
    schemaVersion: 'admin-ui-plugin-manifest.v1',
    shellCompatibility: '>=0.0.0',
    extensionPoints,
    requiredPermissions: [],
    requiredCapabilities: [],
    trustClass: 'trusted',
    reviewStatus: 'approved',
  };
}

/**
 * Incompatible plugin manifest fixture — requires shell >=99.0.0.
 */
export function createIncompatiblePlugin(id: string): IAdminUIPluginManifest {
  return {
    id,
    version: '1.0.0',
    schemaVersion: 'admin-ui-plugin-manifest.v1',
    shellCompatibility: '>=99.0.0',
    extensionPoints: ['nav'],
    requiredPermissions: [],
    requiredCapabilities: [],
    trustClass: 'trusted',
    reviewStatus: 'approved',
  };
}

/**
 * Plugin manifest with invalid schema version.
 */
export function createInvalidSchemaPlugin(id: string): IAdminUIPluginManifest {
  return {
    id,
    version: '1.0.0',
    schemaVersion:
      'admin-ui-plugin-manifest.v2' as 'admin-ui-plugin-manifest.v1',
    shellCompatibility: '>=0.0.0',
    extensionPoints: ['nav'],
    requiredPermissions: [],
    requiredCapabilities: [],
    trustClass: 'trusted',
    reviewStatus: 'approved',
  };
}

/**
 * Plugin manifest requiring specific permissions.
 */
export function createPermissionGatedPlugin(
  id: string,
  requiredPermissions: readonly string[],
): IAdminUIPluginManifest {
  return {
    id,
    version: '1.0.0',
    schemaVersion: 'admin-ui-plugin-manifest.v1',
    shellCompatibility: '>=0.0.0',
    extensionPoints: ['nav', 'page'],
    requiredPermissions: [...requiredPermissions],
    requiredCapabilities: [],
    trustClass: 'trusted',
    reviewStatus: 'approved',
  };
}

/**
 * Plugin manifest without entry point (will fail loadPlugin).
 */
export function createNoEntryPointPlugin(id: string): IAdminUIPluginManifest {
  return {
    id,
    version: '1.0.0',
    schemaVersion: 'admin-ui-plugin-manifest.v1',
    shellCompatibility: '>=0.0.0',
    extensionPoints: ['nav'],
    requiredPermissions: [],
    requiredCapabilities: [],
    trustClass: 'trusted',
    reviewStatus: 'approved',
  };
}

/**
 * Convert manifest to discovery descriptor for bootstrapPlugins input.
 *
 * When the manifest carries `requiredPermissions`, they are encoded
 * as a JSON string in the descriptor `metadata.requiredPermissions`
 * key so that `convertDescriptorToManifest` can parse them back.
 */
export function manifestToDescriptor(
  manifest: IAdminUIPluginManifest,
  extensionOverrides?: {
    nav?: Partial<IAdminNavigationExtensionDescriptor>[];
    page?: Partial<IAdminPageExtensionDescriptor>[];
    widget?: Partial<IAdminWidgetExtensionDescriptor>[];
    action?: Partial<IAdminActionExtensionDescriptor>[];
  },
): IAdminDiscoveredPluginDescriptor {
  const order = manifest.metadata?.order ? Number(manifest.metadata.order) : 0;

  const metadata: Record<string, string> = {
    ...(manifest.metadata as Record<string, string> | undefined),
  };

  if (manifest.requiredPermissions.length > 0) {
    metadata.requiredPermissions = JSON.stringify(manifest.requiredPermissions);
  }

  if (manifest.requiredCapabilities.length > 0) {
    metadata.requiredCapabilities = JSON.stringify(
      manifest.requiredCapabilities,
    );
  }

  return {
    id: manifest.id,
    version: manifest.version,
    displayName: manifest.displayName ?? manifest.id,
    shellCompatibility: manifest.shellCompatibility,
    trustClass: manifest.trustClass,
    reviewStatus: manifest.reviewStatus,
    extensions: {
      navigation:
        (extensionOverrides?.nav as IAdminNavigationExtensionDescriptor[]) ??
        (manifest.extensionPoints.includes('nav')
          ? [
              {
                id: `${manifest.id}-nav`,
                pluginId: manifest.id,
                label: manifest.displayName ?? manifest.id,
                order,
              },
            ]
          : []),
      pages:
        (extensionOverrides?.page as IAdminPageExtensionDescriptor[]) ??
        (manifest.extensionPoints.includes('page')
          ? [
              {
                id: `${manifest.id}-page`,
                pluginId: manifest.id,
                route: `/${manifest.id}`,
                title: manifest.displayName ?? manifest.id,
                componentKey: manifest.id,
                order,
              },
            ]
          : []),
      widgets:
        (extensionOverrides?.widget as IAdminWidgetExtensionDescriptor[]) ??
        (manifest.extensionPoints.includes('widget')
          ? [
              {
                id: `${manifest.id}-widget`,
                pluginId: manifest.id,
                slot: 'default',
                componentKey: manifest.id,
                order,
              },
            ]
          : []),
      actions:
        (extensionOverrides?.action as IAdminActionExtensionDescriptor[]) ??
        (manifest.extensionPoints.includes('action')
          ? [
              {
                id: `${manifest.id}-action`,
                pluginId: manifest.id,
                target: manifest.id,
                label: manifest.displayName ?? manifest.id,
                actionKey: manifest.id,
                order,
              },
            ]
          : []),
    },
    metadata,
  };
}

/**
 * Create a full discovery payload for mock BFF responses.
 */
export function createDiscoveryPayload(
  plugins: IAdminDiscoveredPluginDescriptor[],
  rejected: IAdminDiscoveryPayload['rejected'] = [],
): IAdminDiscoveryPayload {
  return {
    schemaVersion: 'admin-discovery-payload.v1',
    generatedAt: new Date().toISOString(),
    plugins,
    rejected,
  };
}

/**
 * Mix of compatible and incompatible plugin descriptors for integration testing.
 */
export function createMixedPluginFixtures(): {
  compatiblePlugins: IAdminDiscoveredPluginDescriptor[];
  incompatiblePlugins: IAdminDiscoveredPluginDescriptor[];
  allPlugins: IAdminDiscoveredPluginDescriptor[];
} {
  const compatiblePlugins = [
    manifestToDescriptor(createCompatiblePlugin('plugin-nav-a', ['nav']), {
      nav: [{ id: 'nav-a-1', label: 'Dashboard', order: 10 }],
    }),
    manifestToDescriptor(createCompatiblePlugin('plugin-page-b', ['page']), {
      page: [
        {
          id: 'page-b-1',
          route: '/reports',
          title: 'Reports',
          componentKey: 'ReportsView',
          order: 20,
        },
      ],
    }),
    manifestToDescriptor(
      createCompatiblePlugin('plugin-multi-c', [
        'nav',
        'page',
        'widget',
        'action',
      ]),
      {
        nav: [{ id: 'nav-c-1', label: 'Settings', order: 30 }],
        page: [
          {
            id: 'page-c-1',
            route: '/settings',
            title: 'Settings',
            componentKey: 'SettingsView',
            order: 30,
          },
        ],
        widget: [
          {
            id: 'widget-c-1',
            slot: 'sidebar',
            componentKey: 'SettingsWidget',
            order: 30,
          },
        ],
        action: [
          {
            id: 'action-c-1',
            target: 'settings.export',
            label: 'Export Settings',
            actionKey: 'ExportAction',
            order: 30,
          },
        ],
      },
    ),
  ];

  const incompatiblePlugins = [
    manifestToDescriptor(createIncompatiblePlugin('plugin-old-d')),
    manifestToDescriptor(createCompatiblePlugin('plugin-ok-e', ['nav'])),
  ];

  const allPlugins = [...compatiblePlugins, ...incompatiblePlugins];

  return { compatiblePlugins, incompatiblePlugins, allPlugins };
}
