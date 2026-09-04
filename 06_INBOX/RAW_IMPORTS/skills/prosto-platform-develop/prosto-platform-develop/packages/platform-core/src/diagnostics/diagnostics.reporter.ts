import type {
  IDiagnosticsReporter,
  IReportBuilder,
  IRuntimeShutdownReport,
  IRuntimeStartupReport,
  IShutdownReportInput,
  IStartupReportInput,
} from './interfaces/index.js';
import { DiagnosticReportBuilder } from './builders/index.js';

/**
 * @alpha
 * Default implementation of the diagnostics reporter.
 * Delegates report construction to DiagnosticReportBuilder.
 */
export class DiagnosticsReporter implements IDiagnosticsReporter {
  constructor(
    private readonly _reportBuilder: IReportBuilder = new DiagnosticReportBuilder(),
  ) {}

  /**
   * Creates a startup diagnostic report from the provided input.
   */
  createStartupReport(input: IStartupReportInput): IRuntimeStartupReport {
    return this._reportBuilder.buildStartupReport(input);
  }

  /**
   * Creates a shutdown diagnostic report from the provided input.
   */
  createShutdownReport(input: IShutdownReportInput): IRuntimeShutdownReport {
    return this._reportBuilder.buildShutdownReport(input);
  }
}
