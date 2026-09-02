import type {
  IModuleValidationStrategy,
  IModuleValidationStrategyInput,
  ModuleValidationErrorType,
  ModuleValidationResultType,
} from '../interfaces/index.js';

/**
 * @alpha
 * Abstract base class for validation strategies implementing the Strategy pattern.
 */
export abstract class ModuleValidationBaseStrategy implements IModuleValidationStrategy {
  /**
   * Strategy name for diagnostics.
   */
  abstract readonly name: string;

  /**
   * Validate input and return result.
   */
  abstract validate(
    input: IModuleValidationStrategyInput,
  ): ModuleValidationResultType;

  /**
   * Helper to create success result.
   */
  protected success(): ModuleValidationResultType {
    return { ok: true };
  }

  /**
   * Helper to create failure result.
   */
  protected failure(
    error: ModuleValidationErrorType,
  ): ModuleValidationResultType {
    return { ok: false, error };
  }
}
