import { describe, expect, it } from 'vitest';
import fs from 'node:fs';
import path from 'node:path';

const SRC_DIR = path.resolve('src');

const ALLOWED_INNER_LAYER_IMPORTS: Record<string, Set<string>> = {
  shared: new Set(['shared']),
  entities: new Set(['shared', 'entities']),
  features: new Set(['shared', 'entities', 'features']),
  processes: new Set(['shared', 'entities', 'features', 'processes']),
  widgets: new Set(['shared', 'entities', 'features', 'widgets']),
  pages: new Set(['shared', 'entities', 'features', 'widgets', 'pages']),
  app: new Set([
    'shared',
    'entities',
    'features',
    'processes',
    'widgets',
    'pages',
    'app',
  ]),
};

const FORBIDDEN_FRAMEWORK_PACKAGES = new Set(['vue', 'pinia', 'vuetify']);
const FORBIDDEN_INTERNAL_PACKAGES = new Set([
  '@prosto/platform-core',
  '@prosto/platform-adapter-http',
  '@prosto/platform-adapter-admin-bff',
  '@prosto/platform-cli',
  '@prosto/platform-contract-tests',
  '@prosto/platform-sdk',
]);

function getLayer(
  filePath: string,
): { layer: string; sublayer?: string } | null {
  const rel = path.relative(SRC_DIR, filePath);
  const segments = rel.split(path.sep);

  if (segments[0] === 'shared') {
    if (segments[1] === 'api') {
      return { layer: 'shared', sublayer: 'api' };
    }

    if (segments[1] === 'observability') {
      return { layer: 'shared', sublayer: 'observability' };
    }

    return { layer: 'shared' };
  }

  if (segments[0] === 'entities' && segments[1]) {
    if (segments[2] === 'model') {
      return { layer: 'entities', sublayer: 'model' };
    }

    if (segments[2] === 'vue') {
      return { layer: 'entities', sublayer: 'vue' };
    }

    return { layer: 'entities' };
  }

  if (segments[0] === 'features' && segments[1]) {
    if (segments[2] === 'model') {
      return { layer: 'features', sublayer: 'model' };
    }

    if (segments[2] === 'vue') {
      return { layer: 'features', sublayer: 'vue' };
    }

    if (segments[2] === 'api') {
      return { layer: 'features', sublayer: 'api' };
    }

    return { layer: 'features' };
  }

  for (const top of ['processes', 'widgets', 'pages', 'app']) {
    if (segments[0] === top) return { layer: top };
  }

  return null;
}

function getSlice(filePath: string): string {
  return path.relative(SRC_DIR, filePath).split(path.sep)[0];
}

function getTargetLayer(importPath: string): string | null {
  if (!importPath.startsWith('@/')) return null;
  const segments = importPath
    .slice(2)
    .replace(/\.(js|ts|vue|mjs|cjs)$/, '')
    .split('/');
  if (segments[0] === 'shared') return 'shared';
  if (segments[0] === 'entities') return 'entities';
  if (segments[0] === 'features') return 'features';
  if (segments[0] === 'processes') return 'processes';
  if (segments[0] === 'widgets') return 'widgets';
  if (segments[0] === 'pages') return 'pages';
  if (segments[0] === 'app') return 'app';
  return null;
}

function getTargetSlice(importPath: string): string | null {
  if (!importPath.startsWith('@/')) return null;
  return importPath.slice(2).split('/')[0];
}

function collectPublicApis(): Set<string> {
  const apis = new Set<string>();
  const walk = (dir: string) => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const fullPath = path.join(dir, entry.name);
      if (['node_modules', 'dist', 'coverage'].includes(entry.name)) {
        continue;
      }
      if (entry.isDirectory()) {
        walk(fullPath);
      } else if (entry.isFile() && entry.name === 'index.ts') {
        const rel = path.relative(SRC_DIR, dir);
        apis.add(rel);
        apis.add(rel + '/index');
      }
    }
  };
  walk(SRC_DIR);
  return apis;
}

