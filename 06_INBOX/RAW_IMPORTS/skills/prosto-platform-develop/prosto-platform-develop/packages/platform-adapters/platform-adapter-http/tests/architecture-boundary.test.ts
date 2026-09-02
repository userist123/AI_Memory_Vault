import { readFile } from 'node:fs/promises';
import { describe, expect, it } from 'vitest';

const HTTP_PACKAGE_SOURCE = new URL('../src/', import.meta.url);
const FORBIDDEN_PLATFORM_IMPORTS = [
  '@prosto/platform-core',
  '@prosto/platform-adapter-admin-bff',
  '@prosto/platform-adapter-typeorm',
  '@prosto/platform-module-',
] as const;

describe('HTTP adapter architecture boundary', (): void => {
  it('does not import core, other adapters, or feature modules', async (): Promise<void> => {
    // Arrange
    const sourceFiles = [
      'http-server.ts',
      'http-server.errors.ts',
      'http-server.interfaces.ts',
      'index.ts',
      'mapping/fastify-body.mapper.ts',
      'mapping/fastify-request.mapper.ts',
      'mapping/fastify-response.mapper.ts',
      'observability/console-http-logger.ts',
      'observability/http-logger.constants.ts',
      'observability/http-logger.interface.ts',
      'observability/index.ts',
    ];

    // Act
    const sources = await Promise.all(
      sourceFiles.map(async (file) =>
        readFile(new URL(file, HTTP_PACKAGE_SOURCE), 'utf-8'),
      ),
    );

    // Assert
    for (const source of sources) {
      for (const forbiddenImport of FORBIDDEN_PLATFORM_IMPORTS) {
        expect(source).not.toContain(forbiddenImport);
      }
    }
  });
});
