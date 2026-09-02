# Module Loading Security Policy

## Overview

This document describes the security controls for module loading in the prosto-platform runtime. These controls implement ADR-0003: Module Loading Security (Allowlist + Integrity).

## Security Controls

### 1. Allowlist Policy

The allowlist policy enforces that modules can only be loaded if they match explicit allowlist entries. This prevents unauthorized or unexpected modules from being loaded in production environments.

#### Configuration

```typescript
import { createProductionAllowlistPolicy } from '@prosto/platform-core/security';

const policy = new AllowlistPolicyEvaluator({
  environment: 'production',
  allowlist: [
    {
      moduleIdPattern: '@prosto/auth',
      versionPattern: '^1.0.0',
      requiredSecurityClass: 'trusted',
    },
    {
      moduleIdPattern: '@prosto/health',
      versionPattern: '*',
    },
  ],
  requireAllowlist: true,
});
```

#### Policy Decision Flow

```
Module Load Request
        │
        ▼
┌──────────────────┐
│ Check Security   │──Missing──▶ REJECT: MissingSecurityMetadata
│ Classification   │
└────────┬─────────┘
         │ Present
         ▼
┌──────────────────┐
│ Check Blocked    │──Blocked──▶ REJECT: SecurityClassBlocked
│ Security Classes │
└────────┬─────────┘
         │ Allowed
         ▼
┌──────────────────┐
│ Match Allowlist  │──No Match─▶ REJECT: NotInAllowlist
│ Entry            │
└────────┬─────────┘
         │ Matched
         ▼
     ALLOWED
```

### 2. Integrity Verification

Module artifacts must include integrity evidence (checksum or signature) to verify they haven't been tampered with during transit or storage.

#### Supported Formats

- **Checksum**: `sha256:<hex>`, `sha256-<base64>`, or plain hex (64 chars)
- **Signature**: Base64-encoded cryptographic signature

#### Verification Flow

```
Artifact + Evidence
        │
        ▼
┌──────────────────┐
│ Parse Evidence   │──Invalid──▶ REJECT: InvalidEvidenceFormat
└────────┬─────────┘
         │ Valid
         ▼
┌──────────────────┐
│ Compute Hash     │
│ of Payload       │
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ Compare with     │──Mismatch─▶ REJECT: IntegrityMismatch
│ Expected Value   │
└────────┬─────────┘
         │ Match
         ▼
     VERIFIED
```

### 3. Metadata Validation

Module manifests must contain valid security metadata before loading.

#### Required Fields

| Field | Description | Valid Values |
|-------|-------------|--------------|
| `securityClass` | Security classification level | `trusted`, `internal`, `third-party-reviewed` |
| `criticality` | Module criticality level | `critical`, `standard`, `optional` |
| `checksum` or `signature` | Integrity evidence | Valid checksum/signature format |

### 4. Security Classifications

| Classification | Description | Production | Development |
|----------------|-------------|------------|-------------|
| `trusted` | Fully vetted with verified signatures | ✅ | ✅ |
| `internal` | Internal modules with checksums | ✅ | ✅ |
| `third-party-reviewed` | Externally reviewed modules | ✅ | ✅ |
| `unreviewed` | No security review | ❌ | ❌ |

## CI/CD Integration

### Policy Gates

The following checks run on every pull request:

```yaml
# .github/workflows/policy-gates.yml
jobs:
  runtime-policy:
    steps:
      - name: FF-04 startup diagnostics and runtime policy
        run: npm run validate:runtime-policy
```

### Performance Regression Gates

Performance benchmarks run to detect regressions:

| Metric | Threshold | Alert |
|--------|-----------|-------|
| Startup P95 | 15% drift | 20% drift |
| Event Dispatch P95 | 15% drift | 20% drift |

## Emergency Override

In case of production incidents, policy strictness can be temporarily reduced:

1. Document the override with owner, reason, and expiry
2. Update environment-specific policy configuration
3. Revert after incident resolution

## Risk-to-Control Mapping

The full risk-to-control mapping for the security controls described in this
document lives in [`risk-control-matrix.md`](risk-control-matrix.md). It links
each risk (R-SEC-01..R-SEC-05) to its implementing code, automated test, and
CI gate.

| Risk ID | Control | Code | Test | CI Gate |
|---------|---------|------|------|---------|
| R-SEC-01 | Allowlist | [`packages/platform-core/src/modularity/policy/allowlist-policy/allowlist-policy-evaluator.ts`](../../packages/platform-core/src/modularity/policy/allowlist-policy/allowlist-policy-evaluator.ts:1) | [`packages/platform-core/tests/unit/modularity/policy/allowlist.policy.test.ts`](../../packages/platform-core/tests/unit/modularity/policy/allowlist.policy.test.ts:1) | `runtime-policy` ([`.github/workflows/policy-gates.yml`](../../.github/workflows/policy-gates.yml:1)) |
| R-SEC-02 | Integrity verifier + source loader checksum enforcement | [`packages/platform-core/src/security/verifiers/integrity.verifier.ts`](../../packages/platform-core/src/security/verifiers/integrity.verifier.ts:1), [`packages/platform-core/src/modularity/loader/sources/path.source.ts`](../../packages/platform-core/src/modularity/loader/sources/path.source.ts:1), [`packages/platform-core/src/modularity/loader/sources/url.source.ts`](../../packages/platform-core/src/modularity/loader/sources/url.source.ts:1), [`packages/platform-core/src/modularity/loader/sources/registry.source.ts`](../../packages/platform-core/src/modularity/loader/sources/registry.source.ts:1) | [`packages/platform-core/tests/unit/security/integrity.verifier.test.ts`](../../packages/platform-core/tests/unit/security/integrity.verifier.test.ts:1) | `runtime-policy` |
| R-SEC-03 | Secret redactor | [`packages/platform-core/src/security/redactors/secrets.redactor.ts`](../../packages/platform-core/src/security/redactors/secrets.redactor.ts:1) | [`packages/platform-core/tests/unit/security/secrets-redactor.test.ts`](../../packages/platform-core/tests/unit/security/secrets-redactor.test.ts:1) | `runtime-policy` |
| R-SEC-04 | Config access policy | [`packages/platform-core/src/modularity/policy/config-access-policy/config-access-policy-evaluator.ts`](../../packages/platform-core/src/modularity/policy/config-access-policy/config-access-policy-evaluator.ts:1), [`packages/platform-core/src/modularity/policy/config-access-policy/strategies/config-access-policy.strategy.ts`](../../packages/platform-core/src/modularity/policy/config-access-policy/strategies/config-access-policy.strategy.ts:1) | `packages/platform-core/tests/integration/config-access-matrix.test.ts`, `packages/platform-core/tests/integration/runtime-policy-validation.test.ts` | `runtime-policy` |
| R-SEC-05 | Deterministic lifecycle | bootstrap stages in [`packages/platform-core/src/bootstrap/stages/`](../../packages/platform-core/src/bootstrap/stages/) | `npm run test:lifecycle-determinism` | `FF-03 lifecycle determinism`

## Related Documents

- [ADR-0003: Module Loading Security](../../.context/02-architecture-design/adr/ADR-0003-module-loading-security-allowlist-integrity.md)
- [Risk Management](../../.context/02-architecture-design/06-risk-management.md)
- [Risk-to-Control Matrix](risk-control-matrix.md)
- [Regression Budgets](../performance/regression-budgets.md)
