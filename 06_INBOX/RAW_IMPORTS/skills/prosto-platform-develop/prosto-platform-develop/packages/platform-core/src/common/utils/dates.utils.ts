/**
 * @internal
 * Returns the current timestamp as an ISO 8601 string.
 */
export function dateNowIso(): string {
  return new Date().toISOString();
}
