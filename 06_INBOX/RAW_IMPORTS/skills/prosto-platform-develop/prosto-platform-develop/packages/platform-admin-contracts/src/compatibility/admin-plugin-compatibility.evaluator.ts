import { satisfies, valid } from 'semver';
import { ADMIN_UI_PLUGIN_MANIFEST_SCHEMA_VERSION } from '../manifests/index.js';
import { ADMIN_COMPATIBILITY_CONTRACT_VERSION } from './admin-compatibility.constants.js';
import type {
  AdminPluginCompatibilityResultType,
  IAdminPluginCompatibilityEvaluator,
  IAdminPluginCompatibilityInput,
} from './admin-compatibility.interfaces.js';
import type { AdminPluginCompatibilityReasonCodeType } from './admin-compatibility.types.js';

/**
 * @alpha
 * Default compatibility evaluator for admin shell and plugin manifest contracts.
 */
export class AdminPluginCompatibilityEvaluator implements IAdminPluginCompatibilityEvaluator {
  evaluate(
    input: IAdminPluginCompatibilityInput,
  ): AdminPluginCompatibilityResultType {
    if (input.supportedContractVersion !== input.pluginContractVersion) {
      return this._reject(
        'CONTRACT_VERSION_MISMATCH',
        `Shell supports "${input.supportedContractVersion}" but plugin declares "${input.pluginContractVersion}".`,
        'Publish the plugin with the admin compatibility contract version supported by the shell.',
      );
    }

    if (
      input.supportedContractVersion !== ADMIN_COMPATIBILITY_CONTRACT_VERSION
    ) {
      return this._reject(
        'CONTRACT_VERSION_MISMATCH',
        `Unsupported admin compatibility contract "${input.supportedContractVersion}".`,
        'Use a supported admin compatibility contract version.',
      );
    }

    if (
      input.manifest.schemaVersion !== ADMIN_UI_PLUGIN_MANIFEST_SCHEMA_VERSION
    ) {
      return this._reject(
        'PLUGIN_MANIFEST_INVALID',
        `Unsupported plugin manifest schema "${input.manifest.schemaVersion}".`,
        'Publish the plugin with the current admin UI plugin manifest schema.',
      );
    }

    if (valid(input.shellVersion) === null) {
      return this._reject(
        'SHELL_VERSION_INVALID',
        `Shell version "${input.shellVersion}" is not valid semver.`,
        'Provide a valid semver shell version before evaluating plugin compatibility.',
      );
    }

    if (valid(input.manifest.version) === null) {
      return this._reject(
        'PLUGIN_VERSION_INVALID',
        `Plugin version "${input.manifest.version}" is not valid semver.`,
        'Publish the plugin with a valid semver version.',
      );
    }

    if (!satisfies(input.shellVersion, input.manifest.shellCompatibility)) {
      return this._reject(
        'SHELL_VERSION_MISMATCH',
        `Shell version "${input.shellVersion}" does not satisfy plugin range "${input.manifest.shellCompatibility}".`,
        'Install a compatible admin shell version or widen the plugin shell compatibility range.',
      );
    }

    return {
      allowed: true,
      contractVersion: ADMIN_COMPATIBILITY_CONTRACT_VERSION,
    };
  }

  protected _reject(
    reasonCode: AdminPluginCompatibilityReasonCodeType,
    message: string,
    remediationHint: string,
  ): AdminPluginCompatibilityResultType {
    return {
      allowed: false,
      contractVersion: ADMIN_COMPATIBILITY_CONTRACT_VERSION,
      reasonCode,
      message,
      remediationHint,
    };
  }
}
