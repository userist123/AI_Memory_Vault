import { satisfies, valid, validRange } from 'semver';

/**
 * @alpha
 * Checks whether a value is a valid semantic version string.
 */
export function isSemverVersion(value: string): boolean {
  return valid(value) !== null;
}

/**
 * @alpha
 * Checks whether a value is a valid semantic version range expression.
 */
export function isSemverRange(value: string): boolean {
  return validRange(value) !== null;
}

/**
 * @alpha
 * Checks whether a concrete version satisfies a semantic version range.
 */
export function isSemverSatisfied(version: string, range: string): boolean {
  if (!isSemverVersion(version) || !isSemverRange(range)) {
    return false;
  }

  return satisfies(version, range, { includePrerelease: true });
}
