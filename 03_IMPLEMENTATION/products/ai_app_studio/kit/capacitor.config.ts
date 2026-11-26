import type { CapacitorConfig } from '@capacitor/cli';

// Studio-owned. appId is rewritten by the studio per project (dev.aiappstudio.p<id16>); never server.url (DESIGN 3.1).
const config: CapacitorConfig = {
  appId: 'dev.aiappstudio.p0000000000000000',
  appName: 'App',
  webDir: 'dist',
  android: {
    allowMixedContent: false,
  },
};

export default config;
