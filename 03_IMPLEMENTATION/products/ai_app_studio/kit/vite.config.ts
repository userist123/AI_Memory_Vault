import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// Studio-owned. Output goes to ./dist (the web build image passes --outDir /out/dist).
export default defineConfig({
  plugins: [react()],
  base: './',
  build: {
    target: 'es2022',
    sourcemap: false,
    assetsInlineLimit: 0,
    emptyOutDir: true,
  },
});
