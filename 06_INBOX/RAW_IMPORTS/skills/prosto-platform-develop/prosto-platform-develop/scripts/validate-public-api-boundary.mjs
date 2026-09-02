import { readFile } from 'node:fs/promises';
import path from 'node:path';

const PACKAGE_DIRS = [
  'platform-sdk',
  'platform-admin-contracts',
  'platform-core',
  'platform-contract-tests',
  'platform-cli',
  'platform-adapters/platform-adapter-http',
  'platform-adapters/platform-adapter-auth-oidc',
  'platform-adapters/platform-adapter-aes-key-ring',
  'platform-adapters/platform-adapter-auth-oidc-session',
  'platform-modules/platform-module-auth-oidc-session',
  'platform-modules/platform-module-auth-local-session',
  'platform-adapters/platform-adapter-typeorm',
];

const FORBIDDEN_PUBLIC_TYPE_IMPORTS = new Set([
  'fastify',
  'typeorm',
  'node:crypto',
  'jose',
  'openid-client',
]);

const RELATIVE_DECLARATION_IMPORT_RE =
  /(?:import|export)(?:\s+type)?(?:[\s\S]*?\s+from)?\s*['"](\.[^'"]+)['"]/gmu;
const EXTERNAL_DECLARATION_IMPORT_RE =
  /(?:import|export)(?:\s+type)?(?:[\s\S]*?\s+from)?\s*['"]([^.'"][^'"]*)['"]/gmu;
const PUBLIC_DECLARATION_RE =
  /export\s+(?:declare\s+)?(?:abstract\s+)?(?:class|function|const|interface)\s+[A-Za-z_$]|export\s+type\s+[A-Za-z_$]/u;

const STABILITY_DECLARATION_FILES = new Map([
  [
    'platform-adapter-auth-oidc',
    new Set([
      'errors/index.d.ts',
      'interfaces/index.d.ts',
      'platform-oidc-bearer-resolver.d.ts',
    ]),
  ],
  [
    'platform-adapter-aes-key-ring',
    new Set(['errors/index.d.ts', 'platform-aes-key-ring-cipher.d.ts']),
  ],
  [
    'platform-adapter-auth-oidc-session',
    new Set([
      'errors/index.d.ts',
      'interfaces/index.d.ts',
      'platform-oidc-session-runtime.d.ts',
    ]),
  ],
  [
    'platform-module-auth-oidc-session',
    new Set(['interfaces/index.d.ts', 'platform-auth-session.module.d.ts']),
  ],
  [
    'platform-module-auth-local-session',
    new Set([
      'interfaces/index.d.ts',
      'platform-auth-local-session.module.d.ts',
      'services/local-auth-bootstrap-command.d.ts',
    ]),
  ],
]);

async function collectPublicDeclarationFiles(entryPath) {
  const files = new Map();
  const pending = [entryPath];

  while (pending.length > 0) {
    const filePath = pending.pop();
    if (filePath === undefined || files.has(filePath)) {
      continue;
    }

    const source = await readFile(filePath, 'utf8');
    files.set(filePath, source);
    RELATIVE_DECLARATION_IMPORT_RE.lastIndex = 0;

    let match;
    while ((match = RELATIVE_DECLARATION_IMPORT_RE.exec(source)) !== null) {
      const specifier = match[1];
      if (specifier === undefined) {
        continue;
      }

      const declarationSpecifier = specifier.endsWith('.js')
        ? `${specifier.slice(0, -'.js'.length)}.d.ts`
        : `${specifier}.d.ts`;
      const targetPath = path.resolve(
        path.dirname(filePath),
        declarationSpecifier,
      );
      pending.push(targetPath);
    }
  }

  return files;
}

for (const packageDir of PACKAGE_DIRS) {
  const packageBaseName = path.basename(packageDir);
  const packageJsonPath = path.resolve('packages', packageDir, 'package.json');
  const manifest = JSON.parse(await readFile(packageJsonPath, 'utf8'));
  const packageName = String(manifest.name ?? '');
  const rootExport = manifest.exports?.['.'];

  if (!rootExport || typeof rootExport !== 'object') {
    throw new Error(
      `Public API boundary violation: ${packageName} must export only the package root entry point.`,
    );
  }

  const exportKeys = Object.keys(manifest.exports);

  if (exportKeys.length !== 1 || exportKeys[0] !== '.') {
    throw new Error(
      `Public API boundary violation: ${packageName} exports must be restricted to ".".`,
    );
  }

  if (manifest.types !== './dist/index.d.ts') {
    throw new Error(
      `Public API boundary violation: ${packageName} must set types to ./dist/index.d.ts.`,
    );
  }

  if (manifest.main !== './dist/index.js') {
    throw new Error(
      `Public API boundary violation: ${packageName} must set main to ./dist/index.js.`,
    );
  }

  if (
    !packageBaseName.startsWith('platform-adapter-auth') &&
    packageBaseName !== 'platform-module-auth-oidc-session' &&
    packageBaseName !== 'platform-module-auth-local-session'
  ) {
    continue;
  }

  const declarationPath = path.resolve(
    'packages',
    packageDir,
    'dist',
    'index.d.ts',
  );
  const declarations = await collectPublicDeclarationFiles(declarationPath);
  const stabilityFiles =
    STABILITY_DECLARATION_FILES.get(packageBaseName) ?? new Set();

  for (const [filePath, source] of declarations) {
    EXTERNAL_DECLARATION_IMPORT_RE.lastIndex = 0;
    let match;
    while ((match = EXTERNAL_DECLARATION_IMPORT_RE.exec(source)) !== null) {
      const specifier = match[1];
      if (
        specifier !== undefined &&
        FORBIDDEN_PUBLIC_TYPE_IMPORTS.has(specifier)
      ) {
        throw new Error(
          `Public API boundary violation: ${packageName} leaks ${specifier} from ${filePath}.`,
        );
      }
    }

    const declarationRelativePath = path
      .relative(path.dirname(declarationPath), filePath)
      .replaceAll('\\', '/');
    if (
      stabilityFiles.has(declarationRelativePath) &&
      PUBLIC_DECLARATION_RE.test(source) &&
      !source.includes('@alpha')
    ) {
      throw new Error(
        `Public API boundary violation: ${packageName} declaration ${filePath} is missing a stability JSDoc tag.`,
      );
    }
  }
}

console.log(
  'validate:public-api-boundary passed: package exports are constrained to root public entry points.',
);
