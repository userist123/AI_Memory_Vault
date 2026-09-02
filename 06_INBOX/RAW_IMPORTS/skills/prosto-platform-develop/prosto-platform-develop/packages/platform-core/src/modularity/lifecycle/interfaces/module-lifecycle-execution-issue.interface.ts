import type { PlatformModuleLifecycleStageType } from '@prosto/platform-sdk';
import type { RuntimeErrorCodes, RuntimeStage } from '@/common/index.js';

/**
 * @alpha
 * Type representing the startup stages of a module lifecycle.
 */
export type ModuleStartupStagesType = Exclude<
  PlatformModuleLifecycleStageType,
  'stop'
>;

/**
 * @alpha
 * Interface representing diagnostic information for a module lifecycle execution issue.
 */
export interface IModuleLifecycleExecutionIssue {
  readonly moduleId: string;
  readonly phase: `${RuntimeStage.Lifecycle}`;
  readonly lifecycleStage: ModuleStartupStagesType;
  readonly errorCode: `${RuntimeErrorCodes}`;
  readonly message: string;
  readonly remediationHint: string;
}
