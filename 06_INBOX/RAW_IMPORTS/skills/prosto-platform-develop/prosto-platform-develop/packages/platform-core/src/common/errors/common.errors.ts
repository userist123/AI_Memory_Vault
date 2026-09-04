/**
 * Represents an error thrown when an assertion fails.
 */
export class AssertionError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'AssertionError';
  }
}

/**
 * Represents an error thrown when an operation times out.
 */
export class OperationTimeoutError extends Error {
  constructor() {
    super('Operation timed out');
    this.name = 'OperationTimeoutError';
  }
}
