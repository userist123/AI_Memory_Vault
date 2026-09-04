export function normalizeTokenName(name: string): string {
  const normalized = name.trim();

  if (!normalized.length) {
    throw new TypeError('Token name must be a non-empty string.');
  }

  return normalized;
}
