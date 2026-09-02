import type { IAdminDiscoveredPluginDescriptor } from '@/discovery/index.js';
import {
  ADMIN_UI_PLUGIN_MANIFEST_SCHEMA_VERSION,
  type AdminUIPluginExtensionPointType,
  CAPABILITY_METADATA_KEY,
  type IAdminUIPluginManifest,
  PERMISSION_METADATA_KEY,
} from '@/manifests/index.js';

/**
 * Parse a JSON-encoded string array from a metadata record.
 * Returns an empty array when the key is absent, the value is not valid JSON,
 * or the parsed result is not an array of strings.
 */
function parseJsonArrayFromMetadata(
  metadata: Record<string, string> | undefined,
  key: string,
): string[] {
  const raw = metadata?.[key];

  if (!raw) {
    return [];
  }

  try {
    const parsed: unknown = JSON.parse(raw);

    if (Array.isArray(parsed)) {
      return parsed.filter((item): item is string => typeof item === 'string');
    }
  } catch {
    // Malformed JSON — treat as empty
  }

  return [];
}

/**
 * @alpha
 * Convert an {@link IAdminDiscoveredPluginDescriptor} (post-discovery payload)
 * into an {@link IAdminUIPluginManifest} (framework-neutral plugin manifest)
 * suitable for compatibility evaluation, permission gating, and plugin store
 * registration.
 *
 * Field mapping:
 * - Shared identity and review fields are copied directly.
 * - `schemaVersion` is set to the current manifest schema constant.
 * - `extensionPoints` is derived from non-empty extension arrays.
 * - `requiredPermissions` and `requiredCapabilities` are parsed from
 *   JSON-encoded metadata entries.
 * - `reviewedAt` and `reviewer` are left unset (discovery does not carry them).
 */
export function convertDescriptorToManifest(
  descriptor: IAdminDiscoveredPluginDescriptor,
): IAdminUIPluginManifest {
  const { extensions, ...rest } = descriptor;

  const extensionPoints: AdminUIPluginExtensionPointType[] = [];

  if (extensions.navigation.length > 0) extensionPoints.push('nav');
  if (extensions.pages.length > 0) extensionPoints.push('page');
  if (extensions.widgets.length > 0) extensionPoints.push('widget');
  if (extensions.actions.length > 0) extensionPoints.push('action');

  return {
    ...rest,
    schemaVersion: ADMIN_UI_PLUGIN_MANIFEST_SCHEMA_VERSION,
    extensionPoints,
    requiredPermissions: parseJsonArrayFromMetadata(
      descriptor.metadata,
      PERMISSION_METADATA_KEY,
    ),
    requiredCapabilities: parseJsonArrayFromMetadata(
      descriptor.metadata,
      CAPABILITY_METADATA_KEY,
    ),
  };
}
