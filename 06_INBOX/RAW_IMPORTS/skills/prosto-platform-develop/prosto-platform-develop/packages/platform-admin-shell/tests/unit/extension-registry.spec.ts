import type {
  IAdminActionExtensionDescriptor,
  IAdminNavigationExtensionDescriptor,
  IAdminPageExtensionDescriptor,
  IAdminPluginDiscoveryExtensions,
  IAdminWidgetExtensionDescriptor,
} from '@prosto/platform-admin-contracts';
import { beforeEach, describe, expect, it } from 'vitest';
import { ExtensionRegistry } from '@/features/plugin-runtime/model/extension-registry.js';

function makeNav(
  overrides: Partial<IAdminNavigationExtensionDescriptor> & {
    id: string;
    pluginId: string;
  },
): IAdminNavigationExtensionDescriptor {
  return {
    label: overrides.id,
    ...overrides,
  };
}

function makePage(
  overrides: Partial<IAdminPageExtensionDescriptor> & {
    id: string;
    pluginId: string;
    route: string;
  },
): IAdminPageExtensionDescriptor {
  return {
    title: overrides.id,
    componentKey: overrides.id,
    ...overrides,
  };
}

function makeWidget(
  overrides: Partial<IAdminWidgetExtensionDescriptor> & {
    id: string;
    pluginId: string;
    slot: string;
  },
): IAdminWidgetExtensionDescriptor {
  return {
    componentKey: overrides.id,
    ...overrides,
  };
}

function makeAction(
  overrides: Partial<IAdminActionExtensionDescriptor> & {
    id: string;
    pluginId: string;
    target: string;
    actionKey: string;
  },
): IAdminActionExtensionDescriptor {
  return {
    label: overrides.id,
    ...overrides,
  };
}

function emptyExtensions(): IAdminPluginDiscoveryExtensions {
  return { navigation: [], pages: [], widgets: [], actions: [] };
}

