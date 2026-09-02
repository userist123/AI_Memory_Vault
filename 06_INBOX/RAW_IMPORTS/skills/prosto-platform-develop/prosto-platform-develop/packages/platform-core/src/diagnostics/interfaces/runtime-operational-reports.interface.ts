import type { IRuntimeStartupReport } from './runtime-startup-report.interface.js';
import type { IRuntimeShutdownReport } from './runtime-shutdown-report.interface.js';

/**
 * @alpha
 * Interface representing diagnostic information for the runtime operational reports.
 */
export interface IRuntimeOperationalReports {
  readonly startup?: IRuntimeStartupReport;
  readonly shutdown?: IRuntimeShutdownReport;
}
