import type { PlatformStartupPolicyType } from '@prosto/platform-sdk';
import type { RuntimeStartupStatus } from '../constants/index.js';
import type { IRuntimeFailureDiagnostic } from './runtime-failure-diagnostic.interface.js';
import type { IRuntimeLoadedModuleDiagnostic } from './runtime-loaded-module-diagnostic.interface.js';
import type { IRuntimeSkippedModuleDiagnostic } from './runtime-skipped-module-diagnostic.interface.js';

/**
 * @alpha
 * Interface representing diagnostic information for the runtime startup process.
 */
export interface IRuntimeStartupReport {
  readonly type: 'startup';
  readonly status: RuntimeStartupStatus;
  readonly policyMode: PlatformStartupPolicyType;
  readonly correlationId: string;
  readonly startedAt: string;
  readonly completedAt: string;
  readonly degraded: boolean;
  readonly loadedModules: readonly IRuntimeLoadedModuleDiagnostic[];
  readonly skippedModules: readonly IRuntimeSkippedModuleDiagnostic[];
  readonly failedModules: readonly IRuntimeFailureDiagnostic[];
}
