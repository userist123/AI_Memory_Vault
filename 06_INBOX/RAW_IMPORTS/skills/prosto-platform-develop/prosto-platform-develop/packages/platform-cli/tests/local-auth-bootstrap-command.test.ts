import { access, mkdtemp, rm } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { afterEach, describe, expect, it } from 'vitest';
import { bootstrapPlatformLocalAuthentication } from '../src/index.js';

let directory: string | undefined;

afterEach(async (): Promise<void> => {
  if (directory !== undefined) {
    await rm(directory, { recursive: true, force: true });
    directory = undefined;
  }
});

describe('bootstrapPlatformLocalAuthentication', (): void => {
  it('refuses non-interactive output before creating local state', async (): Promise<void> => {
    directory = await mkdtemp(join(tmpdir(), 'prosto-local-auth-no-tty-'));
    const output: string[] = [];
    const database = join(directory, 'local-auth.sqlite');

    await expect(
      bootstrapPlatformLocalAuthentication({
        database,
        output: {
          isInteractive: false,
          write: (message: string) => output.push(message),
        },
      }),
    ).rejects.toThrow('interactive TTY');

    expect(output).toEqual([]);
    await expect(access(database)).rejects.toThrow();
  });

  it('uses the module bootstrap transaction and displays a credential once', async (): Promise<void> => {
    directory = await mkdtemp(join(tmpdir(), 'prosto-local-auth-'));

    const database = join(directory, 'local-auth.sqlite');
    const output: string[] = [];
    const input = {
      database,
      output: {
        isInteractive: true,
        write: (message: string) => output.push(message),
      },
    };

    await expect(bootstrapPlatformLocalAuthentication(input)).resolves.toBe(
      true,
    );
    await expect(bootstrapPlatformLocalAuthentication(input)).resolves.toBe(
      false,
    );
    expect(output).toHaveLength(1);
    expect(output[0]).toContain('One-time password:');
  });
});
