import type {
  IRuntimeShutdownReport,
  IRuntimeStartupReport,
  IShutdownReportBuildContext,
  IStartupReportBuildContext,
} from '../interfaces/index.js';
import { dateNowIso } from '@/common/index.js';
import { ReportBaseBuilder } from './report.base-builder.js';

/**
 * @alpha
 * Concrete implementation of ReportBuilderBase for creating diagnostic reports.
 */
export class DiagnosticReportBuilder extends ReportBaseBuilder {
  /**
   * Builds a startup report from the provided context.
   */
  override buildStartupReport(
    context: IStartupReportBuildContext,
  ): IRuntimeStartupReport {
    return {
      type: 'startup',
      policyMode: context.policyMode,
      correlationId: context.correlationId,
      startedAt: context.startedAt,
      completedAt: dateNowIso(),
      status: this.determineStartupStatus(
        context.loadedModules,
        context.skippedModules,
        context.failedModules,
      ),
      degraded: context.skippedModules.length > 0,
      loadedModules: [...context.loadedModules],
      skippedModules: context.skippedModules.map((skippedModule) => ({
        ...skippedModule,
        reason: this.sanitizeFailure(skippedModule.reason),
      })),
      failedModules: context.failedModules.map((failedModule) =>
        this.sanitizeFailure(failedModule),
      ),
    };
  }

  /**
   * Builds a shutdown report from the provided context.
   */
  override buildShutdownReport(
    context: IShutdownReportBuildContext,
  ): IRuntimeShutdownReport {
    return {
      type: 'shutdown',
      correlationId: context.correlationId,
      startedAt: context.startedAt,
      completedAt: dateNowIso(),
      stopOrder: [...context.stopOrder],
      issues: context.issues.map((issue) => this.sanitizeIssue(issue)),
    };
  }
}
