import type { IAdminUIPluginManifest } from '../manifests/index.js';
import type {
  AdminCompatibilityContractVersionType,
  AdminPluginCompatibilityReasonCodeType,
} from './admin-compatibility.types.js';

/**
 * @alpha
 * Input for shell-to-plugin admin compatibility evaluation.
 */
export interface IAdminPluginCompatibilityInput {
  readonly shellVersion: string;
  readonly supportedContractVersion:
    | AdminCompatibilityContractVersionType
    | string;
  readonly pluginContractVersion: AdminCompatibilityContractVersionType;
  readonly manifest: IAdminUIPluginManifest;
}

/**
 * @alpha
 * Successful admin plugin compatibility decision.
 */
export interface IAdminPluginCompatibilityAllowedResult {
  readonly allowed: true;
  readonly contractVersion: AdminCompatibilityContractVersionType;
}

/**
 * @alpha
 * Rejected admin plugin compatibility decision with stable reason taxonomy.
 */
export interface IAdminPluginCompatibilityRejectedResult {
  readonly allowed: false;
  readonly contractVersion: AdminCompatibilityContractVersionType;
  readonly reasonCode: AdminPluginCompatibilityReasonCodeType;
  readonly message: string;
  readonly remediationHint: string;
}

/**
 * @alpha
 * Compatibility decision between an admin shell and a UI plugin manifest.
 */
export type AdminPluginCompatibilityResultType =
  | IAdminPluginCompatibilityAllowedResult
  | IAdminPluginCompatibilityRejectedResult;

/**
 * @alpha
 * Contract for admin plugin compatibility rule implementations.
 */
export interface IAdminPluginCompatibilityEvaluator {
  evaluate(
    input: IAdminPluginCompatibilityInput,
  ): AdminPluginCompatibilityResultType;
}
