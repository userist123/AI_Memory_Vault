/** @type {import('next').NextConfig} */
export default {
  reactStrictMode: true,
  serverExternalPackages: ['@electric-sql/pglite', 'pg'],
  // @casa3d/twin-core is consumed as TypeScript source (ESM, `./x.js` specifiers per NodeNext convention).
  transpilePackages: ['@casa3d/twin-core'],
  webpack(config){
    config.resolve.extensionAlias = { ...(config.resolve.extensionAlias || {}), '.js': ['.ts', '.tsx', '.js'] };
    return config;
  },
};
