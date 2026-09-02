import { describe, it } from 'vitest';
import { createModuleContractTests } from '@prosto/platform-contract-tests';
import manifest from '../manifest.json';
import { AuthModule } from '../src/index.js';

describe('AuthModule contract', () => {
  createModuleContractTests(
    { manifest, module: new AuthModule() },
    { describe, it },
  );
});
