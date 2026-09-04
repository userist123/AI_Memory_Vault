import type {
  IAdminNavigationExtensionDescriptor,
  IAdminPluginDiscoveryExtensions,
  IAdminUIPluginManifest,
} from '@prosto/platform-admin-contracts';
import { describe, it, expect } from 'vitest';
import { PermissionGuardService } from '@/features/permissions/model/permission-guard.service.js';

function makeManifest(
  overrides: Partial<IAdminUIPluginManifest> = {},
): IAdminUIPluginManifest {
  return {
    id: 'test-plugin',
    version: '1.0.0',
    schemaVersion: 'admin-ui-plugin-manifest.v1',
    shellCompatibility: '>=0.0.0',
    extensionPoints: [],
    requiredPermissions: [],
    requiredCapabilities: [],
    trustClass: 'trusted',
    reviewStatus: 'approved',
    ...overrides,
  };
}

function makeDiscoveryExtensions(): IAdminPluginDiscoveryExtensions {
  return {
    navigation: [],
    pages: [],
    widgets: [],
    actions: [],
  };
}

describe('PermissionGuardService', () => {
  describe('evaluatePluginAccess', () => {
    it('should allow plugin with no required permissions', () => {
      const guard = new PermissionGuardService([]);
      const manifest = makeManifest({ requiredPermissions: [] });
      const decision = guard.evaluatePluginAccess(manifest);

      expect(decision.allowed).toBe(true);
      expect(decision.pluginId).toBe('test-plugin');
      expect(decision.missingPermissions).toEqual([]);
    });

    it('should allow plugin when user has all required permissions', () => {
      const guard = new PermissionGuardService(['admin', 'plugins.read']);
      const manifest = makeManifest({
        requiredPermissions: ['admin', 'plugins.read'],
      });
      const decision = guard.evaluatePluginAccess(manifest);

      expect(decision.allowed).toBe(true);
      expect(decision.missingPermissions).toEqual([]);
    });

    it('should deny plugin when user is missing permissions', () => {
      const guard = new PermissionGuardService(['admin']);
      const manifest = makeManifest({
        requiredPermissions: ['admin', 'plugins.write'],
      });
      const decision = guard.evaluatePluginAccess(manifest);

      expect(decision.allowed).toBe(false);
      expect(decision.missingPermissions).toEqual(['plugins.write']);
    });

    it('should deny plugin when user has no permissions', () => {
      const guard = new PermissionGuardService([]);
      const manifest = makeManifest({
        requiredPermissions: ['admin'],
      });
      const decision = guard.evaluatePluginAccess(manifest);

      expect(decision.allowed).toBe(false);
      expect(decision.missingPermissions).toEqual(['admin']);
    });

    it('should deny plugin with multiple missing permissions', () => {
      const guard = new PermissionGuardService(['other']);
      const manifest = makeManifest({
        requiredPermissions: ['admin', 'plugins.read', 'plugins.write'],
      });
      const decision = guard.evaluatePluginAccess(manifest);

      expect(decision.allowed).toBe(false);
      expect(decision.missingPermissions).toEqual([
        'admin',
        'plugins.read',
        'plugins.write',
      ]);
    });
  });

  describe('evaluateDescriptorAccess', () => {
    it('should return true when no metadata provided', () => {
      const guard = new PermissionGuardService(['admin']);
      expect(guard.evaluateDescriptorAccess(undefined)).toBe(true);
    });

    it('should return true when no permissions declared', () => {
      const guard = new PermissionGuardService(['admin']);
      expect(guard.evaluateDescriptorAccess({ other: 'value' })).toBe(true);
    });

    it('should return true when user has required permissions', () => {
      const guard = new PermissionGuardService(['admin', 'nav.read']);
      const metadata = {
        requiredPermissions: JSON.stringify(['admin', 'nav.read']),
      };
      expect(guard.evaluateDescriptorAccess(metadata)).toBe(true);
    });

    it('should return false when user is missing permissions', () => {
      const guard = new PermissionGuardService(['admin']);
      const metadata = {
        requiredPermissions: JSON.stringify(['admin', 'nav.write']),
      };
      expect(guard.evaluateDescriptorAccess(metadata)).toBe(false);
    });

    it('should support any strategy', () => {
      const guard = new PermissionGuardService(['nav.read']);
      const metadata = {
        requiredPermissions: JSON.stringify(['admin', 'nav.read']),
        permissionMatchStrategy: 'any',
      };
      expect(guard.evaluateDescriptorAccess(metadata)).toBe(true);
    });
  });

  describe('filterExtensions', () => {
    it('should return empty extensions when user has no permissions', () => {
      const guard = new PermissionGuardService([]);
      const extensions = makeDiscoveryExtensions();
      const filtered = guard.filterExtensions(extensions);

      expect(filtered.navigation).toHaveLength(0);
      expect(filtered.pages).toHaveLength(0);
      expect(filtered.widgets).toHaveLength(0);
      expect(filtered.actions).toHaveLength(0);
    });

    it('should return all extensions when no permission metadata', () => {
      const guard = new PermissionGuardService(['admin']);
      const extensions: IAdminPluginDiscoveryExtensions = {
        ...makeDiscoveryExtensions(),
        navigation: [
          {
            id: 'nav-1',
            pluginId: 'plugin-a',
            label: 'Dashboard',
            metadata: undefined,
          },
        ],
      };
      const filtered = guard.filterExtensions(extensions);

      expect(filtered.navigation).toHaveLength(1);
      expect(filtered.navigation[0]?.id).toBe('nav-1');
    });

    it('should filter navigation descriptors by permissions', () => {
      const guard = new PermissionGuardService(['admin']);
      const extensions: IAdminPluginDiscoveryExtensions = {
        ...makeDiscoveryExtensions(),
        navigation: [
          {
            id: 'nav-public',
            pluginId: 'plugin-a',
            label: 'Public',
            metadata: undefined,
          },
          {
            id: 'nav-protected',
            pluginId: 'plugin-b',
            label: 'Protected',
            metadata: {
              requiredPermissions: JSON.stringify(['superadmin']),
            },
          } as IAdminNavigationExtensionDescriptor,
        ],
      };
      const filtered = guard.filterExtensions(extensions);

      expect(filtered.navigation).toHaveLength(1);
      expect(filtered.navigation[0]?.id).toBe('nav-public');
    });

    it('should preserve extensions that meet permission requirements', () => {
      const guard = new PermissionGuardService(['admin', 'reports.read']);
      const extensions: IAdminPluginDiscoveryExtensions = {
        ...makeDiscoveryExtensions(),
        navigation: [
          {
            id: 'nav-1',
            pluginId: 'plugin-a',
            label: 'Reports',
            metadata: {
              requiredPermissions: JSON.stringify(['reports.read']),
            },
          } as IAdminNavigationExtensionDescriptor,
        ],
      };
      const filtered = guard.filterExtensions(extensions);

      expect(filtered.navigation).toHaveLength(1);
      expect(filtered.navigation[0]?.id).toBe('nav-1');
    });
  });

  describe('checkPermission', () => {
    it('should return true with all strategy when all permissions present', () => {
      const guard = new PermissionGuardService(['a', 'b']);
      expect(guard.checkPermission(['a', 'b'], 'all')).toBe(true);
    });

    it('should return false with all strategy when missing permissions', () => {
      const guard = new PermissionGuardService(['a']);
      expect(guard.checkPermission(['a', 'b'], 'all')).toBe(false);
    });

    it('should return true with any strategy when at least one permission present', () => {
      const guard = new PermissionGuardService(['a']);
      expect(guard.checkPermission(['a', 'b'], 'any')).toBe(true);
    });

    it('should return false with any strategy when no permissions present', () => {
      const guard = new PermissionGuardService(['c']);
      expect(guard.checkPermission(['a', 'b'], 'any')).toBe(false);
    });

    it('should return true when required list is empty', () => {
      const guard = new PermissionGuardService([]);
      expect(guard.checkPermission([], 'all')).toBe(true);
    });
  });

  describe('parseDescriptorMetadata', () => {
    it('should return empty when metadata is undefined', () => {
      const guard = new PermissionGuardService([]);
      const result = guard.parseDescriptorMetadata(undefined);

      expect(result.requiredPermissions).toEqual([]);
      expect(result.strategy).toBe('all');
    });

    it('should parse permission data from metadata', () => {
      const guard = new PermissionGuardService([]);
      const result = guard.parseDescriptorMetadata({
        requiredPermissions: JSON.stringify(['admin']),
        permissionMatchStrategy: 'any',
      });

      expect(result.requiredPermissions).toEqual(['admin']);
      expect(result.strategy).toBe('any');
    });

    it('should ignore invalid permission metadata JSON', () => {
      const guard = new PermissionGuardService([]);
      const result = guard.parseDescriptorMetadata({
        requiredPermissions: '{not-json',
        permissionMatchStrategy: 'invalid',
      });

      expect(result.requiredPermissions).toEqual([]);
      expect(result.strategy).toBe('all');
    });

    it('should keep only string permission tokens from metadata arrays', () => {
      const guard = new PermissionGuardService([]);
      const result = guard.parseDescriptorMetadata({
        requiredPermissions: JSON.stringify([
          'admin',
          42,
          null,
          'plugins.read',
        ]),
      });

      expect(result.requiredPermissions).toEqual(['admin', 'plugins.read']);
    });
  });

  describe('getUserPermissions', () => {
    it('should return a copy of user permissions', () => {
      const guard = new PermissionGuardService(['a', 'b']);
      const perms = guard.getUserPermissions();

      expect(perms).toEqual(['a', 'b']);
      expect(perms).not.toBe(guard.getUserPermissions());
      expect(guard.getUserPermissions()).toEqual(['a', 'b']);
    });

    it('should accept readonly permission inputs without mutating them', () => {
      const permissions = ['a', 'b'] as const;
      const guard = new PermissionGuardService(permissions);

      expect(guard.hasPermission(['a', 'b'])).toBe(true);
      expect(permissions).toEqual(['a', 'b']);
    });
  });
});