function isPublicApi(importPath: string, publicApis: Set<string>): boolean {
  if (!importPath.startsWith('@/')) return true;

  let clean = importPath.slice(2);

  for (const ext of ['.vue', '.js', '.ts', '.mjs', '.cjs']) {
    if (clean.endsWith(ext)) clean = clean.slice(0, -ext.length);
  }

  if (clean.endsWith('/index')) return true;
  if (publicApis.has(clean)) return true;

  for (const api of publicApis) {
    if (clean === api || clean.startsWith(api + '/')) return true;
  }

  return false;
}

function extractImports(content: string): string[] {
  const imports: string[] = [];
  const pattern =
    /import\s+(?:type\s+)?(?:[\w\s{},*]+from\s+)?["']([^"']+)["']/g;
  let match;

  while ((match = pattern.exec(content)) !== null) {
    imports.push(match[1]);
  }

  return imports;
}

function findTsFiles(dir: string): string[] {
  const results: string[] = [];
  const entries = fs.readdirSync(dir, { withFileTypes: true });

  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name);

    if (['node_modules', 'dist', 'coverage'].includes(entry.name)) {
      continue;
    }

    if (entry.isDirectory()) {
      results.push(...findTsFiles(fullPath));
    } else if (/\.(ts|vue)$/.test(entry.name)) {
      results.push(fullPath);
    }
  }

  return results;
}

describe('Admin shell architecture boundaries', () => {
  const publicApis = collectPublicApis();
  const files = findTsFiles(SRC_DIR);
  const layerViolations: string[] = [];
  const publicApiViolations: string[] = [];
  const frameworkViolations: string[] = [];
  const packageViolations: string[] = [];

  for (const filePath of files) {
    const relPath = path.relative(SRC_DIR, filePath);
    const content = fs.readFileSync(filePath, 'utf-8');
    const imports = extractImports(content);
    const layerInfo = getLayer(filePath);

    if (!layerInfo) continue;

    const { layer, sublayer } = layerInfo;
    const allowedTargets = ALLOWED_INNER_LAYER_IMPORTS[layer];
    const slice = getSlice(filePath);

    for (const importPath of imports) {
      const isInternalSliceImport = importPath.startsWith('@/');
      const targetLayer = isInternalSliceImport
        ? getTargetLayer(importPath)
        : null;

      if (
        targetLayer !== null &&
        allowedTargets &&
        !allowedTargets.has(targetLayer)
      ) {
        layerViolations.push(
          `Layer violation: ${relPath} (${layer}) imports ${importPath} (${targetLayer})`,
        );
      }

      if (isInternalSliceImport) {
        const targetSlice = getTargetSlice(importPath);

        if (targetSlice && targetSlice !== slice) {
          if (!isPublicApi(importPath, publicApis)) {
            publicApiViolations.push(
              `Cross-slice import without public API: ${relPath} imports ${importPath}`,
            );
          }
        }
      }

      if (
        FORBIDDEN_FRAMEWORK_PACKAGES.has(importPath) ||
        importPath.startsWith('vue/') ||
        importPath.startsWith('pinia/') ||
        importPath.startsWith('vuetify/')
      ) {
        if (sublayer === 'model' && layer === 'features') {
          frameworkViolations.push(
            `Forbidden framework import in features model layer: ${relPath} imports ${importPath}`,
          );
        }

        if (sublayer === 'api' && layer === 'shared') {
          frameworkViolations.push(
            `Forbidden framework import in shared api layer: ${relPath} imports ${importPath}`,
          );
        }
      }

      for (const forbidden of FORBIDDEN_INTERNAL_PACKAGES) {
        if (
          importPath === forbidden ||
          importPath.startsWith(forbidden + '/')
        ) {
          packageViolations.push(
            `Forbidden internal package import: ${relPath} imports ${importPath}`,
          );
        }
      }
    }
  }

  it('should not violate layer dependency rules', () => {
    expect(layerViolations).toEqual([]);
  });

  it('should use public APIs for cross-slice imports', () => {
    expect(publicApiViolations).toEqual([]);
  });

  it('should not import framework packages in features model and shared api layers', () => {
    expect(frameworkViolations).toEqual([]);
  });

  it('should not import forbidden internal packages', () => {
    expect(packageViolations).toEqual([]);
  });
});
