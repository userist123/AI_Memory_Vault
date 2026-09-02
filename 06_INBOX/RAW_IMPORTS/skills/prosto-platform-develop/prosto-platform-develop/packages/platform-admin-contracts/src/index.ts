import pkg from '../package.json' with { type: 'json' };

export * from './authentication/index.js';
export * from './compatibility/index.js';
export * from './discovery/index.js';
export * from './manifests/index.js';
export * from './permissions/index.js';
export * from './utils/index.js';

/**
 * @alpha
 * Admin contract surface baseline identifier.
 */
export const ADMIN_CONTRACT_VERSION = pkg.version;
