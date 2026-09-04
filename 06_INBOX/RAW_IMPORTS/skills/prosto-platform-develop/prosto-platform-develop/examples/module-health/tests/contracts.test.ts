import { describe, it } from 'vitest';
import { createModuleContractTests } from '@prosto/platform-contract-tests';
import manifest from '../manifest.json';
import { HealthModule } from '../src/index.js';

describe('HealthModule contract', () => {
  createModuleContractTests(
    { manifest, module: new HealthModule() },
    { describe, it },
  );
});
