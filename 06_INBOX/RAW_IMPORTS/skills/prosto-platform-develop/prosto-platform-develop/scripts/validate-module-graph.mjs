import { readFile } from 'node:fs/promises';
import path from 'node:path';

const PACKAGE_DIRS = [
  'platform-sdk',
  'platform-admin-contracts',
  'platform-core',
  'platform-contract-tests',
  'platform-cli',
  'platform-adapters/platform-adapter-http',
  'platform-adapters/platform-adapter-auth-oidc',
  'platform-adapters/platform-adapter-aes-key-ring',
  'platform-adapters/platform-adapter-auth-oidc-session',
  'platform-adapters/platform-adapter-auth-local',
  'platform-modules/platform-module-auth-oidc-session',
  'platform-modules/platform-module-auth-local-session',
  'platform-adapters/platform-adapter-typeorm',
];

const manifests = [];

for (const packageDir of PACKAGE_DIRS) {
  const manifest = JSON.parse(
    await readFile(
      path.resolve('packages', packageDir, 'package.json'),
      'utf8',
    ),
  );

  manifests.push(manifest);
}

const graph = new Map();

for (const manifest of manifests) {
  const packageName = String(manifest.name ?? '');
  const dependencies = Object.keys(manifest.dependencies ?? {}).filter((dep) =>
    dep.startsWith('@prosto/'),
  );

  graph.set(packageName, dependencies);
}

const visiting = new Set();
const visited = new Set();

function walk(node) {
  if (visiting.has(node)) {
    throw new Error(`Module graph cycle detected at ${node}.`);
  }

  if (visited.has(node)) {
    return;
  }

  visiting.add(node);

  for (const dep of graph.get(node) ?? []) {
    walk(dep);
  }

  visiting.delete(node);
  visited.add(node);
}

for (const node of graph.keys()) {
  walk(node);
}

console.log(
  'validate:module-graph passed: no workspace internal dependency cycles detected.',
);
