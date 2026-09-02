import type { PlatformStartupPolicyType } from '@prosto/platform-sdk';
import type { IModuleLifecycleShutdownIssue } from '@/modularity/index.js';
import type { IRuntimeFailureDiagnostic } from './runtime-failure-diagnostic.interface.js';
import type { IRuntimeLoadedModuleDiagnostic } from './runtime-loaded-module-diagnostic.interface.js';
import type { IRuntimeShutdownReport } from './runtime-shutdown-report.interface.js';
import type { IRuntimeSkippedModuleDiagnostic } from './runtime-skipped-module-diagnostic.interface.js';
import type { IRuntimeStartupReport } from './runtime-startup-report.interface.js';

/**
 * @alpha
 * Interface representing input for generating a runtime startup diagnostic report.
 */
export interface IStartupReportInput {
  readonly policyMode: PlatformStartupPolicyType;
  readonly correlationId: string;
  readonly startedAt: string;
  readonly loadedModules: readonly IRuntimeLoadedModuleDiagnostic[];
  readonly skippedModules: readonly IRuntimeSkippedModuleDiagnostic[];
  readonly failedModules: readonly IRuntimeFailureDiagnostic[];
}

/**
 * @alpha
 * Interface representing input for generating a runtime shutdown diagnostic report.
 */
export interface IShutdownReportInput {
  readonly correlationId: string;
  readonly startedAt: string;
  readonly stopOrder: readonly string[];
  readonly issues: readonly IModuleLifecycleShutdownIssue[];
}

/**
 * @alpha
 * Diagnostics reporter contract for creating startup and shutdown reports.
 */
export interface IDiagnosticsReporter {
  createStartupReport(input: IStartupReportInput): IRuntimeStartupReport;
  createShutdownReport(input: IShutdownReportInput): IRuntimeShutdownReport;
}
