import type {
  IPlatformModuleContext,
  IPlatformModule,
  IPlatformModuleManifest,
} from '@prosto/platform-sdk';
import { SDK_CONTRACT_VERSION } from '@prosto/platform-sdk';
import {
  type IPlatformRuntime,
  type IRuntimeBuilderOptions,
  RuntimeBuilder,
} from '@/runtime/index.js';

export async function createRuntime(
  options: IRuntimeBuilderOptions,
): Promise<IPlatformRuntime> {
  const runtime = new RuntimeBuilder().build(options);
  await runtime.start();
  return runtime;
}

export function createManifest(
  input: Partial<IPlatformModuleManifest> & Pick<IPlatformModuleManifest, 'id'>,
): IPlatformModuleManifest {
  return {
    version: input.version ?? SDK_CONTRACT_VERSION,
    sdkVersion: input.sdkVersion ?? '^0.0.0',
    nodeVersion: input.nodeVersion,
    title: input.title ?? 'Test Module',
    dependencies: input.dependencies ?? [],
    ...input,
  };
}

export interface ITestModuleBehavior {
  readonly failOnRegister?: boolean;
  readonly failOnInit?: boolean;
  readonly failOnStart?: boolean;
  readonly stopDelayMs?: number;
}

export class TestModule implements IPlatformModule {
  readonly calls: string[] = [];
  private readonly _behavior: ITestModuleBehavior;

  constructor(behavior: ITestModuleBehavior = {}) {
    this._behavior = behavior;
  }

  register(_ctx: IPlatformModuleContext): void {
    this.calls.push('register');

    if (this._behavior.failOnRegister) {
      throw new Error('register failed');
    }
  }

  init(_ctx: IPlatformModuleContext): void {
    this.calls.push('init');

    if (this._behavior.failOnInit) {
      throw new Error('init failed');
    }
  }

  start(_ctx: IPlatformModuleContext): void {
    this.calls.push('start');

    if (this._behavior.failOnStart) {
      throw new Error('start failed');
    }
  }

  async stop(_ctx: IPlatformModuleContext): Promise<void> {
    this.calls.push('stop');

    if (this._behavior.stopDelayMs && this._behavior.stopDelayMs > 0) {
      await new Promise((resolve) =>
        setTimeout(resolve, this._behavior.stopDelayMs),
      );
    }
  }
}
