const LOGIN_PATH = '/auth/login';

/** @internal */
export function resolveInAppDestination(value: unknown): string {
  if (
    typeof value !== 'string' ||
    !value.startsWith('/') ||
    value.startsWith('//') ||
    value.includes('\\')
  ) {
    return '/';
  }

  return value;
}

/**
 * @internal
 * Resolves the BFF origin and rejects cross-origin browser configuration.
 */
export function resolveAdminBffBaseUrl(
  configuredUrl: string | undefined,
  browserOrigin: string,
): string {
  const expectedOrigin = new URL(browserOrigin).origin;
  const candidate = configuredUrl?.trim();

  if (!candidate) {
    return expectedOrigin;
  }

  let parsedUrl: URL;

  try {
    parsedUrl = new URL(candidate);
  } catch {
    throw new Error(
      'Configured admin BFF URL must be an absolute same-origin URL.',
    );
  }

  if (
    parsedUrl.origin !== expectedOrigin ||
    parsedUrl.username !== '' ||
    parsedUrl.password !== ''
  ) {
    throw new Error(
      'Configured admin BFF URL must be an absolute same-origin URL.',
    );
  }

  return parsedUrl.origin;
}

/**
 * @internal
 * Detects the broker's fixed callback-failure query state.
 */
export function isAuthenticationFailure(search: string): boolean {
  return new URLSearchParams(search).get('auth') === 'failed';
}

/**
 * @internal
 * Always starts a fresh login navigation after a logout attempt.
 */
export async function logoutAndNavigate(
  logout: () => Promise<void>,
  navigate: (path: string) => void,
  destination = LOGIN_PATH,
): Promise<void> {
  try {
    await logout();
  } finally {
    navigate(destination);
  }
}
