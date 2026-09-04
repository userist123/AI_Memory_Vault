// SPDX-License-Identifier: MIT
// Architecture gate: workspace topology + static import boundary analysis.

import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import {
  ALLOWED_INTERNAL_DEPENDENCIES,
  DYNAMIC_IMPORT_ALLOWLIST,
  REQUIRED_PACKAGE_DIRS,
  REQUIRED_WORKSPACE_GLOBS,
} from './architecture/dependency-matrix.mjs';
import {
  collectSourceFiles,
  extractStaticImports,
  hasDynamicImportOrRequire,
  mapInternalImport,
  validateImportEdge,
} from './architecture/import-analysis.mjs';

const ROOT_PACKAGE_JSON = path.resolve('package.json');
const PACKAGES_ROOT = path.resolve('packages');

const rootManifest = JSON.parse(await readFile(ROOT_PACKAGE_JSON, 'utf8'));
const workspaces = Array.isArray(rootManifest.workspaces)
  ? rootManifest.workspaces
  : [];

for (const requiredWorkspaceGlob of REQUIRED_WORKSPACE_GLOBS) {
  if (!workspaces.includes(requiredWorkspaceGlob)) {
    throw new Error(
      `Expected root workspaces to include "${requiredWorkspaceGlob}".`,
    );
  }
}

for (const packageDir of REQUIRED_PACKAGE_DIRS) {
  const packageJsonPath = path.resolve(
    PACKAGES_ROOT,
    packageDir,
    'package.json',
  );

  try {
    await readFile(packageJsonPath, 'utf8');
  } catch {
    throw new Error(
      `Missing required platform package manifest: ${packageJsonPath}`,
    );
  }
}

async function collectPackageDirectories(
  directoryPath,
  relativeDirectory = '',
) {
  const entries = await readdir(directoryPath, { withFileTypes: true });
  const packageDirectories = [];

  for (const entry of entries) {
    if (!entry.isDirectory() || entry.name === 'node_modules') {
      continue;
    }

    const packageDirectory = path.join(directoryPath, entry.name);
    const relativePackageDirectory = path.join(relativeDirectory, entry.name);
    const manifestPath = path.join(packageDirectory, 'package.json');

    try {
      await readFile(manifestPath, 'utf8');
      packageDirectories.push(relativePackageDirectory);
      continue;
    } catch {
      packageDirectories.push(
        ...(await collectPackageDirectories(
          packageDirectory,
          relativePackageDirectory,
        )),
      );
    }
  }

  return packageDirectories;
}

// Build a set of known workspace package names from package.json manifests.
const knownPackageNames = new Set();
const packageDirToName = new Map();
const packageDirectories = await collectPackageDirectories(PACKAGES_ROOT);

for (const packageDirectory of packageDirectories) {
  const manifestPath = path.resolve(
    PACKAGES_ROOT,
    packageDirectory,
    'package.json',
  );

  try {
    const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
    const name = String(manifest.name ?? '');

    if (name.startsWith('@prosto/')) {
      knownPackageNames.add(name);
      packageDirToName.set(packageDirectory, name);
    }
  } catch {
    // Manifest missing or unreadable: skip non-package directories.
  }
}

const packageRoots = [...packageDirToName.keys()]
  .map((packageDirectory) => [
    packageDirectory,
    path.resolve(PACKAGES_ROOT, packageDirectory),
  ])
  .sort(([, leftPath], [, rightPath]) => rightPath.length - leftPath.length);

function findContainingPackageDirectory(filePath) {
  for (const [packageDirectory, packageRoot] of packageRoots) {
    if (
      filePath === packageRoot ||
      filePath.startsWith(`${packageRoot}${path.sep}`)
    ) {
      return packageDirectory;
    }
  }

  return undefined;
}

const violations = [];

for (const [dirName, packageName] of packageDirToName) {
  if (!packageName.startsWith('@prosto/')) {
    continue;
  }

  const srcDir = path.resolve(PACKAGES_ROOT, dirName, 'src');
  let files;

  try {
    files = await collectSourceFiles(srcDir);
  } catch (error) {
    if (error.code === 'ENOENT') {
      // No src directory for this package; nothing to analyse.
      continue;
    }

    throw error;
  }

  for (const filePath of files) {
    const sourceCode = await readFile(filePath, 'utf8');
    const specifiers = extractStaticImports(sourceCode);

    if (hasDynamicImportOrRequire(sourceCode)) {
      const relativePath = path
        .relative(PACKAGES_ROOT, filePath)
        .replaceAll('\\', '/');

      if (!DYNAMIC_IMPORT_ALLOWLIST.has(relativePath)) {
        violations.push(
          `Dynamic import()/require() is not allowed in package source (file: ${filePath}).`,
        );
      }
    }

    for (const specifier of specifiers) {
      if (specifier.startsWith('.')) {
        // Relative imports are allowed inside the same package. Cross-package relative imports are forbidden.
        const resolved = path.resolve(path.dirname(filePath), specifier);
        const fromPackageDir = findContainingPackageDirectory(filePath);
        const toPackageDir = findContainingPackageDirectory(resolved);

        if (toPackageDir !== undefined && toPackageDir !== fromPackageDir) {
          violations.push(
            `Cross-package relative import from ${packageName} to ${specifier} is not allowed (file: ${filePath}).`,
          );
        }

        continue;
      }

      const internal = mapInternalImport(specifier, knownPackageNames);

      if (!internal) {
        continue;
      }

      const error = validateImportEdge(
        {
          fromPackage: packageName,
          toPackage: internal.packageName,
          specifier,
          filePath,
          line: 1,
          isDeep: internal.isDeep,
          isRelativeCrossPackage: false,
        },
        ALLOWED_INTERNAL_DEPENDENCIES,
      );

      if (error) {
        violations.push(error);
      }
    }
  }
}

if (violations.length > 0) {
  for (const violation of violations) {
    console.error(`Architecture violation: ${violation}`);
  }
  throw new Error(
    `lint:architecture failed with ${violations.length} boundary violation(s).`,
  );
}

console.log(
  'lint:architecture passed: workspace topology and import boundaries are valid.',
);
