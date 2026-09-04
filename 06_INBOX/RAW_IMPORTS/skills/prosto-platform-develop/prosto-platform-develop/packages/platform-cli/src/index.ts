#!/usr/bin/env node

import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  bootstrapPlatformLocalAuthentication as bootstrapLocalAuthentication,
  type IPlatformLocalAuthBootstrapCommandInput,
} from '@prosto/platform-module-auth-local-session';

/** @alpha */
export type { IPlatformLocalAuthBootstrapCommandInput };

/**
 * @alpha
 * Initializes durable local authentication state and writes a one-time password
 * only to an explicitly interactive output.
 */
export async function bootstrapPlatformLocalAuthentication(
  input: IPlatformLocalAuthBootstrapCommandInput,
): Promise<boolean> {
  return bootstrapLocalAuthentication(input);
}

/** @internal */
function parseDatabaseOption(arguments_: readonly string[]): string {
  if (arguments_.length === 0) {
    return resolve('.prosto', 'local-auth.sqlite');
  }

  if (arguments_.length === 2 && arguments_[0] === '--database') {
    return resolve(arguments_[1] ?? '');
  }

  throw new Error(
    'Usage: prosto-platform auth bootstrap-local [--database <path>]',
  );
}

/** @internal */
export async function runPlatformCli(
  arguments_: readonly string[],
  output: IPlatformLocalAuthBootstrapCommandInput['output'],
): Promise<void> {
  if (arguments_[0] !== 'auth' || arguments_[1] !== 'bootstrap-local') {
    throw new Error(
      'Usage: prosto-platform auth bootstrap-local [--database <path>]',
    );
  }

  await bootstrapPlatformLocalAuthentication({
    database: parseDatabaseOption(arguments_.slice(2)),
    output,
  });
}

const invokedPath = process.argv[1];

if (
  invokedPath !== undefined &&
  fileURLToPath(import.meta.url) === resolve(invokedPath)
) {
  void runPlatformCli(process.argv.slice(2), {
    isInteractive: process.stdout.isTTY,
    write: (message: string): void => {
      process.stdout.write(message);
    },
  }).catch((error: unknown): void => {
    const message =
      error instanceof Error
        ? error.message
        : 'Local authentication bootstrap failed.';

    process.stderr.write(`${message}\n`);
    process.exitCode = 1;
  });
}
