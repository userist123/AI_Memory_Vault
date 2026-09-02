import { type IPlatformModuleManifest } from '@prosto/platform-sdk';

/**
 * Environment tier for policy enforcement.
 */
export type ModuleLoadingEnvironmentType =
  | 'production'
  | 'development'
  | 'test';

/**
 * Decision result from policy evaluation.
 */
export interface IModulePolicyDecision {
  /**
   * Whether the module is allowed to load.
   */
  readonly allowed: boolean;
  /**
   * Reason code for the decision.
   */
  readonly reasonCode: ModulePolicyReasonCode;
  /**
   * Human-readable message explaining the decision.
   */
  readonly message: string;
  /**
   * Suggested remediation if blocked.
   */
  readonly remediationHint?: string;
}

/**
 * Reason codes for policy decisions.
 */
export enum ModulePolicyReasonCode {
  /**
   * Module is explicitly allowed by policy.
   */
  Allowed = 'ALLOWED',
  /**
   * Module is not in the allowlist.
   */
  NotInAllowlist = 'NOT_IN_ALLOWLIST',
  /**
   * Module security class is blocked for the current environment.
   */
  SecurityClassBlocked = 'SECURITY_CLASS_BLOCKED',
  /**
   * Module manifest is missing required security metadata.
   */
  MissingSecurityMetadata = 'MISSING_SECURITY_METADATA',
  /**
   * Module version is not in the allowed version range.
   */
  VersionNotAllowed = 'VERSION_NOT_ALLOWED',
}

/**
 * Allowlist entry for a module.
 */
export interface IAllowlistEntry {
  /**
   * Module ID pattern (supports wildcards with `*`).
   */
  readonly moduleIdPattern: string;
  /**
   * Allowed version range (semver pattern).
   */
  readonly versionPattern?: string;
}

/**
 * Configuration for the allowlist policy.
 */
export interface IAllowlistPolicyConfig {
  /**
   * Environment tier for enforcement level.
   */
  readonly environment: ModuleLoadingEnvironmentType;
  /**
   * List of allowlist entries.
   */
  readonly allowlist: IAllowlistEntry[];
  /**
   * Whether to require allowlist matching (true for production).
   */
  readonly requireAllowlist?: boolean;
}

/**
 * @alpha
 * Module loading allowlist policy evaluator.
 * Enforces security controls based on module identity, version, and security classification.
 */
export class AllowlistPolicyEvaluator {
  private readonly _config: IAllowlistPolicyConfig;

  constructor(config: IAllowlistPolicyConfig) {
    this._config = {
      requireAllowlist: config.environment === 'production',
      ...config,
    };
  }

  /**
   * Evaluate whether a module should be allowed to load.
   */
  evaluate(module: IPlatformModuleManifest): IModulePolicyDecision {
    // Check allowlist if required
    if (this._config.requireAllowlist) {
      const match = this._findAllowlistMatch(module);

      if (!match) {
        return {
          allowed: false,
          reasonCode: ModulePolicyReasonCode.NotInAllowlist,
          message: `Module "${module.id}@${module.version}" is not in the allowlist for ${this._config.environment} environment.`,
          remediationHint:
            'Add module to the allowlist configuration or use an allowed module.',
        };
      }
    }

    return {
      allowed: true,
      reasonCode: ModulePolicyReasonCode.Allowed,
      message: `Module "${module.id}@${module.version}" is allowed to load.`,
    };
  }

  /**
   * Get the current policy configuration.
   */
  getConfig(): IAllowlistPolicyConfig {
    return { ...this._config };
  }

  /**
   * Check if a module ID matches a pattern.
   */
  private _matchesPattern(moduleId: string, pattern: string): boolean {
    // Convert glob-like pattern to regex
    const regexPattern = pattern
      .replace(/\./g, '\\.')
      .replace(/\*/g, '.*')
      .replace(/\?/g, '.');
    const regex = new RegExp(`^${regexPattern}$`);

    return regex.test(moduleId);
  }

  /**
   * Find a matching allowlist entry for the module.
   */
  private _findAllowlistMatch(
    module: IPlatformModuleManifest,
  ): IAllowlistEntry | null {
    for (const entry of this._config.allowlist) {
      if (!this._matchesPattern(module.id, entry.moduleIdPattern)) {
        continue;
      }

      // Check version pattern if specified
      if (entry.versionPattern) {
        const versionMatch = this._matchesVersion(
          module.version,
          entry.versionPattern,
        );

        if (!versionMatch) {
          continue;
        }
      }

      return entry;
    }

    return null;
  }

  /**
   * Check if a version matches a semver pattern.
   * Simple implementation supporting exact match and caret/tilde ranges.
   */
  private _matchesVersion(version: string, pattern: string): boolean {
    // Exact match
    if (pattern === version) {
      return true;
    }

    // Caret range (^1.2.3)
    if (pattern.startsWith('^')) {
      const baseVersion = pattern.slice(1);
      const [baseMajor] = baseVersion.split('.');
      const [versionMajor] = version.split('.');

      return versionMajor === baseMajor && version >= baseVersion;
    }

    // Tilde range (~1.2.3)
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

    // Wildcard (1.2.*)
    if (pattern.includes('*')) {
      const regexPattern = pattern.replace(/\./g, '\\.').replace(/\*/g, '.*');
      const regex = new RegExp(`^${regexPattern}$`);

      return regex.test(version);
    }

    // Greater than or equal
    if (pattern.startsWith('>=')) {
      return version >= pattern.slice(2);
    }

    return false;
  }
}
