/** @type {import('next').NextConfig} */
export default {
  reactStrictMode: true,
  serverExternalPackages: ['@electric-sql/pglite', 'pg'],
  // @casa3d/twin-core is consumed as TypeScript source (ESM, `./x.js` specifiers per NodeNext convention).
  transpilePackages: ['@casa3d/twin-core'],
  // Next.js 16 uses Turbopack by default, which has no equivalent of `extensionAlias` for the `./x.js` → `.ts`
  // specifiers of twin-core: build and dev run with `--webpack` (package.json, scripts/run-local.mjs).
  webpack(config){
    config.resolve.extensionAlias = { ...(config.resolve.extensionAlias || {}), '.js': ['.ts', '.tsx', '.js'] };
    return config;
  },
};
