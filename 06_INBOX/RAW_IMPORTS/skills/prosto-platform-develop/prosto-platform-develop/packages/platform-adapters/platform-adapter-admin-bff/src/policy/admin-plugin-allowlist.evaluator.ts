import type {
  AdminPluginPolicyResultType,
  IAdminPluginAllowlistEntry,
  IAdminPluginAllowlistEvaluator,
} from './admin-plugin-policy.interfaces.js';

/**
 * @alpha
 * Configuration for the admin plugin allowlist evaluator.
 */
export interface IAdminPluginAllowlistEvaluatorConfig {
  readonly entries: readonly IAdminPluginAllowlistEntry[];
  readonly requireAllowlist: boolean;
}

/**
 * @alpha
 * Admin UI plugin allowlist evaluator.
 *
 * Determines whether a plugin is permitted by the environment-level
 * allowlist before admission into discovery payloads.
 *
 * Supports glob-like patterns for plugin IDs (`*` matches any characters)
 * and semver-style version patterns (`^1.0.0`, `~1.2.0`, `1.*`).
 */
export class AdminPluginAllowlistEvaluator implements IAdminPluginAllowlistEvaluator {
  private readonly _config: IAdminPluginAllowlistEvaluatorConfig;

  constructor(config: IAdminPluginAllowlistEvaluatorConfig) {
    this._config = config;
  }

  evaluate(
    pluginId: string,
    pluginVersion: string,
  ): AdminPluginPolicyResultType {
    if (!this._config.requireAllowlist) {
      return { allowed: true };
    }

    const match = this._findMatch(pluginId, pluginVersion);

    if (!match) {
      return {
        allowed: false,
        reasonCode: 'ALLOWLIST_REJECTED',
        message: `Plugin "${pluginId}@${pluginVersion}" is not in the allowlist.`,
        remediationHint:
          'Add the plugin to the allowlist configuration or use an approved plugin.',
      };
    }

    return { allowed: true };
  }

  private _findMatch(pluginId: string, pluginVersion: string): boolean {
    for (const entry of this._config.entries) {
      if (!this._matchesPattern(pluginId, entry.pluginIdPattern)) {
        continue;
      }

      if (entry.versionPattern) {
        if (!this._matchesVersion(pluginVersion, entry.versionPattern)) {
          continue;
        }
      }

      return true;
    }

    return false;
  }

  private _matchesPattern(value: string, pattern: string): boolean {
    const regexPattern = pattern
      .replace(/\./g, '\\.')
      .replace(/\*/g, '.*')
      .replace(/\?/g, '.');
    const regex = new RegExp(`^${regexPattern}$`);
    return regex.test(value);
  }

  private _matchesVersion(version: string, pattern: string): boolean {
    if (pattern === version) {
      return true;
    }

    if (pattern.startsWith('^')) {
      const baseVersion = pattern.slice(1);
      const [baseMajor] = baseVersion.split('.');
      const [versionMajor] = version.split('.');
      return versionMajor === baseMajor && version >= baseVersion;
    }

    if (pattern.startsWith('~')) {
      const baseVersion = pattern.slice(1);
      const [baseMajor, baseMinor] = baseVersion.split('.');
      const [versionMajor, versionMinor] = version.split('.');
      return (
        versionMajor === baseMajor &&
        versionMinor === baseMinor &&
        version >= baseVersion
      );
    }

    if (pattern.includes('*')) {
      const regexPattern = pattern.replace(/\./g, '\\.').replace(/\*/g, '.*');
      const regex = new RegExp(`^${regexPattern}$`);
      return regex.test(version);
    }

    if (pattern.startsWith('>=')) {
      return version >= pattern.slice(2);
    }

    return false;
  }
}
