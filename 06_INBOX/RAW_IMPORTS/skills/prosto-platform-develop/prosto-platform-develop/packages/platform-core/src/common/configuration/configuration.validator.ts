import type { ZodError, ZodType } from 'zod';
import { type ISecretsRedactor, SecretsRedactor } from '@/security/index.js';

/**
 * @alpha
 * Error thrown when configuration validation fails.
 */
export class ConfigurationValidationError extends Error {
  constructor(
    message: string,
    public readonly zodError: ZodError,
  ) {
    super(message);
    this.name = 'ConfigValidationError';
  }
}

/**
 * @alpha
 * Class for validating configuration objects using Zod schemas.
 */
export class ConfigurationValidator {
  constructor(
    private readonly _secretsRedactor: ISecretsRedactor = new SecretsRedactor(),
  ) {}

  validate<T>(configuration: unknown, schema: ZodType<T>): T {
    const result = schema.safeParse(configuration);

    if (!result.success) {
      const errors = this._formatZodErrors(result.error);
      const message = this._secretsRedactor.redact(
        `Configuration validation failed:\n${errors.join('\n')}`,
      );

      throw new ConfigurationValidationError(message, result.error);
    }

    return result.data;
  }

  private _formatZodErrors(error: ZodError): string[] {
    return error.issues.map((issue) => {
      const path = issue.path.length > 0 ? issue.path.join('.') : 'root';

      return `  - "${path}": ${issue.message}`;
    });
  }
}
