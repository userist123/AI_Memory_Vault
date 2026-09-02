import type {
  IPlatformModuleManifestValidator,
  IPlatformModuleManifest,
} from '@prosto/platform-sdk';
import type { IContractCheckResult } from '@/interfaces/index.js';
import { ContractFailureCodes } from '@/constants/index.js';

export const MANIFEST_CHECK_RESULT_ID = 'manifest-conformance';

/**
 * @alpha
 * Validates module manifest schema and semantic constraints.
 */
export function runManifestConformanceCheck(params: {
  manifest: IPlatformModuleManifest;
  manifestValidator: IPlatformModuleManifestValidator;
}): IContractCheckResult {
  const result = params.manifestValidator.validate(params.manifest);

  if (result.success) {
    return {
      id: MANIFEST_CHECK_RESULT_ID,
      title: 'Manifest conformance',
      severity: 'mandatory',
      passed: true,
      code: null,
      details: 'Manifest matches schema and semantic constraints.',
    };
  }

  const isSemantic = result.error.issues.some(
    (issue) =>
      issue.code.startsWith('duplicate_') || issue.code === 'self_dependency',
  );

  return {
    id: MANIFEST_CHECK_RESULT_ID,
    title: 'Manifest conformance',
    severity: 'mandatory',
    passed: false,
    code: isSemantic
      ? ContractFailureCodes.ManifestSemanticInvalid
      : ContractFailureCodes.ManifestSchemaInvalid,
    details: result.error.issues
      .map((issue) => `[${issue.code}] ${issue.path}: ${issue.message}`)
      .join('; '),
  };
}
