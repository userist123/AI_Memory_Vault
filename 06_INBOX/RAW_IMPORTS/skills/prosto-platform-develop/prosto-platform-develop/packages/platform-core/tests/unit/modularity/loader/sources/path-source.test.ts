import { createHash } from 'node:crypto';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { PathSource } from '@/modularity/index.js';
import { RuntimeErrorCodes } from '@/common/index.js';

describe('PathSource', () => {
  it('rejects when checksum does not match artifact payload', async () => {
    const dir = await mkdtemp(join(tmpdir(), 'prosto-path-source-'));
    const artifactPath = join(dir, 'module.zip');

    try {
      await writeFile(artifactPath, 'payload', 'utf8');

      const source = new PathSource({
        type: 'path',
        moduleIdHint: 'module-path',
        path: artifactPath,
        integrity: {
          checksum:
            'sha256:0000000000000000000000000000000000000000000000000000000000000000',
        },
      });

      const result = await source.load();

      expect('reasonCode' in result).toBe(true);

      if ('reasonCode' in result) {
        expect(result.reasonCode).toBe(
          RuntimeErrorCodes.SourceIntegrityMismatch,
        );
      }
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it('returns explicitly-not-implemented after successful checksum preflight', async () => {
    const dir = await mkdtemp(join(tmpdir(), 'prosto-path-source-'));
    const artifactPath = join(dir, 'module.zip');

    try {
      await writeFile(artifactPath, 'payload', 'utf8');
      const checksum = createHash('sha256').update('payload').digest('hex');

      const source = new PathSource({
        type: 'path',
        moduleIdHint: 'module-path',
        path: artifactPath,
        integrity: { checksum: `sha256:${checksum}` },
      });

      const result = await source.load();

      expect('reasonCode' in result).toBe(true);

      if ('reasonCode' in result) {
        expect(result.reasonCode).toBe(
          RuntimeErrorCodes.SourceExtractionFailed,
        );
      }
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });
});
