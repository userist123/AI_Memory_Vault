import type {
  IPlatformModule,
  IPlatformModuleContext,
} from '@prosto/platform-sdk';

/**
 * @internal
 * Reference module used to validate contract conformance.
 */
export class HealthModule implements IPlatformModule {
  init(_ctx: IPlatformModuleContext): void {
    console.log('[health module] initialized.');
  }

  start(_ctx: IPlatformModuleContext): void {
    console.log('[health module] started.');
  }

  stop(_ctx: IPlatformModuleContext): void {
    console.log('[health module] stopped.');
  }
}
