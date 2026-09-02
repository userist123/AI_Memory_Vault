// SPDX-License-Identifier: MIT
// Shared dependency matrix used by architecture validation gates.

/** @internal */
export const ALLOWED_INTERNAL_DEPENDENCIES = new Map([
  ['@prosto/platform-sdk', []],
  ['@prosto/platform-core', ['@prosto/platform-sdk']],
  ['@prosto/platform-contract-tests', ['@prosto/platform-sdk']],
  [
    '@prosto/platform-cli',
    ['@prosto/platform-sdk', '@prosto/platform-module-auth-local-session'],
  ],
  ['@prosto/platform-adapter-http', ['@prosto/platform-sdk']],
  ['@prosto/platform-adapter-auth-oidc', ['@prosto/platform-sdk']],
  ['@prosto/platform-adapter-aes-key-ring', ['@prosto/platform-sdk']],
  ['@prosto/platform-adapter-auth-oidc-session', ['@prosto/platform-sdk']],
  [
    '@prosto/platform-adapter-auth-local',
    ['@prosto/platform-sdk', '@prosto/platform-admin-contracts'],
  ],
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
  ['@prosto/platform-admin-contracts', ['@prosto/platform-sdk']],
  [
    '@prosto/platform-adapter-admin-bff',
    ['@prosto/platform-sdk', '@prosto/platform-admin-contracts'],
  ],
  ['@prosto/platform-admin-shell', ['@prosto/platform-admin-contracts']],
  ['@prosto/platform-adapter-typeorm', ['@prosto/platform-sdk']],
]);

/** @internal */
export const FORBIDDEN_PACKAGE_DEPENDENCIES = new Map([
  ['@prosto/platform-sdk', ['typeorm']],
  ['@prosto/platform-core', ['typeorm', '@prosto/platform-adapter-typeorm']],
]);

/** @internal */
export const WORKSPACE_PACKAGE_DIRS = [
  'platform-sdk',
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
  'platform-admin-contracts',
  'platform-adapters/platform-adapter-admin-bff',
  'platform-admin-shell',
];

/** @internal */
export const INTERNAL_PREFIX = '@prosto/';

/** @internal */
export const FORBIDDEN_ROOT_DEPS = [
  'cookie-parser',
  'cors',
  'helmet',
  'node-fetch',
];

/** @internal */
export const REQUIRED_WORKSPACE_GLOBS = [
  'packages/*',
  'packages/*/*',
  'packages/*/*/*',
];

/** @internal */
export const REQUIRED_PACKAGE_DIRS = [
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

/**
 * @internal
 * Files allowed to use dynamic import() for legitimate runtime module loading.
 * Paths are relative to the packages/ directory.
 */
export const DYNAMIC_IMPORT_ALLOWLIST = new Set([
  'platform-admin-shell/src/features/plugin-runtime/model/plugin-loader.ts',
  'platform-core/src/modularity/loader/utils/dynamic-module-loading.utils.ts',
]);
