import type { IModuleLifecycleShutdownIssue } from '@/modularity/index.js';

/**
 * @alpha
 * Interface representing diagnostic information for the runtime shutdown process.
 */
export interface IRuntimeShutdownReport {
  readonly type: 'shutdown';
  readonly correlationId: string;
  readonly startedAt: string;
  readonly completedAt: string;
  readonly stopOrder: readonly string[];
  readonly issues: readonly IModuleLifecycleShutdownIssue[];
}
