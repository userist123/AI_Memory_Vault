import { describe, expect, it } from 'vitest';
import {
  ADMIN_DISCOVERY_PAYLOAD_SCHEMA_VERSION,
  AdminDiscoveryPayloadValidationError,
  AdminDiscoveryPayloadValidator,
  type IAdminDiscoveredPluginDescriptor,
  type IAdminDiscoveryPayload,
} from '@/index.js';

const validPlugin: IAdminDiscoveredPluginDescriptor = {
  id: 'admin-health',
  version: '1.2.3',
  displayName: 'Health',
  shellCompatibility: '^0.1.0',
  trustClass: 'internal',
  reviewStatus: 'approved',
  extensions: {
    navigation: [
      {
        id: 'health.nav',
        pluginId: 'admin-health',
        label: 'Health',
        pageId: 'health.page',
        order: 10,
      },
    ],
    pages: [
      {
        id: 'health.page',
        pluginId: 'admin-health',
        route: '/health',
        title: 'Health',
        componentKey: 'health.page',
      },
    ],
    widgets: [
      {
        id: 'health.summary',
        pluginId: 'admin-health',
        slot: 'dashboard.summary',
        title: 'Health summary',
        componentKey: 'health.summary',
      },
    ],
    actions: [
      {
        id: 'health.refresh',
        pluginId: 'admin-health',
        target: 'health.page',
        label: 'Refresh',
        actionKey: 'health.refresh',
      },
    ],
  },
};

const validPayload: IAdminDiscoveryPayload = {
  schemaVersion: ADMIN_DISCOVERY_PAYLOAD_SCHEMA_VERSION,
  generatedAt: '2026-06-03T12:00:00.000Z',
  plugins: [validPlugin],
  rejected: [
    {
      id: 'admin-audit',
      version: '0.1.0',
      reasonCode: 'PLUGIN_NOT_ALLOWLISTED',
      message: 'Plugin is not approved for this environment.',
      remediationHint: 'Add the plugin to the admin UI allowlist.',
    },
  ],
};

describe('admin discovery payload validation', () => {
  const payloadValidator = new AdminDiscoveryPayloadValidator();

  it('accepts a valid discovery payload', () => {
    const parsedPayload = payloadValidator.parse(validPayload);

    expect(parsedPayload.plugins).toHaveLength(1);
    expect(parsedPayload.rejected[0]?.reasonCode).toBe(
      'PLUGIN_NOT_ALLOWLISTED',
    );
  });

  it('returns failure for schema violations', () => {
    const result = payloadValidator.validate({
      ...validPayload,
      generatedAt: 'not-a-date',
    });

    expect(result.success).toBe(false);

    if (result.success) {
      throw new Error('Expected validation failure.');
    }

    expect(result.error).toBeInstanceOf(AdminDiscoveryPayloadValidationError);
    expect(
      result.error.issues.some((issue) => issue.path === 'generatedAt'),
    ).toBe(true);
  });

  it('returns failure when a descriptor references another plugin', () => {
    const result = payloadValidator.validate({
      ...validPayload,
      plugins: [
        {
          ...validPlugin,
          extensions: {
            ...validPlugin.extensions,
            pages: [
              {
                id: 'health.page',
                pluginId: 'admin-other',
                route: '/health',
                title: 'Health',
                componentKey: 'health.page',
              },
            ],
          },
        },
      ],
    });

    expect(result.success).toBe(false);

    if (result.success) {
      throw new Error('Expected validation failure.');
    }

    expect(
      result.error.issues.some(
        (issue) => issue.code === 'descriptor_plugin_mismatch',
      ),
    ).toBe(true);
  });
});
