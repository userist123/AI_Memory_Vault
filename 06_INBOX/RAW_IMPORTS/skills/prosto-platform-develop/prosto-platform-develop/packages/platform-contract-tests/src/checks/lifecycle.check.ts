import type {
  IContractCheckResult,
  IModuleLifecycleContextFactory,
} from '@/interfaces/index.js';
import {
  type IPlatformModule,
  type IPlatformModuleManifest,
  MODULE_LIFECYCLE_STAGES,
} from '@prosto/platform-sdk';
import { ContractFailureCodes } from '@/constants/index.js';

export const LIFECYCLE_CHECK_RESULT_ID = 'lifecycle-conformance';

/**
 * @alpha
 * Verifies lifecycle method presence and successful execution.
 */
export async function runLifecycleConformanceCheck(params: {
  module: IPlatformModule;
  manifest: IPlatformModuleManifest;
  moduleLifecycleContextFactory: IModuleLifecycleContextFactory;
}): Promise<IContractCheckResult> {
  for (const methodName of MODULE_LIFECYCLE_STAGES) {
    const method = params.module[methodName];

    if (typeof method !== 'function') {
      return {
        id: LIFECYCLE_CHECK_RESULT_ID,
        title: 'Lifecycle method behavior',
        severity: 'mandatory',
        passed: false,
        code: ContractFailureCodes.LifecycleMethodMissing,
        details: `Lifecycle method "${methodName}" is missing.`,
      };
    }
  }

  const context = params.moduleLifecycleContextFactory.create(params.manifest);

  for (const methodName of MODULE_LIFECYCLE_STAGES) {
    try {
      await params.module[methodName](context);
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);

      return {
        id: LIFECYCLE_CHECK_RESULT_ID,
        title: 'Lifecycle method behavior',
        severity: 'mandatory',
        passed: false,
        code: ContractFailureCodes.LifecycleMethodFailed,
        details: `Lifecycle method "${methodName}" failed: ${message}`,
      };
    }
  }

  return {
    id: LIFECYCLE_CHECK_RESULT_ID,
    title: 'Lifecycle method behavior',
    severity: 'mandatory',
    passed: true,
    code: null,
    details: 'All lifecycle methods are present and executed without errors.',
  };
}
