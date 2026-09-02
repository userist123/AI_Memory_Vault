import { execSync } from 'node:child_process';

function run(command) {
  execSync(command, {
    stdio: 'inherit',
    windowsHide: true,
  });
}

run(
  'npm run test --workspace @prosto/platform-core -- --run tests/integration/runtime-policy-validation.test.ts',
);
run(
  'npm run test --workspace @prosto/platform-core -- --run tests/integration/critical-failure.test.ts',
);

console.log(
  'validate:runtime-policy passed: diagnostics schema and policy-critical behavior are verified.',
);
