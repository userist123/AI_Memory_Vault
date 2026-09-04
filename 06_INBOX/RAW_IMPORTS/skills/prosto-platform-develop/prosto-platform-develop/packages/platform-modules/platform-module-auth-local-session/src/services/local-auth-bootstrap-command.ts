import { mkdir } from 'node:fs/promises';
import { dirname } from 'node:path';
import { PlatformArgon2idPasswordHasher } from '@prosto/platform-adapter-auth-local';
import {
  createPlatformLocalAuthBootstrapDataSource,
  LocalAuthBootstrapService,
} from './local-auth-bootstrap.service.js';

/** @alpha */
export interface IPlatformLocalAuthBootstrapCommandInput {
  readonly database: string;
  readonly output: {
    readonly isInteractive: boolean;
    write(message: string): void;
  };
}

/**
 * @alpha
 * Runs the same transactional local-auth bootstrap used by the module lifecycle.
 */
export async function bootstrapPlatformLocalAuthentication(
  input: IPlatformLocalAuthBootstrapCommandInput,
): Promise<boolean> {
  if (!input.output.isInteractive) {
    throw new Error(
      'Local authentication bootstrap requires an interactive TTY; no credential was created.',
    );
  }

  await mkdir(dirname(input.database), { recursive: true });

  const dataSource = createPlatformLocalAuthBootstrapDataSource(input.database);

  try {
    await dataSource.initialize();
    await dataSource.runMigrations();

    const result = await new LocalAuthBootstrapService(
      dataSource,
      new PlatformArgon2idPasswordHasher(),
    ).bootstrap();

    if (
      result.created &&
      result.username !== undefined &&
      result.password !== undefined
    ) {
      input.output.write(
        `\nLocal authentication bootstrap\nUsername: ${result.username}\nOne-time password: ${result.password}\nChange this password before using the admin BFF.\n`,
      );
    }

    return result.created;
  } finally {
    if (dataSource.isInitialized) {
      await dataSource.destroy();
    }
  }
}
