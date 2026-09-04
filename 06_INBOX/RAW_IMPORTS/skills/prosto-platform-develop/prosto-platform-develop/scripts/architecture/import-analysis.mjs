// SPDX-License-Identifier: MIT
// Static import analysis helpers for architecture boundary enforcement.

import { readdir } from 'node:fs/promises';
import path from 'node:path';

const IMPORT_EXPORT_RE =
  /^(?:import\s+(?:type\s+)?|export\s+(?:type\s+)?)?(?:[\s\S]*?\s+from\s+)?['"]([^'"]+)['"];?\s*$/gmu;

const DYNAMIC_IMPORT_RE = /import\s*\(/gu;
const REQUIRE_RE = /require\s*\(/gu;

// TypeScript inline type import: import('module').Type — type-only, not runtime
const TYPE_IMPORT_RE = /import\s*\(\s*['"][^'"]+['"]\s*\)\s*\./g;

// Single-line comment: //
const SINGLE_LINE_COMMENT_RE = /\/\/.*$/gm;

// Block comment: /* ... */
const BLOCK_COMMENT_RE = /\/\*[\s\S]*?\*\//g;

const EXCLUDED_DIRS = new Set([
  'dist',
  'node_modules',
  'tests',
  'test',
  '__tests__',
  'coverage',
  'build',
  'out',
  '.turbo',
  'generated',
]);

const EXCLUDED_FILE_PARTS = [
  '.d.ts',
  '.spec.ts',
  '.test.ts',
  'vitest.config',
  'vite.config',
  'eslint.config',
  'prettier.config',
];

/**
 * @internal
 * Recursively collects all non-excluded .ts source files under a package src directory.
 *
 * @param {string} srcDir
 * @returns {Promise<string[]>}
 */
export async function collectSourceFiles(srcDir) {
  const files = [];

  await walk(srcDir, files);

  return files;
}

async function walk(dir, files) {
  const entries = await readdir(dir, { withFileTypes: true }).catch(() => []);

  if (!Array.isArray(entries)) {
    return;
  }

  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name);

    if (entry.isDirectory()) {
      if (EXCLUDED_DIRS.has(entry.name)) {
        continue;
      }

      await walk(fullPath, files);
    } else if (entry.isFile() && entry.name.endsWith('.ts')) {
      if (EXCLUDED_FILE_PARTS.some((part) => entry.name.includes(part))) {
        continue;
      }

      files.push(fullPath);
    }
  }
}

/**
 * @internal
 * Extracts static ESM imports and re-exports from source code.
 *
 * @param {string} sourceCode
 * @returns {string[]}
 */
export function extractStaticImports(sourceCode) {
  const imports = [];
  let match;

  IMPORT_EXPORT_RE.lastIndex = 0;

  while ((match = IMPORT_EXPORT_RE.exec(sourceCode)) !== null) {
    const specifier = match[1];

    if (typeof specifier === 'string' && specifier.length > 0) {
      imports.push(specifier);
    }
  }

  return imports;
}

/**
 * @internal
 * Returns true if the source contains dynamic import() or require() calls.
 *
 * @param {string} sourceCode
 * @returns {boolean}
 */
export function hasDynamicImportOrRequire(sourceCode) {
  // Strip comments first to avoid false positives from JSDoc / inline comments
  const stripped = sourceCode
    .replace(SINGLE_LINE_COMMENT_RE, '')
    .replace(BLOCK_COMMENT_RE, '');

  // Strip TypeScript inline type imports: import('module').Type
  // These are compile-time only, not runtime dynamic imports.
  const withoutTypeImports = stripped.replace(TYPE_IMPORT_RE, '');

  DYNAMIC_IMPORT_RE.lastIndex = 0;
  REQUIRE_RE.lastIndex = 0;
  return (
    DYNAMIC_IMPORT_RE.test(withoutTypeImports) ||
    REQUIRE_RE.test(withoutTypeImports)
  );
}

/**
 * @internal
 * Resolves a package name from a workspace-relative path.
 *
 * @param {string} filePath
 * @param {string} packagesRoot
 * @returns {string | null}
 */
export function resolvePackageName(filePath, packagesRoot) {
  const relative = path.relative(packagesRoot, filePath);
  const firstSegment = relative.split(path.sep)[0];

  if (!firstSegment) {
    return null;
  }

  const manifestPath = path.join(packagesRoot, firstSegment, 'package.json');

  return manifestPath;
}

/**
 * @internal
 * Maps an import specifier to a package name if it is an internal @prosto/* package.
 *
 * @param {string} specifier
 * @param {Set<string>} knownPackageNames
 * @returns {{ packageName: string; isDeep: boolean } | null}
 */
export function mapInternalImport(specifier, knownPackageNames) {
  if (!specifier.startsWith('@prosto/')) {
    return null;
  }

  const withoutPrefix = specifier.slice('@prosto/'.length);
  const firstSlash = withoutPrefix.indexOf('/');

  if (firstSlash === -1) {
    const packageName = `@prosto/${withoutPrefix}`;

    return knownPackageNames.has(packageName)
      ? { packageName, isDeep: false }
      : null;
  }

  const packageName = `@prosto/${withoutPrefix.slice(0, firstSlash)}`;

  if (!knownPackageNames.has(packageName)) {
    return null;
  }

  return { packageName, isDeep: true };
}

/**
 * @internal
 * Validates a single import edge against the allowed dependency matrix.
 *
 * @param {{
 *   fromPackage: string;
 *   toPackage: string;
 *   specifier: string;
 *   filePath: string;
 *   line: number;
 *   isDeep: boolean;
 *   isRelativeCrossPackage: boolean;
 * }} edge
 * @param {Map<string, string[]>} allowedMatrix
 * @returns {string | null}
 */
export function validateImportEdge(edge, allowedMatrix) {
  if (edge.isDeep) {
    return `Deep import from ${edge.fromPackage} to ${edge.specifier} is not allowed. Public exports use only "." (file: ${edge.filePath}:${edge.line}).`;
  }

  if (edge.isRelativeCrossPackage) {
    return `Cross-package relative import from ${edge.fromPackage} to ${edge.specifier} is not allowed (file: ${edge.filePath}:${edge.line}).`;
  }

  const allowed = new Set(allowedMatrix.get(edge.fromPackage) ?? []);

  if (!allowed.has(edge.toPackage)) {
    return `Architecture boundary violation: ${edge.fromPackage} cannot import ${edge.toPackage} via ${edge.specifier} (file: ${edge.filePath}:${edge.line}).`;
  }

  return null;
}
