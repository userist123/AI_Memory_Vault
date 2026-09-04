import { describe, expect, it } from 'vitest';
import type {
  IPlatformModule,
  IPlatformModuleContext,
  IPlatformModuleManifest,
} from '@prosto/platform-sdk';
import {
  ContractFailureCodes,
  LIFECYCLE_CHECK_RESULT_ID,
  MANIFEST_CHECK_RESULT_ID,
  runModuleContractConformance,
  toConformanceReportJson,
} from '@/index.js';

const validManifest: IPlatformModuleManifest = {
  id: 'module-health',
  version: '1.0.0',
  sdkVersion: '^0.1.0',
  title: 'Health',
  dependencies: [],
  optional: false,
  groups: ['Group 1'],
};

class ValidModule implements IPlatformModule {
  init(_ctx: IPlatformModuleContext): void {
    /* empty */
  }

  start(_ctx: IPlatformModuleContext): void {
    /* empty */
  }

  stop(_ctx: IPlatformModuleContext): void {
    /* empty */
  }
}

class BrokenModuleLifecycleFailure extends ValidModule {
  override start(_ctx: IPlatformModuleContext): void {
    throw new Error('start failed');
  }
}

describe('module contract conformance', () => {
  it('returns pass summary for a valid module', async () => {
    const report = await runModuleContractConformance({
      manifest: validManifest,
      module: new ValidModule(),
      now: () => '2026-03-31T00:00:00.000Z',
    });

    expect(report.summary.result).toBe('pass');
    expect(report.summary.failedMandatoryChecks).toBe(0);
    expect(report.moduleId).toBe('module-health');
  });

  it('returns fail summary for a invalid module title', async () => {
    const report = await runModuleContractConformance({
      manifest: {
        ...validManifest,
        id: 'module-broken-title',
        title: '',
      },
      module: new ValidModule(),
    });

    const groupsCheck = report.checks.find(
      (check) => check.id === MANIFEST_CHECK_RESULT_ID,
    );

    expect(groupsCheck?.passed).toBe(false);
    expect(groupsCheck?.code).toBe(ContractFailureCodes.ManifestSchemaInvalid);
    expect(groupsCheck?.details).toContain('Module title must not be empty.');
    expect(report.summary.result).toBe('fail');
  });

  it('returns lifecycle failure code on lifecycle method exception', async () => {
    const report = await runModuleContractConformance({
      manifest: {
        ...validManifest,
        id: 'module-broken-lifecycle',
      },
      module: new BrokenModuleLifecycleFailure(),
    });

    const lifecycleCheck = report.checks.find(
      (check) => check.id === LIFECYCLE_CHECK_RESULT_ID,
    );

    expect(lifecycleCheck?.passed).toBe(false);
    expect(lifecycleCheck?.code).toBe(
      ContractFailureCodes.LifecycleMethodFailed,
    );
    expect(report.summary.result).toBe('fail');
  });

  it('marks the duplication of groups', async () => {
    const report = await runModuleContractConformance({
      manifest: {
        ...validManifest,
        id: 'module-broken-groups',
        groups: ['Group 1', 'Group 1'],
      },
      module: new ValidModule(),
    });

    const groupsCheck = report.checks.find(
      (check) => check.id === MANIFEST_CHECK_RESULT_ID,
    );

    expect(groupsCheck?.passed).toBe(false);
    expect(groupsCheck?.severity).toBe('mandatory');
    expect(groupsCheck?.code).toBe(
      ContractFailureCodes.ManifestSemanticInvalid,
    );
    expect(report.summary.failedMandatoryChecks).toBe(1);
    expect(report.summary.failedAdvisoryChecks).toBe(0);
    expect(report.summary.result).toBe('fail');
  });

  it('serializes machine-readable report as deterministic JSON', async () => {
    const report = await runModuleContractConformance({
      manifest: validManifest,
      module: new ValidModule(),
      now: () => '2026-03-31T00:00:00.000Z',
    });

    const serialized = toConformanceReportJson(report);

    expect(serialized).toContain('"moduleId": "module-health"');
    expect(serialized).toContain('"result": "pass"');
    expect(serialized.endsWith('\n')).toBe(true);
  });
});
