import { Polar } from '@polar-sh/sdk';

const accessToken = process.env.POLAR_ACCESS_TOKEN;
const serverMode = (process.env.POLAR_SERVER_MODE || 'sandbox') as 'sandbox' | 'production';

export const polar = accessToken
  ? new Polar({
      accessToken,
      server: serverMode,
    })
  : null;

export function isPolarConfigured(): boolean {
  return polar !== null;
}

export const POLAR_WEBHOOK_SECRET = process.env.POLAR_WEBHOOK_SECRET || '';
