import { describe, expect, it } from 'vitest';
import {
  DiagnosticReportBuilder,
  DiagnosticsReporter,
  RuntimeStartupStatus,
} from '@/diagnostics/index.js';
import { RuntimeErrorCodes } from '@/common/index.js';
import { SecretsRedactor } from '@/security/index.js';

function createReporter(): DiagnosticsReporter {
  const redactor = new SecretsRedactor({ enabled: false });
  return new DiagnosticsReporter(new DiagnosticReportBuilder(redactor));
}

function createRedactingReporter(): DiagnosticsReporter {
  const redactor = new SecretsRedactor({
    enabled: true,
    patterns: ['password', 'token', 'secret', 'key', 'apiKey', 'passphrase'],
  });
  return new DiagnosticsReporter(new DiagnosticReportBuilder(redactor));
}

describe('createStartupReport', () => {
  const reporter = createReporter();

  it('produces success status when no failures', () => {
    const report = reporter.createStartupReport({
      correlationId: 'cid',
      policyMode: 'strict',
      startedAt: '2024-01-01T00:00:00.000Z',
      loadedModules: [{ moduleId: 'mod-a', version: '1.0.0' }],
      skippedModules: [],
      failedModules: [],
    });

    expect(report.status).toBe(RuntimeStartupStatus.Success);
    expect(report.degraded).toBe(false);
    expect(report.type).toBe('startup');
  });

  it('produces degraded status when skipped modules exist', () => {
    const report = reporter.createStartupReport({
      correlationId: 'cid',
      policyMode: 'best-effort',
      startedAt: '2024-01-01T00:00:00.000Z',
      loadedModules: [{ moduleId: 'mod-a', version: '1.0.0' }],
      skippedModules: [
        {
          moduleId: 'mod-b',
          reason: {
            moduleId: 'mod-b',
            phase: 'validate',
            errorCode: RuntimeErrorCodes.CompatibilityMismatch,
            message: 'mismatch',
            remediationHint: 'fix',
          },
        },
      ],
      failedModules: [],
    });

    expect(report.status).toBe(RuntimeStartupStatus.Degraded);
    expect(report.degraded).toBe(true);
  });

  it('produces failed status when all modules fail', () => {
    const report = reporter.createStartupReport({
      correlationId: 'cid',
      policyMode: 'strict',
      startedAt: '2024-01-01T00:00:00.000Z',
      loadedModules: [],
      skippedModules: [],
      failedModules: [
        {
          moduleId: 'mod-a',
          phase: 'lifecycle',
          errorCode: RuntimeErrorCodes.LifecycleStartFailed,
          message: 'start failed',
          remediationHint: 'check',
        },
      ],
    });

    expect(report.status).toBe(RuntimeStartupStatus.Failed);
    expect(report.degraded).toBe(false);
  });

  it('redacts secrets in failure messages', () => {
    const redactingReporter = createRedactingReporter();
    const report = redactingReporter.createStartupReport({
      correlationId: 'cid',
      policyMode: 'strict',
      startedAt: '2024-01-01T00:00:00.000Z',
      loadedModules: [],
      skippedModules: [],
      failedModules: [
        {
          moduleId: 'mod-a',
          phase: 'validate',
          errorCode: RuntimeErrorCodes.ManifestInvalid,
          message: 'token=secret123',
          remediationHint: 'password=secret456',
        },
      ],
    });

    expect(report.failedModules[0]?.message).toBe('token=[REDACTED]');
    expect(report.failedModules[0]?.remediationHint).toBe(
      'password=[REDACTED]',
    );
  });
});

describe('createShutdownReport', () => {
  const redactingReporter = createRedactingReporter();

  it('produces shutdown report with issues', () => {
    const report = redactingReporter.createShutdownReport({
      correlationId: 'cid',
      startedAt: '2024-01-01T00:00:00.000Z',
      stopOrder: ['mod-b', 'mod-a'],
      issues: [
        {
          moduleId: 'mod-b',
          phase: 'shutdown',
          errorCode: RuntimeErrorCodes.ShutdownTimeout,
          message: 'timeout',
          remediationHint: 'increase timeout',
        },
      ],
    });

    expect(report.type).toBe('shutdown');
    expect(report.stopOrder).toEqual(['mod-b', 'mod-a']);
    expect(report.issues).toHaveLength(1);
    expect(report.issues[0]?.moduleId).toBe('mod-b');
  });

  it('redacts secrets in shutdown issues', () => {
    const report = redactingReporter.createShutdownReport({
      correlationId: 'cid',
      startedAt: '2024-01-01T00:00:00.000Z',
      stopOrder: ['mod-a'],
      issues: [
        {
          moduleId: 'mod-a',
          phase: 'shutdown',
          errorCode: RuntimeErrorCodes.ShutdownTimeout,
          message: 'bearer leak',
          remediationHint: 'apikey=leak',
        },
      ],
    });

    expect(report.issues[0]?.message).toBe('bearer [REDACTED]');
    expect(report.issues[0]?.remediationHint).toBe('apikey=[REDACTED]');
  });
});
