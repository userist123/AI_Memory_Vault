// SPDX-License-Identifier: MIT
// Fixture-based tests for architecture import analysis helpers.

import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { ALLOWED_INTERNAL_DEPENDENCIES } from '../architecture/dependency-matrix.mjs';
import {
  extractStaticImports,
  hasDynamicImportOrRequire,
  mapInternalImport,
  validateImportEdge,
} from '../architecture/import-analysis.mjs';

const FIXTURES_DIR = path.join(
  path.dirname(fileURLToPath(import.meta.url)),
  'fixtures',
);

async function fixture(name) {
  return await readFile(path.join(FIXTURES_DIR, name), 'utf8');
}

describe('extractStaticImports', () => {
  it('extracts SDK import from BFF fixture', async () => {
    const source = await fixture('allowed-bff-to-sdk.ts');
    const imports = extractStaticImports(source);

    assert.deepEqual(imports, [
      '@prosto/platform-sdk',
      '@prosto/platform-adapter-admin-bff',
    ]);
  });

  it('extracts deep import specifier', async () => {
    const source = await fixture('deep-import.ts');
    const imports = extractStaticImports(source);

    assert.deepEqual(imports, ['@prosto/platform-sdk/transport/http']);
  });

  it('extracts cross-package relative import', async () => {
    const source = await fixture('cross-package-relative.ts');
    const imports = extractStaticImports(source);

    assert.deepEqual(imports, ['../../platform-sdk/src/index.js']);
  });
});

describe('hasDynamicImportOrRequire', () => {
  it('detects dynamic import in fixture', async () => {
    const source = await fixture('dynamic-import.ts');

    assert.equal(hasDynamicImportOrRequire(source), true);
  });

  it('does not flag static imports', async () => {
    const source = await fixture('allowed-bff-to-sdk.ts');

    assert.equal(hasDynamicImportOrRequire(source), false);
  });
});

describe('mapInternalImport', () => {
  const known = new Set([
    '@prosto/platform-sdk',
    '@prosto/platform-adapter-admin-bff',
    '@prosto/platform-adapter-http',
    '@prosto/platform-adapter-auth-oidc',
    '@prosto/platform-adapter-aes-key-ring',
    '@prosto/platform-adapter-auth-oidc-session',
    '@prosto/platform-adapter-typeorm',
    '@prosto/platform-core',
    '@prosto/platform-module-auth-oidc-session',
  ]);

  it('maps root SDK import as non-deep', () => {
    const result = mapInternalImport('@prosto/platform-sdk', known);

    assert.deepEqual(result, {
      packageName: '@prosto/platform-sdk',
      isDeep: false,
    });
  });

  it('maps deep SDK import as deep', () => {
    const result = mapInternalImport(
      '@prosto/platform-sdk/transport/http',
      known,
    );

    assert.deepEqual(result, {
      packageName: '@prosto/platform-sdk',
      isDeep: true,
    });
  });

  it('returns null for external packages', () => {
    const result = mapInternalImport('fastify', known);

    assert.equal(result, null);
  });

  it('returns null for unknown scoped packages', () => {
    const result = mapInternalImport('@prosto/unknown-package', known);

    assert.equal(result, null);
  });
});

