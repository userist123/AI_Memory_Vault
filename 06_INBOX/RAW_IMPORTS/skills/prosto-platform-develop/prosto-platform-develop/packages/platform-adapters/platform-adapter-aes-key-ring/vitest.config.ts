import { configDefaults, defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    include: ['tests/**/*.test.ts'],
    exclude: [...configDefaults.exclude],
    alias: {
      '@/': new URL('./src/', import.meta.url).pathname,
    },
  },
});
