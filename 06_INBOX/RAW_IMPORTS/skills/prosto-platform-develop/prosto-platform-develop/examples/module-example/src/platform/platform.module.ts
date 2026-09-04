import type {
  IPlatformModule,
  IPlatformModuleContext,
} from '@prosto/platform-sdk';

export class PlatformModule implements IPlatformModule {
  init(_ctx: IPlatformModuleContext): void {
    console.log('[example platform module] initialized.');
  }

  start(_ctx: IPlatformModuleContext): void {
    console.log('[example platform module] started.');
  }

  stop(_ctx: IPlatformModuleContext): void {
    console.log('[example platform module] stopped.');
  }
}
