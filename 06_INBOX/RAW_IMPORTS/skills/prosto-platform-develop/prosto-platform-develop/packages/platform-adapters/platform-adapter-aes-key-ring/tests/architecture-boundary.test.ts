import { readFile, readdir } from 'node:fs/promises';
import { describe, expect, it } from 'vitest';

const SOURCE_DIRECTORY = new URL('../src/', import.meta.url);
const DECLARATION_ENTRY_POINT = new URL('../dist/index.d.ts', import.meta.url);
const FORBIDDEN_PLATFORM_IMPORTS = [
  '@prosto/platform-core',
  '@prosto/platform-adapter-',
  '@prosto/platform-module-',
  '@prosto/platform-admin-',
] as const;
const FORBIDDEN_RUNTIME_IMPORTS = [
  'fastify',
  'jose',
  'openid-client',
  'typeorm',
] as const;
const FORBIDDEN_PUBLIC_TYPE_IMPORTS = [
  'fastify',
  'typeorm',
  'node:crypto',
  'jose',
  'openid-client',
] as const;

describe('AES key-ring adapter architecture boundary', (): void => {
  it('imports only the SDK and node crypto across every source file', async (): Promise<void> => {
    // Arrange
    const sourceFiles = (
      await readdir(SOURCE_DIRECTORY, { recursive: true })
    ).filter((entry) => entry.endsWith('.ts'));

    // Act
    const sources = await Promise.all(
      sourceFiles.map((file) =>
        readFile(new URL(file, SOURCE_DIRECTORY), 'utf-8'),
      ),
    );

    // Assert
    for (const source of sources) {
      for (const forbiddenImport of FORBIDDEN_PLATFORM_IMPORTS) {
        expect(source).not.toContain(forbiddenImport);
      }
      for (const forbiddenImport of FORBIDDEN_RUNTIME_IMPORTS) {
        expect(source).not.toContain(forbiddenImport);
      }
    }
  });

  it('does not leak infrastructure types from generated declarations', async (): Promise<void> => {
    const declaration = await readFile(DECLARATION_ENTRY_POINT, 'utf8');

    for (const forbiddenImport of FORBIDDEN_PUBLIC_TYPE_IMPORTS) {
      expect(declaration).not.toContain(`from '${forbiddenImport}'`);
    }
  });
});
