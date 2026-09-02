import type { IRuntimeFailureDiagnostic } from './runtime-failure-diagnostic.interface.js';

/**
 * @alpha
 * Interface representing diagnostic information for a skipped module during runtime startup.
 */
export interface IRuntimeSkippedModuleDiagnostic {
  readonly moduleId: string;
  readonly reason: IRuntimeFailureDiagnostic;
}
