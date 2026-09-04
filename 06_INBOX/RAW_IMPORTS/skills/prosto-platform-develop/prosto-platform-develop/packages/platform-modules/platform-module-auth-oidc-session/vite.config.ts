import pkg from './package.json' with { type: 'json' };
import { builtinModules } from 'node:module';
import { resolve } from 'node:path';
import type { Diagnostic } from 'typescript';
import { defineConfig } from 'vite';
import dts from 'vite-plugin-dts';

const externalPackages = Object.keys(pkg.dependencies ?? {});

function hasDtsDiagnostics(diagnostics: readonly Diagnostic[]): void {
  if (diagnostics.length > 0) {
    throw new Error(
      `vite-plugin-dts emitted ${diagnostics.length.toString()} diagnostic(s).`,
    );
  }
}

export default defineConfig({
  resolve: { tsconfigPaths: true },
  plugins: [
    dts({
      entryRoot: 'src',
      afterDiagnostic: hasDtsDiagnostics,
      tsconfigPath: './tsconfig.package.json',
    }),
  ],
  build: {
    target: 'node22',
    sourcemap: true,
    minify: true,
    emptyOutDir: true,
    lib: {
      entry: resolve(__dirname, 'src/index.ts'),
      formats: ['es'],
      fileName: () => 'index.js',
    },
    rolldownOptions: {
      external: (id) =>
        builtinModules.includes(id) ||
        id.startsWith('node:') ||
        externalPackages.some(
          (name) => id === name || id.startsWith(`${name}/`),
        ),
    },
  },
});
