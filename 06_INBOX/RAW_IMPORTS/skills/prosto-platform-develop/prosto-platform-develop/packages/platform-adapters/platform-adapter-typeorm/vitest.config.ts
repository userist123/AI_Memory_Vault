import { configDefaults, defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    include:
      process.env.PROSTO_TYPEORM_INTEGRATION === '1'
        ? ['tests/integration/**/*.test.ts']
        : ['tests/**/*.test.ts'],
    exclude: [...configDefaults.exclude],
    alias: {
      '@/': new URL('./src/', import.meta.url).pathname,
    },
  },
});