describe('ExtensionRegistry', () => {
  let registry: ExtensionRegistry;

  beforeEach(() => {
    registry = new ExtensionRegistry();
  });

  describe('registerPluginExtensions', () => {
    it('should register navigation extensions', () => {
      const result = registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        navigation: [
          makeNav({ id: 'nav-1', pluginId: 'plugin-a', label: 'Dashboard' }),
        ],
      });

      expect(result.registered).toBe(true);
      expect(result.conflicts).toHaveLength(0);
      expect(result.registeredDescriptorIds).toEqual(['nav-1']);
      expect(registry.getNavigationExtensions()).toHaveLength(1);
    });

    it('should register page extensions', () => {
      const result = registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        pages: [
          makePage({
            id: 'page-1',
            pluginId: 'plugin-a',
            route: '/dashboard',
            title: 'Dashboard',
          }),
        ],
      });

      expect(result.registered).toBe(true);
      expect(registry.getPageExtensions()).toHaveLength(1);
    });

    it('should register widget extensions', () => {
      const result = registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        widgets: [
          makeWidget({ id: 'widget-1', pluginId: 'plugin-a', slot: 'sidebar' }),
        ],
      });

      expect(result.registered).toBe(true);
      expect(registry.getWidgetExtensions()).toHaveLength(1);
    });

    it('should register action extensions', () => {
      const result = registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        actions: [
          makeAction({
            id: 'action-1',
            pluginId: 'plugin-a',
            target: 'users',
            actionKey: 'create',
          }),
        ],
      });

      expect(result.registered).toBe(true);
      expect(registry.getActionExtensions()).toHaveLength(1);
    });

    it('should register extensions of all kinds simultaneously', () => {
      const result = registry.registerPluginExtensions('plugin-a', {
        navigation: [
          makeNav({ id: 'nav-1', pluginId: 'plugin-a', label: 'Nav' }),
        ],
        pages: [
          makePage({
            id: 'page-1',
            pluginId: 'plugin-a',
            route: '/page',
            title: 'Page',
          }),
        ],
        widgets: [
          makeWidget({ id: 'widget-1', pluginId: 'plugin-a', slot: 'slot' }),
        ],
        actions: [
          makeAction({
            id: 'action-1',
            pluginId: 'plugin-a',
            target: 't',
            actionKey: 'k',
          }),
        ],
      });

      expect(result.registered).toBe(true);
      expect(result.registeredDescriptorIds).toHaveLength(4);
      expect(registry.getTotalExtensionCount()).toBe(4);
    });

    it('should return empty registeredDescriptorIds for empty extensions', () => {
      const result = registry.registerPluginExtensions(
        'plugin-a',
        emptyExtensions(),
      );

      expect(result.registered).toBe(true);
      expect(result.registeredDescriptorIds).toHaveLength(0);
    });
  });

  describe('conflict detection', () => {
    it('should detect duplicate navigation descriptor IDs', () => {
      registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        navigation: [
          makeNav({ id: 'nav-1', pluginId: 'plugin-a', label: 'Nav A' }),
        ],
      });

      const result = registry.registerPluginExtensions('plugin-b', {
        ...emptyExtensions(),
        navigation: [
          makeNav({ id: 'nav-1', pluginId: 'plugin-b', label: 'Nav B' }),
        ],
      });

      expect(result.registered).toBe(false);
      expect(result.conflicts).toHaveLength(1);
      expect(result.conflicts[0]?.reason).toBe('DUPLICATE_ID');
      expect(result.conflicts[0]?.kind).toBe('navigation');
      expect(result.conflicts[0]?.existingPluginId).toBe('plugin-a');
      expect(result.conflicts[0]?.conflictingPluginId).toBe('plugin-b');
    });

    it('should detect duplicate page descriptor IDs', () => {
      registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        pages: [makePage({ id: 'page-1', pluginId: 'plugin-a', route: '/a' })],
      });

      const result = registry.registerPluginExtensions('plugin-b', {
        ...emptyExtensions(),
        pages: [makePage({ id: 'page-1', pluginId: 'plugin-b', route: '/b' })],
      });

      expect(result.registered).toBe(false);
      expect(result.conflicts[0]?.reason).toBe('DUPLICATE_ID');
      expect(result.conflicts[0]?.kind).toBe('page');
    });

    it('should detect duplicate page routes', () => {
      registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        pages: [
          makePage({ id: 'page-a', pluginId: 'plugin-a', route: '/shared' }),
        ],
      });

      const result = registry.registerPluginExtensions('plugin-b', {
        ...emptyExtensions(),
        pages: [
          makePage({ id: 'page-b', pluginId: 'plugin-b', route: '/shared' }),
        ],
      });

      expect(result.registered).toBe(false);
      expect(result.conflicts).toHaveLength(1);
      expect(result.conflicts[0]?.reason).toBe('DUPLICATE_ROUTE');
      expect(result.conflicts[0]?.detail).toContain('/shared');
    });

    it('should detect duplicate widget descriptor IDs', () => {
      registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        widgets: [
          makeWidget({ id: 'widget-1', pluginId: 'plugin-a', slot: 'sidebar' }),
        ],
      });

      const result = registry.registerPluginExtensions('plugin-b', {
        ...emptyExtensions(),
        widgets: [
          makeWidget({ id: 'widget-1', pluginId: 'plugin-b', slot: 'header' }),
        ],
      });

      expect(result.registered).toBe(false);
      expect(result.conflicts[0]?.reason).toBe('DUPLICATE_ID');
      expect(result.conflicts[0]?.kind).toBe('widget');
    });

    it('should detect duplicate widget slots', () => {
      registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        widgets: [
          makeWidget({ id: 'widget-a', pluginId: 'plugin-a', slot: 'sidebar' }),
        ],
      });

      const result = registry.registerPluginExtensions('plugin-b', {
        ...emptyExtensions(),
        widgets: [
          makeWidget({ id: 'widget-b', pluginId: 'plugin-b', slot: 'sidebar' }),
        ],
      });

      expect(result.registered).toBe(false);
      expect(result.conflicts[0]?.reason).toBe('DUPLICATE_SLOT');
      expect(result.conflicts[0]?.detail).toContain('sidebar');
    });

    it('should detect duplicate action descriptor IDs', () => {
      registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        actions: [
          makeAction({
            id: 'action-1',
            pluginId: 'plugin-a',
            target: 'users',
            actionKey: 'create',
          }),
        ],
      });

      const result = registry.registerPluginExtensions('plugin-b', {
        ...emptyExtensions(),
        actions: [
          makeAction({
            id: 'action-1',
            pluginId: 'plugin-b',
            target: 'orders',
            actionKey: 'delete',
          }),
        ],
      });

      expect(result.registered).toBe(false);
      expect(result.conflicts[0]?.reason).toBe('DUPLICATE_ID');
      expect(result.conflicts[0]?.kind).toBe('action');
    });

    it('should detect duplicate action targets with same key', () => {
      registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        actions: [
          makeAction({
            id: 'action-a',
            pluginId: 'plugin-a',
            target: 'users',
            actionKey: 'create',
          }),
        ],
      });

      const result = registry.registerPluginExtensions('plugin-b', {
        ...emptyExtensions(),
        actions: [
          makeAction({
            id: 'action-b',
            pluginId: 'plugin-b',
            target: 'users',
            actionKey: 'create',
          }),
        ],
      });

      expect(result.registered).toBe(false);
      expect(result.conflicts[0]?.reason).toBe('DUPLICATE_ACTION');
      expect(result.conflicts[0]?.detail).toContain('users');
    });

    it('should allow same target with different actionKey', () => {
      registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        actions: [
          makeAction({
            id: 'action-a',
            pluginId: 'plugin-a',
            target: 'users',
            actionKey: 'create',
          }),
        ],
      });

      const result = registry.registerPluginExtensions('plugin-b', {
        ...emptyExtensions(),
        actions: [
          makeAction({
            id: 'action-b',
            pluginId: 'plugin-b',
            target: 'users',
            actionKey: 'delete',
          }),
        ],
      });

      expect(result.registered).toBe(true);
      expect(result.conflicts).toHaveLength(0);
    });

    it('should allow same actionKey with different target', () => {
      registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        actions: [
          makeAction({
            id: 'action-a',
            pluginId: 'plugin-a',
            target: 'users',
            actionKey: 'create',
          }),
        ],
      });

      const result = registry.registerPluginExtensions('plugin-b', {
        ...emptyExtensions(),
        actions: [
          makeAction({
            id: 'action-b',
            pluginId: 'plugin-b',
            target: 'orders',
            actionKey: 'create',
          }),
        ],
      });

      expect(result.registered).toBe(true);
    });

    it('should detect multiple conflicts in a single registration', () => {
      registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        navigation: [
          makeNav({ id: 'nav-1', pluginId: 'plugin-a', label: 'Nav A' }),
        ],
        pages: [
          makePage({ id: 'page-1', pluginId: 'plugin-a', route: '/shared' }),
        ],
      });

      const result = registry.registerPluginExtensions('plugin-b', {
        ...emptyExtensions(),
        navigation: [
          makeNav({ id: 'nav-1', pluginId: 'plugin-b', label: 'Nav B' }),
        ],
        pages: [
          makePage({ id: 'page-1', pluginId: 'plugin-b', route: '/shared' }),
        ],
      });

      expect(result.registered).toBe(false);
      expect(result.conflicts).toHaveLength(2);
    });

    it('should register non-conflicting extensions and skip conflicting ones', () => {
      registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        navigation: [
          makeNav({ id: 'nav-1', pluginId: 'plugin-a', label: 'Nav A' }),
        ],
      });

      const result = registry.registerPluginExtensions('plugin-b', {
        navigation: [
          makeNav({ id: 'nav-1', pluginId: 'plugin-b', label: 'Nav B' }),
          makeNav({ id: 'nav-2', pluginId: 'plugin-b', label: 'Nav B2' }),
        ],
        pages: [],
        widgets: [],
        actions: [],
      });

      expect(result.registered).toBe(false);
      expect(result.conflicts).toHaveLength(1);
      expect(result.registeredDescriptorIds).toEqual(['nav-2']);
      expect(registry.getNavigationExtensions()).toHaveLength(2);
    });
  });

  describe('ordering', () => {
    it('should sort navigation extensions by order', () => {
      registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        navigation: [
          makeNav({
            id: 'nav-3',
            pluginId: 'plugin-a',
            label: 'Third',
            order: 30,
          }),
          makeNav({
            id: 'nav-1',
            pluginId: 'plugin-a',
            label: 'First',
            order: 10,
          }),
          makeNav({
            id: 'nav-2',
            pluginId: 'plugin-a',
            label: 'Second',
            order: 20,
          }),
        ],
      });

      const navs = registry.getNavigationExtensions();

      expect(navs[0]?.id).toBe('nav-1');
      expect(navs[1]?.id).toBe('nav-2');
      expect(navs[2]?.id).toBe('nav-3');
    });

    it('should sort pages by order', () => {
      registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        pages: [
          makePage({
            id: 'page-b',
            pluginId: 'plugin-a',
            route: '/b',
            order: 20,
          }),
          makePage({
            id: 'page-a',
            pluginId: 'plugin-a',
            route: '/a',
            order: 10,
          }),
        ],
      });

      const pages = registry.getPageExtensions();

      expect(pages[0]?.id).toBe('page-a');
      expect(pages[1]?.id).toBe('page-b');
    });

    it('should sort widgets by order', () => {
      registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        widgets: [
          makeWidget({
            id: 'widget-b',
            pluginId: 'plugin-a',
            slot: 'b',
            order: 20,
          }),
          makeWidget({
            id: 'widget-a',
            pluginId: 'plugin-a',
            slot: 'a',
            order: 10,
          }),
        ],
      });

      const widgets = registry.getWidgetExtensions();

      expect(widgets[0]?.id).toBe('widget-a');
      expect(widgets[1]?.id).toBe('widget-b');
    });

    it('should sort actions by order', () => {
      registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        actions: [
          makeAction({
            id: 'action-b',
            pluginId: 'plugin-a',
            target: 'b',
            actionKey: 'b',
            order: 20,
          }),
          makeAction({
            id: 'action-a',
            pluginId: 'plugin-a',
            target: 'a',
            actionKey: 'a',
            order: 10,
          }),
        ],
      });

      const actions = registry.getActionExtensions();

      expect(actions[0]?.id).toBe('action-a');
      expect(actions[1]?.id).toBe('action-b');
    });

    it('should treat undefined order as 0', () => {
      registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        navigation: [
          makeNav({
            id: 'nav-2',
            pluginId: 'plugin-a',
            label: 'Second',
            order: 10,
          }),
          makeNav({ id: 'nav-1', pluginId: 'plugin-a', label: 'Default' }),
        ],
      });

      const navs = registry.getNavigationExtensions();

      expect(navs[0]?.id).toBe('nav-1');
      expect(navs[1]?.id).toBe('nav-2');
    });

    it('should not mutate registration order during read operations', () => {
      registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        navigation: [
          makeNav({ id: 'nav-a', pluginId: 'plugin-a', label: 'A' }),
          makeNav({ id: 'nav-b', pluginId: 'plugin-a', label: 'B' }),
        ],
      });

      const firstRead = registry.getNavigationExtensions().map(({ id }) => id);
      const secondRead = registry.getNavigationExtensions().map(({ id }) => id);

      expect(firstRead).toEqual(['nav-a', 'nav-b']);
      expect(secondRead).toEqual(firstRead);
    });

    it('should keep stable order after plugin removal and later registration', () => {
      registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        navigation: [
          makeNav({ id: 'nav-a', pluginId: 'plugin-a', label: 'A' }),
          makeNav({ id: 'nav-b', pluginId: 'plugin-a', label: 'B' }),
        ],
      });

      registry.removePluginExtensions('plugin-a');
      registry.registerPluginExtensions('plugin-b', {
        ...emptyExtensions(),
        navigation: [
          makeNav({ id: 'nav-c', pluginId: 'plugin-b', label: 'C' }),
          makeNav({ id: 'nav-d', pluginId: 'plugin-b', label: 'D' }),
        ],
      });

      expect(registry.getNavigationExtensions().map(({ id }) => id)).toEqual([
        'nav-c',
        'nav-d',
      ]);
    });
  });

  describe('getExtensionsByKind', () => {
    it('should return navigation extensions for navigation kind', () => {
      registry.registerPluginExtensions('plugin-a', {
        navigation: [
          makeNav({ id: 'nav-1', pluginId: 'plugin-a', label: 'Nav' }),
        ],
        pages: [
          makePage({
            id: 'page-1',
            pluginId: 'plugin-a',
            route: '/p',
            title: 'P',
          }),
        ],
        widgets: [],
        actions: [],
      });

      const navs = registry.getExtensionsByKind('navigation');

      expect(navs).toHaveLength(1);
      expect(navs[0]?.id).toBe('nav-1');
    });

    it('should return page extensions for page kind', () => {
      registry.registerPluginExtensions('plugin-a', {
        navigation: [],
        pages: [
          makePage({
            id: 'page-1',
            pluginId: 'plugin-a',
            route: '/p',
            title: 'P',
          }),
        ],
        widgets: [],
        actions: [],
      });

      const pages = registry.getExtensionsByKind('page');

      expect(pages).toHaveLength(1);
    });
  });

  describe('findExtensionById', () => {
    it('should find navigation extension by ID', () => {
      registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        navigation: [
          makeNav({ id: 'nav-1', pluginId: 'plugin-a', label: 'Nav' }),
        ],
      });

      const found = registry.findExtensionById('nav-1');

      expect(found).toBeDefined();
      expect(found?.id).toBe('nav-1');
    });

    it('should find page extension by ID', () => {
      registry.registerPluginExtensions('plugin-a', {
        ...emptyExtensions(),
        pages: [
          makePage({
            id: 'page-1',
            pluginId: 'plugin-a',
            route: '/p',
            title: 'P',
          }),
        ],
      });

      const found = registry.findExtensionById('page-1');

      expect(found).toBeDefined();
    });

    it('should return undefined for non-existent ID', () => {
      expect(registry.findExtensionById('non-existent')).toBeUndefined();
    });
  });

  describe('getExtensionsByPluginId', () => {
    it('should return extensions for a specific plugin', () => {
      registry.registerPluginExtensions('plugin-a', {
        navigation: [
          makeNav({ id: 'nav-a', pluginId: 'plugin-a', label: 'Nav A' }),
        ],
        pages: [],
        widgets: [],
        actions: [],
      });

      registry.registerPluginExtensions('plugin-b', {
        navigation: [
          makeNav({ id: 'nav-b', pluginId: 'plugin-b', label: 'Nav B' }),
        ],
        pages: [
          makePage({
            id: 'page-b',
            pluginId: 'plugin-b',
            route: '/b',
            title: 'B',
          }),
        ],
        widgets: [],
        actions: [],
      });

      const aExtensions = registry.getExtensionsByPluginId('plugin-a');
      const bExtensions = registry.getExtensionsByPluginId('plugin-b');

      expect(aExtensions.navigation).toHaveLength(1);
      expect(aExtensions.pages).toHaveLength(0);
      expect(bExtensions.navigation).toHaveLength(1);
      expect(bExtensions.pages).toHaveLength(1);
    });
  });

  describe('removePluginExtensions', () => {
    it('should remove all extensions for a plugin', () => {
      registry.registerPluginExtensions('plugin-a', {
        navigation: [
          makeNav({ id: 'nav-a', pluginId: 'plugin-a', label: 'Nav A' }),
        ],
        pages: [
          makePage({
            id: 'page-a',
            pluginId: 'plugin-a',
            route: '/a',
            title: 'A',
          }),
        ],
        widgets: [],
        actions: [],
      });

      const removed = registry.removePluginExtensions('plugin-a');

      expect(removed).toBe(2);
      expect(registry.getTotalExtensionCount()).toBe(0);
    });

    it('should not affect other plugins', () => {
      registry.registerPluginExtensions('plugin-a', {
        navigation: [
          makeNav({ id: 'nav-a', pluginId: 'plugin-a', label: 'Nav A' }),
        ],
        pages: [],
        widgets: [],
        actions: [],
      });

      registry.registerPluginExtensions('plugin-b', {
        navigation: [
          makeNav({ id: 'nav-b', pluginId: 'plugin-b', label: 'Nav B' }),
        ],
        pages: [],
        widgets: [],
        actions: [],
      });

      registry.removePluginExtensions('plugin-a');

      expect(registry.getTotalExtensionCount()).toBe(1);
      expect(registry.getNavigationExtensions()[0]?.pluginId).toBe('plugin-b');
    });

    it('should return 0 for non-existent plugin', () => {
      expect(registry.removePluginExtensions('non-existent')).toBe(0);
    });
  });

  describe('clear', () => {
    it('should remove all extensions', () => {
      registry.registerPluginExtensions('plugin-a', {
        navigation: [
          makeNav({ id: 'nav-1', pluginId: 'plugin-a', label: 'Nav' }),
        ],
        pages: [
          makePage({
            id: 'page-1',
            pluginId: 'plugin-a',
            route: '/p',
            title: 'P',
          }),
        ],
        widgets: [
          makeWidget({ id: 'widget-1', pluginId: 'plugin-a', slot: 's' }),
        ],
        actions: [
          makeAction({
            id: 'action-1',
            pluginId: 'plugin-a',
            target: 't',
            actionKey: 'k',
          }),
        ],
      });

      registry.clear();

      expect(registry.getTotalExtensionCount()).toBe(0);
      expect(registry.getNavigationExtensions()).toHaveLength(0);
      expect(registry.getPageExtensions()).toHaveLength(0);
      expect(registry.getWidgetExtensions()).toHaveLength(0);
      expect(registry.getActionExtensions()).toHaveLength(0);
    });
  });

  describe('totalExtensionCount', () => {
    it('should count extensions across all kinds', () => {
      registry.registerPluginExtensions('plugin-a', {
        navigation: [
          makeNav({ id: 'nav-1', pluginId: 'plugin-a', label: 'N1' }),
          makeNav({ id: 'nav-2', pluginId: 'plugin-a', label: 'N2' }),
        ],
        pages: [
          makePage({
            id: 'page-1',
            pluginId: 'plugin-a',
            route: '/p',
            title: 'P',
          }),
        ],
        widgets: [],
        actions: [
          makeAction({
            id: 'action-1',
            pluginId: 'plugin-a',
            target: 't',
            actionKey: 'k',
          }),
        ],
      });

      expect(registry.getTotalExtensionCount()).toBe(4);
    });
  });
});
