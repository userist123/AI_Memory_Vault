import type {
  IPlatformModuleContext,
  IPlatformModule,
  IPlatformModuleManifest,
} from '@prosto/platform-sdk';
import { RuntimeBuilder } from '@/runtime/runtime.builder.js';
import Fastify from 'fastify';

const demoModuleManifest: IPlatformModuleManifest = {
  id: 'demo-module',
  version: '1.0.0',
  sdkVersion: '^0.0.0',
  title: 'Demo',
  dependencies: [],
};

class DemoModule implements IPlatformModule {
  init(_ctx: IPlatformModuleContext): void {
    console.log('[demo module] initialized');
  }

  start(_ctx: IPlatformModuleContext): void {
    console.log('[demo module] started');
  }

  stop(_ctx: IPlatformModuleContext): void {
    console.log('[demo module] stopped');
  }
}

async function main(): Promise<void> {
  const runtime = new RuntimeBuilder().build({
    environment: process.env.NODE_ENV || 'production',
    modules: [
      {
        type: 'memory',
        manifest: demoModuleManifest,
        module: new DemoModule(),
      },
      {
        type: 'path',
        path: '../../examples/module-auth/artifacts/module-auth-0.0.0.zip',
      },
      {
        type: 'path',
        path: '../../examples/module-health/artifacts/module-health-0.0.0.zip',
      },
    ],
  });

  await runtime.start();

  console.log(JSON.stringify(runtime.reports.startup, null, 2));

  const shutdown = async (): Promise<void> => {
    console.log('Shutting down...');
    await runtime.stop();
    console.log(JSON.stringify(runtime.reports.shutdown, null, 2));
    process.exit(0);
  };

  process.on('SIGINT', shutdown);
  process.on('SIGTERM', shutdown);

  const fastify = Fastify({
    logger: true,
  });

  fastify.get('/', async function handler(_request, _reply) {
    return runtime.reports.startup;
  });

  await fastify.listen({ port: 3001 }).catch((error) => {
    fastify.log.error(error);

    throw new Error(error instanceof Error ? error.message : String(error), {
      cause: error,
    });
  });
}

main().catch(() => {
  process.exit(1);
});
