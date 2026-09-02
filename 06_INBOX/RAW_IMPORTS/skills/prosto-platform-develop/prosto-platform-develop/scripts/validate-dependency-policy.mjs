// SPDX-License-Identifier: MIT
// Dependency policy gate: validates declared workspace and root dependencies
// against the shared architecture boundary matrix.

import { readFile } from 'node:fs/promises';
import path from 'node:path';
import {
  ALLOWED_INTERNAL_DEPENDENCIES,
  FORBIDDEN_PACKAGE_DEPENDENCIES,
  FORBIDDEN_ROOT_DEPS,
  INTERNAL_PREFIX,
  WORKSPACE_PACKAGE_DIRS,
} from './architecture/dependency-matrix.mjs';

for (const packageDir of WORKSPACE_PACKAGE_DIRS) {
  const packageJsonPath = path.resolve('packages', packageDir, 'package.json');
  const manifest = JSON.parse(await readFile(packageJsonPath, 'utf8'));
  const packageName = String(manifest.name ?? '');
  const dependencies = {
    ...(manifest.dependencies ?? {}),
    ...(manifest.peerDependencies ?? {}),
    ...(manifest.optionalDependencies ?? {}),
  };

  const allowed = new Set(ALLOWED_INTERNAL_DEPENDENCIES.get(packageName) ?? []);
  const forbidden = new Set(
    FORBIDDEN_PACKAGE_DEPENDENCIES.get(packageName) ?? [],
  );

  for (const depName of Object.keys(dependencies)) {
    if (forbidden.has(depName)) {
      throw new Error(
        `Dependency policy violation: ${packageName} cannot depend on ${depName}.`,
      );
    }

    if (!depName.startsWith(INTERNAL_PREFIX)) {
      continue;
    }

    if (!allowed.has(depName)) {
      throw new Error(
        `Dependency policy violation: ${packageName} cannot depend on ${depName}.`,
      );
    }
  }
}

// Regression assertion: BFF is allowed only SDK and admin contracts, never core/adapters/shell.
const bffAllowed = new Set(
  ALLOWED_INTERNAL_DEPENDENCIES.get('@prosto/platform-adapter-admin-bff') ?? [],
);
const bffExpected = new Set([
  '@prosto/platform-sdk',
  '@prosto/platform-admin-contracts',
]);
const bffExtra = [...bffAllowed].filter((dep) => !bffExpected.has(dep));
const bffMissing = [...bffExpected].filter((dep) => !bffAllowed.has(dep));

if (bffExtra.length > 0 || bffMissing.length > 0) {
  throw new Error(
    `BFF dependency regression: expected exactly ${[...bffExpected].join(', ')} but got ${[...bffAllowed].join(', ')}.`,
  );
}

const rootManifest = JSON.parse(
  await readFile(path.resolve('package.json'), 'utf8'),
);

const rootDeps = Object.keys(rootManifest.dependencies ?? {});

for (const forbidden of FORBIDDEN_ROOT_DEPS) {
  if (rootDeps.includes(forbidden)) {
    throw new Error(
      `Root dependency policy violation: ${forbidden} must be owned by adapter packages.`,
    );
  }
}

console.log(
  'validate:dependency-policy passed: internal dependency rules and adapter dependency ownership are valid.',
);
