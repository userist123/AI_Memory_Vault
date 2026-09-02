import type { Diagnostic } from 'typescript';
import { builtinModules } from 'node:module';
import { resolve } from 'path';
import { defineConfig } from 'vite';
import dts from 'vite-plugin-dts';

function hasDtsDiagnostics(diagnostics: readonly Diagnostic[]): void {
  if (diagnostics.length > 0) {
    throw new Error(
      `vite-plugin-dts emitted ${diagnostics.length.toString()} diagnostic(s).`,
    );
  }
}

export default defineConfig({
  resolve: {
    tsconfigPaths: true,
  },
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
    copyPublicDir: true,
    lib: {
      entry: resolve(__dirname, 'src/index.ts'),
      formats: ['es'],
      fileName: () => 'index.js',
    },
    rolldownOptions: {
      external: (id) => builtinModules.includes(id) || id.startsWith('node:'),
    },
  },
});
