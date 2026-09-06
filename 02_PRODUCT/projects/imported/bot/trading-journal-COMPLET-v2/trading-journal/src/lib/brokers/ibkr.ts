import type { BrokerAdapter } from './types';

/**
 * IBKR requires either:
 * - TWS Gateway running on user's machine (desktop bridge)
 * - Client Portal API Gateway (hosted)
 *
 * This is MUCH more complex than REST-based brokers.
 * Marked as "coming soon" - stub implementation.
 *
 * Planned: use Client Portal via user-hosted gateway + OAuth flow.
 * Timeline: after first 100 paying users request it.
 */
export const ibkrAdapter: BrokerAdapter = {
  info: {
    id: 'ibkr',
    displayName: 'Interactive Brokers (Coming Soon)',
    supportedAssets: ['stocks', 'forex', 'futures', 'crypto'],
    testnetAvailable: true,
    docsUrl: 'https://interactivebrokers.github.io/cpwebapi/',
    apiKeyUrl: 'https://www.interactivebrokers.com/',
    requiredPermissions: ['Client Portal API'],
    recommendedPermissions: [],
  },

  async validateCredentials() {
    return {
      valid: false,
      permissions: [],
      error: 'IBKR integration este în development. Disponibil în curând pentru Elite users.',
    };
  },

  async getBalances() {
    throw new Error('IBKR: coming soon');
  },

  async getPositions() {
    throw new Error('IBKR: coming soon');
  },

  async placeOrder() {
    throw new Error('IBKR: coming soon - folosește manual trade entry până atunci');
  },

  async getOrder() {
    return null;
  },

  async cancelOrder() {
    return false;
  },

  async getCurrentPrice() {
    throw new Error('IBKR: market data coming soon');
  },

  async getSymbolInfo() {
    return null;
  },
};
