import type { IAdminUIPluginManifest } from '@prosto/platform-admin-contracts';

export async function loadPlugin(
  manifest: IAdminUIPluginManifest,
): Promise<void> {
  const entryPoint = manifest.metadata?.entryPoint;

  if (!entryPoint) {
    throw new Error(`Plugin ${manifest.id} has no entry point`);
  }

  await import(/* @vite-ignore */ entryPoint);
}
