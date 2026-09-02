/**
 * @alpha
 * Shared base class for SDK-level contract failures.
 */
export class PlatformSdkError extends Error {
  constructor(
    readonly code: string,
    override readonly message: string,
    readonly details?: Readonly<Record<string, unknown>>,
  ) {
    super(message);

    this.name = 'PlatformSdkError';
    this.code = code;
    this.details = details;
  }
}
