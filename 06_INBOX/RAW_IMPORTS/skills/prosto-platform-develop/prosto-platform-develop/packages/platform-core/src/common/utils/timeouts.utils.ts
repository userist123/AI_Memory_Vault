import { OperationTimeoutError } from '../errors/index.js';

export type CreateTimeoutErrorFunctionType = () => Error;

/**
 * Execute a promise with a timeout.
 * @param promise - The promise to execute
 * @param timeoutMs - Timeout in milliseconds
 * @param createErrorFn - Function to create the error
 * @throws {Error} If the operation times out
 */
export async function executeWithTimeout(
  promise: Promise<void>,
  timeoutMs: number,
  createErrorFn: CreateTimeoutErrorFunctionType = () =>
    new OperationTimeoutError(),
): Promise<void> {
  let timer: ReturnType<typeof setTimeout> | undefined;

  try {
    await Promise.race([
      promise,
      new Promise<void>((_, reject) => {
        timer = setTimeout(() => reject(createErrorFn()), timeoutMs);
      }),
    ]);
  } finally {
    if (timer) {
      clearTimeout(timer);
    }
  }
}
