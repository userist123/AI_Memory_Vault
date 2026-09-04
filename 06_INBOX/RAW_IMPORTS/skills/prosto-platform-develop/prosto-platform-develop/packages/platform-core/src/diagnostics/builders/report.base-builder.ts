import type { IModuleLifecycleShutdownIssue } from '@/modularity/index.js';
import type { ISecretsRedactor } from '@/security/index.js';
import { SecretsRedactor } from '@/security/index.js';
import type {
  IReportBuilder,
  IRuntimeFailureDiagnostic,
  IRuntimeLoadedModuleDiagnostic,
  IRuntimeShutdownReport,
  IRuntimeSkippedModuleDiagnostic,
  IRuntimeStartupReport,
  IShutdownReportBuildContext,
  IStartupReportBuildContext,
} from '../interfaces/index.js';
import { RuntimeStartupStatus } from '../constants/index.js';

/**
 * @alpha
 * Abstract base class for diagnostic report builders.
 */
export abstract class ReportBaseBuilder implements IReportBuilder {
  constructor(
    protected readonly _secretsRedactor: ISecretsRedactor = new SecretsRedactor(),
  ) {}

  /**
   * Builds a startup report from the provided context.
   */
  abstract buildStartupReport(
    context: IStartupReportBuildContext,
  ): IRuntimeStartupReport;

  /**
   * Builds a shutdown report from the provided context.
   */
  abstract buildShutdownReport(
    context: IShutdownReportBuildContext,
  ): IRuntimeShutdownReport;

  /**
   * Sanitizes a failure diagnostic by redacting secrets from message fields.
   */
  protected sanitizeFailure(
    failure: IRuntimeFailureDiagnostic,
  ): IRuntimeFailureDiagnostic {
    return {
      ...failure,
      message: this._secretsRedactor.redact(failure.message),
      remediationHint: this._secretsRedactor.redact(failure.remediationHint),
    };
  }

  /**
   * Sanitizes a shutdown issue by redacting secrets from message fields.
   */
  protected sanitizeIssue(
    issue: IModuleLifecycleShutdownIssue,
  ): IModuleLifecycleShutdownIssue {
    return {
      ...issue,
      message: this._secretsRedactor.redact(issue.message),
      remediationHint: this._secretsRedactor.redact(issue.remediationHint),
    };
  }

  /**
   * Determines the startup status based on loaded, skipped, and failed modules.
   */
  protected determineStartupStatus(
    loadedModules: readonly IRuntimeLoadedModuleDiagnostic[],
    skippedModules: readonly IRuntimeSkippedModuleDiagnostic[],
    failedModules: readonly IRuntimeFailureDiagnostic[],
  ): RuntimeStartupStatus {
    const hasFatalFailure =
      failedModules.length > 0 && loadedModules.length === 0;

    if (hasFatalFailure) {
      return RuntimeStartupStatus.Failed;
    }

    const degraded = skippedModules.length > 0;

    if (degraded) {
      return RuntimeStartupStatus.Degraded;
    }

    return RuntimeStartupStatus.Success;
  }
}
