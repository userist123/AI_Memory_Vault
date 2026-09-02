import type {
  IPlatformModuleContext,
  IPlatformModuleManifestValidator,
  IPlatformModule,
  IPlatformModuleManifest,
} from '@prosto/platform-sdk';
import type { ContractFailureCodes } from '@/constants/index.js';

/**
 * @alpha
 * Severity level for contract conformance checks.
 */
export type ContractCheckSeverityType = 'mandatory' | 'advisory';

/**
 * @alpha
 * Single failure code identifier.
 */
export type ContractFailureCodeType = `${ContractFailureCodes}`;

/**
 * @alpha
 * Structured check outcome used in machine-readable reports.
 */
export interface IContractCheckResult {
  id: string;
  title: string;
  passed: boolean;
  /** Severity level for contract conformance checks */
  severity: ContractCheckSeverityType;
  /** Contract failure code */
  code: ContractFailureCodeType | null;
  details: string;
}

/**
 * @alpha
 * Conformance summary for quick CI gate decisions.
 */
export interface IContractConformanceSummary {
  totalChecks: number;
  passedChecks: number;
  failedMandatoryChecks: number;
  failedAdvisoryChecks: number;
  result: 'pass' | 'fail';
}

/**
 * @alpha
 * Machine-readable report produced by the conformance suite.
 */
export interface IModuleContractConformanceReport {
  moduleId: string;
  moduleVersion: string;
  generatedAt: string;
  checks: IContractCheckResult[];
  summary: IContractConformanceSummary;
}

/**
 * @alpha
 * Runtime context for module lifecycle checks.
 */
export interface IModuleLifecycleContextFactory {
  create(moduleManifest: IPlatformModuleManifest): IPlatformModuleContext;
}

/**
 * @alpha
 * Input contract for conformance execution.
 */
export interface IModuleContractTestInput {
  module: IPlatformModule;
  manifest: IPlatformModuleManifest;
  manifestValidator?: IPlatformModuleManifestValidator;
  moduleLifecycleContextFactory?: IModuleLifecycleContextFactory;
  now?: () => string;
}

/**
 * @alpha
 * Minimal test runner contract used by createModuleContractTests.
 */
export interface IContractTestRunnerApi {
  describe(name: string, body: () => void): void;
  it(name: string, body: () => Promise<void> | void): void;
}
