import type {
  IPlatformModule,
  IPlatformModuleContext,
} from '@prosto/platform-sdk';

/**
 * @internal
 * Reference module used to validate contract conformance.
 */
export class AuthModule implements IPlatformModule {
  init(_ctx: IPlatformModuleContext): void {
    console.log('[auth module] initialized.');
  }

  start(_ctx: IPlatformModuleContext): void {
    console.log('[auth module] started.');
  }

  stop(_ctx: IPlatformModuleContext): void {
    console.log('[auth module] stopped.');
  }
}
