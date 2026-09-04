import pkg from '../package.json' with { type: 'json' };

export * from './errors/index.js';
export * from './authentication/index.js';
export * from './events/index.js';
export * from './modularity/index.js';
export * from './persistence/index.js';
export * from './security/index.js';
export * from './services/index.js';
export * from './transport/index.js';
export * from './utils/index.js';

/**
 * @alpha
 * SDK contract surface baseline identifier.
 */
export const SDK_CONTRACT_VERSION = pkg.version;
