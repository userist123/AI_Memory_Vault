/**
 * @internal
 * Sleep for a given amount of time.
 */
export function sleep(timeoutMs: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, timeoutMs));
}
