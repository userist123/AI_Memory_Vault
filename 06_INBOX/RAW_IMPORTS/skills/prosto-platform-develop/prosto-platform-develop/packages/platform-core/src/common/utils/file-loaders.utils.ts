import { readFileSync, existsSync } from 'node:fs';

/**
 * Load JSON file from the file system.
 */
export function loadJsonFileSync<T extends object = Record<string, unknown>>(
  filePath: string,
  optional = false,
): T {
  if (!existsSync(filePath)) {
    if (optional) return {} as T;

    throw new Error(`File not found: ${filePath}`);
  }

  const content = readFileSync(filePath, 'utf-8');

  if (!content.trim()) return {} as T;

  try {
    return JSON.parse(content);
  } catch (error) {
    throw new Error(
      `Failed to parse file "${filePath}": ${
        error instanceof Error ? error.message : String(error)
      }`,
      { cause: error },
    );
  }
}
