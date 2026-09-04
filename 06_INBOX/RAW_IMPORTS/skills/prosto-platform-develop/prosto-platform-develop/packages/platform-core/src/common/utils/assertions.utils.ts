import { AssertionError } from '../errors/index.js';

/**
 * @internal
 * Throws an AssertionError if the condition is falsy.
 */
export function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new AssertionError(message);
  }
}
