import type {
  IContractTestRunnerApi,
  IModuleContractConformanceReport,
  IModuleContractTestInput,
} from '@/interfaces/index.js';
import { PlatformModuleManifestValidator } from '@prosto/platform-sdk';
import {
  LIFECYCLE_CHECK_RESULT_ID,
  MANIFEST_CHECK_RESULT_ID,
  runLifecycleConformanceCheck,
  runManifestConformanceCheck,
} from '@/checks/index.js';
import { DefaultModuleLifecycleContextFactory } from '@/factories/index.js';
import { buildConformanceReport } from '@/utils/index.js';

/**
 * @alpha
 * Executes full module contract conformance suite and returns machine-readable report.
 */
export async function runModuleContractConformance(
  input: IModuleContractTestInput,
): Promise<IModuleContractConformanceReport> {
  return buildConformanceReport({
    moduleId: input.manifest.id,
    moduleVersion: input.manifest.version,
    generatedAt: input.now?.() ?? new Date().toISOString(),
    checks: [
      runManifestConformanceCheck({
        manifest: input.manifest,
        manifestValidator:
          input.manifestValidator ?? new PlatformModuleManifestValidator(),
      }),
      await runLifecycleConformanceCheck({
        module: input.module,
        manifest: input.manifest,
        moduleLifecycleContextFactory:
          input.moduleLifecycleContextFactory ??
          new DefaultModuleLifecycleContextFactory(),
      }),
    ],
  });
}

/**
 * @alpha
 * Reusable test-entry helper for module repositories.
 */
export function createModuleContractTests(
  input: IModuleContractTestInput,
  runner: IContractTestRunnerApi,
): void {
  // Start all checks once; shared promise across all tests
  const checksMapPromise = runModuleContractConformance(input).then(
    ({ checks }) => new Map(checks.map((check) => [check.id, check])),
  );

  runner.describe('manifest', () => {
    runner.it('should satisfy schema and semantic constraints', async () => {
      const check = (await checksMapPromise).get(MANIFEST_CHECK_RESULT_ID);

      if (!check?.passed) {
        throw new Error(check?.details ?? 'Manifest conformance check failed.');
      }
    });
  });

  runner.describe('lifecycle', () => {
    runner.it(
      'should expose register/init/start/stop and execute successfully',
      async () => {
        const check = (await checksMapPromise).get(LIFECYCLE_CHECK_RESULT_ID);

        if (!check?.passed) {
          throw new Error(
            check?.details ?? 'Lifecycle conformance check failed.',
          );
        }
      },
    );
  });
}
