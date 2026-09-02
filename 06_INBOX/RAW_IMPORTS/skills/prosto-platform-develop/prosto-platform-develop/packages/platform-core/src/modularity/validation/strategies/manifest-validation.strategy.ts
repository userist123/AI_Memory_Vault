import type {
  IModuleValidationStrategyInput,
  ModuleValidationResultType,
} from '../interfaces/index.js';
import {
  type IPlatformModuleManifestValidator,
  PlatformModuleManifestValidator,
} from '@prosto/platform-sdk';
import { RuntimeErrorCodes } from '@/common/index.js';
import { ModuleValidationBaseStrategy } from './module-validation.base-strategy.js';

/**
 * @alpha
 * Manifest validation strategy.
 * Wraps IModuleManifestValidator from SDK
 * to provide pluggable manifest validation.
 */
export class ManifestValidationStrategy extends ModuleValidationBaseStrategy {
  readonly name = 'manifest' as const;

  constructor(
    private readonly _validator: IPlatformModuleManifestValidator = new PlatformModuleManifestValidator(),
  ) {
    super();
  }

  override validate(
    input: IModuleValidationStrategyInput,
  ): ModuleValidationResultType {
    const result = this._validator.validate(
      input.artifact.moduleEnvelope.manifest,
    );

    if (result.success === true) {
      return this.success();
    }

    const issues = result.error.issues
      .map(
        (issue) =>
          ` - [${issue.code}] ${issue.message} (path: "${issue.path}");`,
      )
      .join('\n');

    return this.failure({
      errorCode: RuntimeErrorCodes.ManifestInvalid,
      message: `Manifest validation failed for module ${input.artifact.moduleId}: ${
        result.error.message
      }${issues.length ? `\n\nIssues:\n${issues}` : ''}`,
      remediationHint:
        'Fix module manifest according to @prosto/platform-sdk schema and semantic rules.',
    });
  }
}
