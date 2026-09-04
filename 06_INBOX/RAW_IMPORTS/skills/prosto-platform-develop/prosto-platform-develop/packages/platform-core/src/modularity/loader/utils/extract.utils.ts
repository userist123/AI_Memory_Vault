import { mkdir, mkdtemp, rm, stat } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve, sep } from 'node:path';
import AdmZip from 'adm-zip';
import * as tar from 'tar';

/**
 * @alpha
 * Create a temporary directory with the given prefix.
 */
export async function createTempDir(prefix: string): Promise<string> {
  return mkdtemp(join(tmpdir(), `${prefix}-`));
}

/**
 * @alpha
 * Clean up a temporary directory.
 */
export async function cleanupTempDir(dir: string): Promise<void> {
  await rm(dir, { recursive: true, force: true });
}

/**
 * @alpha
 * Check if a file exists.
 */
export async function fileExists(filePath: string): Promise<boolean> {
  try {
    await stat(filePath);
    return true;
  } catch {
    return false;
  }
}

/**
 * @alpha
 * Secure archive extraction with path traversal protection.
 */
export class ArtifactExtractor {
  private static readonly MAX_FILES = 10_000;
  private static readonly MAX_TOTAL_SIZE = 100 * 1024 * 1024; // 100MB
  private static readonly MAX_FILENAME_LENGTH = 255;

  static async extractZip(
    archivePath: string,
    extractPath: string,
  ): Promise<void> {
    await mkdir(extractPath, { recursive: true });

    const zip = new AdmZip(archivePath);
    const entries = zip.getEntries();

    let fileCount = 0;
    let totalSize = 0;

    for (const entry of entries) {
      const entryName = entry.entryName;

      if (!this._isPathSafe(entryName, extractPath)) {
        throw new Error(`Path traversal detected in zip entry: ${entryName}`);
      }

      if (entryName.length > this.MAX_FILENAME_LENGTH) {
        throw new Error(`Filename exceeds maximum length: ${entryName}`);
      }

      if (entry.isDirectory) continue;

      fileCount++;
      totalSize += entry.header.size;

      if (fileCount > this.MAX_FILES) {
        throw new Error(
          `Archive exceeds maximum file count of ${this.MAX_FILES}`,
        );
      }

      if (totalSize > this.MAX_TOTAL_SIZE) {
        throw new Error(
          `Archive exceeds maximum size of ${this.MAX_TOTAL_SIZE / (1024 * 1024)}MB`,
        );
      }
    }

    zip.extractAllTo(extractPath, true);
  }

  static async extractTgz(
    archivePath: string,
    extractPath: string,
  ): Promise<void> {
    await mkdir(extractPath, { recursive: true });

    let fileCount = 0;
    let totalSize = 0;

    await tar.extract({
      file: archivePath,
      cwd: extractPath,
      strict: true,
      onReadEntry: (entry) => {
        const entryName = entry.path;

        if (!this._isPathSafe(entryName, extractPath)) {
          throw new Error(`Path traversal detected in tgz entry: ${entryName}`);
        }

        if (entryName.length > this.MAX_FILENAME_LENGTH) {
          throw new Error(`Filename exceeds maximum length: ${entryName}`);
        }

        if (entry.type === 'SymbolicLink') {
          throw new Error(`Symlinks not allowed in artifacts: ${entryName}`);
        }

        if (entry.type === 'File') {
          fileCount++;
          totalSize += entry.size;

          if (fileCount > this.MAX_FILES) {
            throw new Error(
              `Archive exceeds maximum file count of ${this.MAX_FILES}`,
            );
          }

          if (totalSize > this.MAX_TOTAL_SIZE) {
            throw new Error(
              `Archive exceeds maximum size of ${this.MAX_TOTAL_SIZE / (1024 * 1024)}MB`,
            );
          }
        }
      },
    });
  }

  private static _isPathSafe(entryName: string, destDir: string): boolean {
    if (entryName.includes('..')) {
      return false;
    }

    if (entryName.startsWith('/') || /^[a-zA-Z]:\\/.test(entryName)) {
      return false;
    }

    if (entryName.includes('\\') && entryName.includes('/')) {
      return false;
    }

    const resolved = resolve(destDir, entryName);
    const resolvedDest = resolve(destDir);

    return resolved.startsWith(resolvedDest + sep) || resolved === resolvedDest;
  }
}
