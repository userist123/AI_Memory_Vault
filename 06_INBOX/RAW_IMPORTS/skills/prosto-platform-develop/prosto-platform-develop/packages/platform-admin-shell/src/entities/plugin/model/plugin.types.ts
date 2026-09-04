import type { IAdminUIPluginManifest } from '@prosto/platform-admin-contracts';

export type PluginStatusType = 'loading' | 'ready' | 'failed' | 'rejected';

export interface IPluginEntry {
  manifest: IAdminUIPluginManifest;
  status: PluginStatusType;
  error?: string;
}
