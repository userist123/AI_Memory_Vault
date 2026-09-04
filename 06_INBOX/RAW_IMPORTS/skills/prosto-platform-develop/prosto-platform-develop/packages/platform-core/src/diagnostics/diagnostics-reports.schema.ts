import type {
  IRuntimeOperationalReports,
  IRuntimeShutdownReport,
  IRuntimeStartupReport,
} from './interfaces/index.js';
import { assert } from '@/common/index.js';
import { RuntimeErrorCodes } from '@/common/index.js';

/**
 * Valid error codes for configuration access policy violations.
 * These codes are used to categorize failures in a structured, machine-readable way.
 */
const CONFIG_ACCESS_ERROR_CODES = [
  RuntimeErrorCodes.ConfigAccessDenied,
  RuntimeErrorCodes.ConfigCapabilityInvalid,
  RuntimeErrorCodes.ConfigSectionNotAllowlisted,
  RuntimeErrorCodes.ConfigWildcardForbidden,
];

/**
 * Check if an error code is a config access policy error.
 */
function isConfigAccessErrorCode(errorCode: string): boolean {
  return CONFIG_ACCESS_ERROR_CODES.some((code) => code === errorCode);
}

/**
 * Validate that a diagnostic payload does not contain sensitive data.
 * This is a basic check; production systems should use more sophisticated redaction.
 */
function validateNoSensitiveData(
  payload: Record<string, unknown>,
  context: string,
): void {
  const sensitivePatterns = [
    /key/i,
    /token/i,
    /secret/i,
    /password/i,
    /passphrase/i,
    /connection[_-]?string/i,
    /private[_-]?key/i,
    /api[_-]?key/i,
    /database[_-]?url/i,
    /jwt[_-]?secret/i,
    /encryption[_-]?key/i,
  ];

  const checkKey = (key: string, path: string) => {
    for (const pattern of sensitivePatterns) {
      if (pattern.test(key)) {
        assert(
          false,
          `${context}: Sensitive field '${path}' detected in diagnostic payload. ` +
            'This may indicate a security vulnerability.',
        );
      }
    }
  };

  const walk = (obj: unknown, path: string) => {
    if (obj === null || obj === undefined) return;

    if (typeof obj === 'object' && !Array.isArray(obj)) {
      const entries = Object.entries(obj);

      for (const [key, value] of entries) {
        const currentPath = path ? `${path}.${key}` : key;

        checkKey(key, currentPath);
        walk(value, currentPath);
      }
    } else if (Array.isArray(obj)) {
      obj.forEach((item, index) => {
        walk(item, `${path}[${index}]`);
      });
    }
  };

  walk(payload, '');
}

export function assertStartupReport(report: IRuntimeStartupReport): void {
  assert(report.type === 'startup', 'startup.type must equal "startup"');
  assert(
    typeof report.policyMode === 'string',
    'startup.policyMode must be present',
  );
  assert(
    typeof report.correlationId === 'string' && report.correlationId.length > 0,
    'startup.correlationId is required',
  );
  assert(
    typeof report.startedAt === 'string' && report.startedAt.length > 0,
    'startup.startedAt is required',
  );
  assert(
    typeof report.completedAt === 'string' && report.completedAt.length > 0,
    'startup.completedAt is required',
  );
  assert(
    Array.isArray(report.loadedModules),
    'startup.loadedModules must be an array',
  );
  assert(
    Array.isArray(report.skippedModules),
    'startup.skippedModules must be an array',
  );
  assert(
    Array.isArray(report.failedModules),
    'startup.failedModules must be an array',
  );

  for (const failed of report.failedModules as IRuntimeStartupReport['failedModules']) {
    assert(
      typeof failed.moduleId === 'string' && failed.moduleId.length > 0,
      'failedModules[].moduleId is required',
    );
    assert(
      typeof failed.phase === 'string' && failed.phase.length > 0,
      'failedModules[].phase is required',
    );
    assert(
      typeof failed.errorCode === 'string' && failed.errorCode.length > 0,
      'failedModules[].errorCode is required',
    );
    assert(
      typeof failed.remediationHint === 'string' &&
        failed.remediationHint.length > 0,
      'failedModules[].remediationHint is required',
    );

    // Validate config access error codes have proper structure
    if (isConfigAccessErrorCode(failed.errorCode)) {
      // Ensure remediation hints for config errors don't contain sensitive paths
      assert(
        !failed.remediationHint.includes('key') &&
          !failed.remediationHint.includes('token') &&
          !failed.remediationHint.includes('secret') &&
          !failed.remediationHint.includes('password') &&
          !failed.remediationHint.includes('passphrase') &&
          !failed.remediationHint.includes('connection_string') &&
          !failed.remediationHint.includes('private_key') &&
          !failed.remediationHint.includes('api_key') &&
          !failed.remediationHint.includes('database_url') &&
          !failed.remediationHint.includes('jwt_secret') &&
          !failed.remediationHint.includes('encryption_key'),
        `failedModules[].remediationHint contains sensitive data for error ${failed.errorCode}`,
      );
    }

    // Check for sensitive data in the entire diagnostic object
    validateNoSensitiveData(
      failed as unknown as Record<string, unknown>,
      `failedModules[${failed.moduleId}]`,
    );
  }
}

export function assertShutdownReport(report: IRuntimeShutdownReport): void {
  assert(report.type === 'shutdown', 'shutdown.type must equal "shutdown"');
  assert(
    typeof report.correlationId === 'string' && report.correlationId.length > 0,
    'shutdown.correlationId is required',
  );
  assert(
    Array.isArray(report.stopOrder),
    'shutdown.stopOrder must be an array',
  );
  assert(Array.isArray(report.issues), 'shutdown.issues must be an array');
}

export function validateOperationalReportsSchema(
  reports: IRuntimeOperationalReports,
): void {
  if (reports.startup) {
    assertStartupReport(reports.startup);
  }

  if (reports.shutdown) {
    assertShutdownReport(reports.shutdown);
  }
}
