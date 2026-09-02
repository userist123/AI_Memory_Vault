import type {
  IContractCheckResult,
  IContractConformanceSummary,
  IModuleContractConformanceReport,
} from '@/interfaces/index.js';

/**
 * @alpha
 * Builds deterministic conformance summary from check results.
 */
export function buildConformanceSummary(
  checks: IContractCheckResult[],
): IContractConformanceSummary {
  const passedChecks: IContractCheckResult[] = [];
  const failedMandatoryChecks: IContractCheckResult[] = [];
  const failedAdvisoryChecks: IContractCheckResult[] = [];

  checks.forEach((check) => {
    if (check.passed) {
      passedChecks.push(check);
      return;
    }

    switch (check.severity) {
      case 'mandatory':
        failedMandatoryChecks.push(check);
        break;

      case 'advisory':
        failedAdvisoryChecks.push(check);
        break;
    }
  });

  return {
    totalChecks: checks.length,
    passedChecks: passedChecks.length,
    failedMandatoryChecks: failedMandatoryChecks.length,
    failedAdvisoryChecks: failedAdvisoryChecks.length,
    result: failedMandatoryChecks.length ? 'fail' : 'pass',
  };
}

/**
 * @alpha
 * Builds machine-readable module conformance report.
 */
export function buildConformanceReport(
  params: Omit<IModuleContractConformanceReport, 'summary'>,
): IModuleContractConformanceReport {
  return {
    moduleId: params.moduleId,
    moduleVersion: params.moduleVersion,
    generatedAt: params.generatedAt,
    checks: params.checks,
    summary: buildConformanceSummary(params.checks),
  };
}

/**
 * @alpha
 * Serializes report in deterministic JSON format for CI consumers.
 */
export function toConformanceReportJson(
  report: IModuleContractConformanceReport,
): string {
  return `${JSON.stringify(report, null, 2)}\n`;
}
