import { readFile, readdir } from 'node:fs/promises';
import { describe, expect, it } from 'vitest';

const SOURCE_DIRECTORY = new URL('../src/', import.meta.url);
const FORBIDDEN_IMPORTS = [
  '@prosto/platform-core',
  '@prosto/platform-adapter-',
  '@prosto/platform-module-',
  'fastify',
  'typeorm',
  'vue',
] as const;

describe('local auth adapter architecture boundary', (): void => {
  it('does not depend on core, persistence, HTTP framework or UI runtime', async (): Promise<void> => {
    const sourceFiles = (
      await readdir(SOURCE_DIRECTORY, { recursive: true })
    ).filter((entry) => entry.endsWith('.ts'));
    const sources = await Promise.all(
      sourceFiles.map((file) =>
        readFile(new URL(file, SOURCE_DIRECTORY), 'utf8'),
      ),
    );

    for (const source of sources) {
      for (const forbidden of FORBIDDEN_IMPORTS) {
        expect(source).not.toContain(forbidden);
      }
    }
  });
});
