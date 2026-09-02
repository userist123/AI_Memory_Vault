import { execSync } from 'node:child_process';

execSync(
  'npm run test --workspace @prosto/platform-core -- --run tests/integration/determinism.test.ts',
  {
    stdio: 'inherit',
    windowsHide: true,
  },
);

console.log(
  'test:lifecycle-determinism passed: deterministic startup ordering verified.',
);
