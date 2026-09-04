import type { BootstrapStage } from '@/bootstrap/index.js';
import type { RuntimeErrorCodes, RuntimeStage } from '@/common/index.js';

/**
 * @alpha
 * Interface representing diagnostic information for a runtime failure.
 */
export interface IRuntimeFailureDiagnostic {
  readonly moduleId: string;
  readonly phase: `${RuntimeStage}` | `${BootstrapStage}`;
  readonly errorCode: `${RuntimeErrorCodes}`;
  readonly message: string;
  readonly remediationHint: string;
}
