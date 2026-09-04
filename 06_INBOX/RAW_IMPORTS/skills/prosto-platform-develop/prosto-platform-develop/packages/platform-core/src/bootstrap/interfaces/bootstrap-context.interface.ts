import type { PlatformStartupPolicyType } from '@prosto/platform-sdk';
import type { IRuntimeFailureDiagnostic } from '@/diagnostics/index.js';
import type { IModuleEnvelope } from '@/modularity/index.js';
import type { IBootstrapStageOutcome } from './bootstrap-stage-context.interface.js';

/**
 * @alpha
 * Output context from the bootstrap coordinator after processing all stages.
 */
export interface IBootstrapContext {
  readonly policyMode: PlatformStartupPolicyType;
  readonly loadedModules: readonly IModuleEnvelope[];
  readonly skippedModuleIds: readonly string[];
  readonly failedDiagnostics: readonly IRuntimeFailureDiagnostic[];
  readonly stageOutcomes: readonly IBootstrapStageOutcome[];
}