describe('validateImportEdge', () => {
  const matrix = ALLOWED_INTERNAL_DEPENDENCIES;

  it('accepts allowed BFF-to-SDK edge', () => {
    const error = validateImportEdge(
      {
        fromPackage: '@prosto/platform-adapter-admin-bff',
        toPackage: '@prosto/platform-sdk',
        specifier: '@prosto/platform-sdk',
        filePath: 'fixtures/allowed-bff-to-sdk.ts',
        line: 1,
        isDeep: false,
        isRelativeCrossPackage: false,
      },
      matrix,
    );

    assert.equal(error, null);
  });

  it('rejects forbidden HTTP-to-BFF edge', () => {
    const error = validateImportEdge(
      {
        fromPackage: '@prosto/platform-adapter-http',
        toPackage: '@prosto/platform-adapter-admin-bff',
        specifier: '@prosto/platform-adapter-admin-bff',
        filePath: 'fixtures/forbidden-http-to-bff.ts',
        line: 1,
        isDeep: false,
        isRelativeCrossPackage: false,
      },
      matrix,
    );

    assert.ok(error);
    assert.match(error, /Architecture boundary violation/);
    assert.match(error, /@prosto\/platform-adapter-http/);
    assert.match(error, /@prosto\/platform-adapter-admin-bff/);
  });

  it('rejects deep import', () => {
    const error = validateImportEdge(
      {
        fromPackage: '@prosto/platform-adapter-admin-bff',
        toPackage: '@prosto/platform-sdk',
        specifier: '@prosto/platform-sdk/transport/http',
        filePath: 'fixtures/deep-import.ts',
        line: 1,
        isDeep: true,
        isRelativeCrossPackage: false,
      },
      matrix,
    );

    assert.ok(error);
    assert.match(error, /Deep import/);
  });

  it('rejects cross-package relative import', () => {
    const error = validateImportEdge(
      {
        fromPackage: '@prosto/platform-adapter-admin-bff',
        toPackage: '@prosto/platform-sdk',
        specifier: '../../platform-sdk/src/index.js',
        filePath: 'fixtures/cross-package-relative.ts',
        line: 1,
        isDeep: false,
        isRelativeCrossPackage: true,
      },
      matrix,
    );

    assert.ok(error);
    assert.match(error, /Cross-package relative import/);
  });

  it('accepts the bearer adapter to SDK edge', async () => {
    const source = await fixture('allowed-auth-to-sdk.ts');
    const [specifier] = extractStaticImports(source);
    const mapped = mapInternalImport(
      specifier,
      new Set(['@prosto/platform-sdk', '@prosto/platform-adapter-auth-oidc']),
    );

    assert.deepEqual(mapped, {
      packageName: '@prosto/platform-sdk',
      isDeep: false,
    });
    assert.equal(
      validateImportEdge(
        {
          fromPackage: '@prosto/platform-adapter-auth-oidc',
          toPackage: mapped.packageName,
          specifier,
          filePath: 'fixtures/allowed-auth-to-sdk.ts',
          line: 1,
          isDeep: mapped.isDeep,
          isRelativeCrossPackage: false,
        },
        matrix,
      ),
      null,
    );
  });

  it('accepts the session module to session adapter edge', async () => {
    const source = await fixture('allowed-auth-session-module-to-session.ts');
    const [specifier] = extractStaticImports(source);
    const mapped = mapInternalImport(
      specifier,
      new Set([
        '@prosto/platform-adapter-auth-oidc-session',
        '@prosto/platform-module-auth-oidc-session',
      ]),
    );

    assert.deepEqual(mapped, {
      packageName: '@prosto/platform-adapter-auth-oidc-session',
      isDeep: false,
    });
    assert.equal(
      validateImportEdge(
        {
          fromPackage: '@prosto/platform-module-auth-oidc-session',
          toPackage: mapped.packageName,
          specifier,
          filePath: 'fixtures/allowed-auth-session-module-to-session.ts',
          line: 1,
          isDeep: mapped.isDeep,
          isRelativeCrossPackage: false,
        },
        matrix,
      ),
      null,
    );
  });

  it('rejects bearer adapter to session adapter imports', async () => {
    const source = await fixture('forbidden-bearer-to-session.ts');
    const [specifier] = extractStaticImports(source);
    const mapped = mapInternalImport(
      specifier,
      new Set([
        '@prosto/platform-adapter-auth-oidc',
        '@prosto/platform-adapter-auth-oidc-session',
      ]),
    );

    assert.ok(mapped);
    const error = validateImportEdge(
      {
        fromPackage: '@prosto/platform-adapter-auth-oidc',
        toPackage: mapped.packageName,
        specifier,
        filePath: 'fixtures/forbidden-bearer-to-session.ts',
        line: 1,
        isDeep: mapped.isDeep,
        isRelativeCrossPackage: false,
      },
      matrix,
    );

    assert.ok(error);
    assert.match(error, /Architecture boundary violation/);
  });
});

describe('auth package dependency regression', () => {
  const expectedDependencies = new Map([
    ['@prosto/platform-adapter-auth-oidc', ['@prosto/platform-sdk']],
    ['@prosto/platform-adapter-aes-key-ring', ['@prosto/platform-sdk']],
    ['@prosto/platform-adapter-auth-oidc-session', ['@prosto/platform-sdk']],
    [
      '@prosto/platform-module-auth-oidc-session',
      [
        '@prosto/platform-sdk',
        '@prosto/platform-adapter-auth-oidc-session',
        '@prosto/platform-adapter-typeorm',
      ],
    ],
    [
      '@prosto/platform-module-auth-local-session',
      [
        '@prosto/platform-sdk',
        '@prosto/platform-adapter-auth-local',
        '@prosto/platform-adapter-typeorm',
      ],
    ],
  ]);

  it('permits only the documented auth adapter and module edges', () => {
    for (const [packageName, expected] of expectedDependencies) {
      assert.deepEqual(
        ALLOWED_INTERNAL_DEPENDENCIES.get(packageName),
        expected,
        `${packageName} has an unexpected internal dependency allowance.`,
      );
    }
  });

  it('allows the complete auth composition only in the example host', async () => {
    const manifest = JSON.parse(
      await readFile(
        path.resolve('examples/admin-bff-http-host/package.json'),
        'utf8',
      ),
    );
    const dependencies = new Set(Object.keys(manifest.dependencies ?? {}));
    const required = [
      '@prosto/platform-adapter-admin-bff',
      '@prosto/platform-adapter-aes-key-ring',
      '@prosto/platform-adapter-auth-oidc',
      '@prosto/platform-adapter-http',
      '@prosto/platform-core',
      '@prosto/platform-module-auth-oidc-session',
    ];

    for (const packageName of required) {
      assert.equal(
        dependencies.has(packageName),
        true,
        `The admin BFF composition host must depend on ${packageName}.`,
      );
    }
  });
});

describe('BFF dependency regression', () => {
  it('allows exactly SDK and admin contracts for BFF', () => {
    const allowed = new Set(
      ALLOWED_INTERNAL_DEPENDENCIES.get('@prosto/platform-adapter-admin-bff') ??
        [],
    );
    const expected = new Set([
      '@prosto/platform-sdk',
      '@prosto/platform-admin-contracts',
    ]);

    assert.deepEqual(allowed, expected);
  });

  it('does not allow BFF to depend on core, HTTP, TypeORM or shell', () => {
    const allowed = new Set(
      ALLOWED_INTERNAL_DEPENDENCIES.get('@prosto/platform-adapter-admin-bff') ??
        [],
    );
    const forbidden = [
      '@prosto/platform-core',
      '@prosto/platform-adapter-http',
      '@prosto/platform-adapter-typeorm',
      '@prosto/platform-admin-shell',
    ];

    for (const dep of forbidden) {
      assert.equal(allowed.has(dep), false, `BFF must not depend on ${dep}`);
    }
  });
});
