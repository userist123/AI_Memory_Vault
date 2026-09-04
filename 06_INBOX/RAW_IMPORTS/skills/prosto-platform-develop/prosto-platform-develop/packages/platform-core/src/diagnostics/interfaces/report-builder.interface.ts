import type { PlatformStartupPolicyType } from '@prosto/platform-sdk';
import type { IModuleLifecycleShutdownIssue } from '@/modularity/index.js';
import type { IRuntimeFailureDiagnostic } from './runtime-failure-diagnostic.interface.js';
import type { IRuntimeLoadedModuleDiagnostic } from './runtime-loaded-module-diagnostic.interface.js';
import type { IRuntimeShutdownReport } from './runtime-shutdown-report.interface.js';
import type { IRuntimeSkippedModuleDiagnostic } from './runtime-skipped-module-diagnostic.interface.js';
import type { IRuntimeStartupReport } from './runtime-startup-report.interface.js';

/**
 * @alpha
 * Input context for building a startup report.
 */
export interface IStartupReportBuildContext {
  readonly policyMode: PlatformStartupPolicyType;
  readonly correlationId: string;
  readonly startedAt: string;
  readonly loadedModules: readonly IRuntimeLoadedModuleDiagnostic[];
  readonly skippedModules: readonly IRuntimeSkippedModuleDiagnostic[];
  readonly failedModules: readonly IRuntimeFailureDiagnostic[];
}

/**
 * @alpha
 * Input context for building a shutdown report.
 */
export interface IShutdownReportBuildContext {
  readonly correlationId: string;
  readonly startedAt: string;
  readonly stopOrder: readonly string[];
  readonly issues: readonly IModuleLifecycleShutdownIssue[];
}

/**
 * @alpha
 * Builder contract for constructing runtime diagnostic reports.
 */
export interface IReportBuilder {
  buildStartupReport(
    context: IStartupReportBuildContext,
  ): IRuntimeStartupReport;
  buildShutdownReport(
    context: IShutdownReportBuildContext,
  ): IRuntimeShutdownReport;
}
