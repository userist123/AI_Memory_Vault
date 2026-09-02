# Security-First Development Rules

## Module Loading Security (ADR-0003)

### Production Module Loading

**MANDATORY for production:**

1. **Allowlist-only loading** - Only explicitly approved modules can load
2. **Integrity verification** - Checksum/signature validation before load
3. **Manifest validation** - Schema validation against versioned contract
4. **Security classification** - Every module must have security class

### Security Classification

```typescript
type SecurityClassType = 
  | 'trusted'               // Core platform modules, full access
  | 'internal'              // Internal team modules, standard access
  | 'third-party-reviewed'; // External modules, reviewed and approved
```

### Module Manifest Requirements

```typescript
interface IModuleManifest {
  id: string;
  version: string;
  platformVersion: string;
  
  // Security classification (MANDATORY)
  securityClass: SecurityClassType;
  
  // Criticality for startup policy
  criticality: 'critical' | 'standard' | 'optional';
  
  // Integrity metadata
  checksum?: string;        // SHA-256 of artifact
  signature?: string;       // Signed checksum for production
  
  // Dependencies and capabilities
  dependencies: string[];
  capabilities: string[];
}
```

### Allowlist Configuration

```typescript
// Production config example
const moduleAllowlist = [
  {
    id: 'prosto-module-health',
    version: '^1.0.0',
    checksum: 'sha256:abc123...',
    securityClass: 'internal'
  },
  {
    id: 'prosto-module-auth',
    version: '^1.2.0',
    checksum: 'sha256:def456...',
    securityClass: 'trusted'
  }
];

// Reject any module not in allowlist for production
if (process.env.NODE_ENV === 'production' && !isInAllowlist(module)) {
  throw new SecurityError('Module not in production allowlist', { moduleId: module.id });
}
```

---

## Input Validation

### Boundary Validation with Zod

**ALL external inputs MUST be validated:**

```typescript
import { z } from 'zod';

// HTTP request validation
const CreateModuleSchema = z.object({
  id: z.string().regex(/^[a-z][a-z0-9-]*$/),
  version: z.string().regex(/^\d+\.\d+\.\d+$/),
  config: z.record(z.unknown()).optional()
});

// Validate at boundary
function handleCreateModule(req: Request): Module {
  const validated = CreateModuleSchema.parse(req.body);
  // Now type-safe to use
  return moduleService.create(validated);
}
```

### Validation Points

**Validate at:**
- HTTP request boundaries (adapters)
- Queue message handlers
- CLI input parsing
- Webhook receivers
- Configuration loading
- Module manifest loading

**Never trust:**
- Module-provided data without validation
- User input from any source
- Environment variables without schema validation
- Data from external systems

---

## Secret Management

### Secret Redaction

```typescript
// ✅ Good: Redact secrets from logs
const logger = pino({
  redact: {
    paths: ['*.password', '*.secret', '*.apiKey', '*.token'],
    remove: true
  }
});

// ❌ Bad: Log sensitive data
logger.info({ config }, 'Loading configuration'); // May expose secrets
```

### Environment Variables

```typescript
// Validate environment variables with schema
const EnvSchema = z.object({
  NODE_ENV: z.enum(['development', 'production', 'test']),
  DATABASE_URL: z.string().url(),
  API_KEY: z.string().min(1),
  PORT: z.string().transform(Number)
});

const env = EnvSchema.parse(process.env);
```

### Secret Storage

**NEVER:**
- Commit secrets to version control
- Hardcode API keys in source code
- Log sensitive configuration values
- Pass secrets in query parameters

**ALWAYS:**
- Use environment variables or secret manager
- Redact secrets from logs and diagnostics
- Rotate secrets regularly
- Use separate secrets per environment

---

## Dependency Security

### Lockfile Discipline

```bash
# ALWAYS commit lockfile
git add package-lock.json

# NEVER bypass lockfile
npm ci  # Use in CI, not npm install
```

### Vulnerability Scanning

```bash
# Regular security audits
npm audit

# Fail CI on critical vulnerabilities
npm audit --audit-level=critical
```

### Minimal Dependency Footprint

**For `platform-sdk`:**
- Justify every dependency
- Prefer native Node.js APIs
- Consider if dependency can be in consumer packages

**For `platform-core`:**
- No framework dependencies in kernel path
- Vet all dependencies for security
- Track dependency licenses

---

## Module Trust Model

### Trust Classes

| Class | Description | Requirements | Access Level |
|-------|-------------|--------------|--------------|
| `trusted` | Core platform modules | Full security review, signed artifacts | Full platform access |
| `internal` | Internal team modules | Code review, integrity check | Standard platform APIs |
| `third-party-reviewed` | External modules | Security review, contract tests | Sandboxed access |

### Module Onboarding Checklist

```markdown
## Module Security Review

### Required for all modules
- [ ] Manifest validates against schema
- [ ] Integrity checksum provided
- [ ] Security classification declared
- [ ] Dependencies scanned for vulnerabilities
- [ ] Contract tests pass

### Additional for `trusted` class
- [ ] Full security audit completed
- [ ] Artifact signed by platform team
- [ ] Source code reviewed
- [ ] No critical/high vulnerabilities

### Additional for `third-party-reviewed`
- [ ] Third-party security review report
- [ ] Sandboxed execution verified
- [ ] Network access restricted
- [ ] File system access limited
```

---

## API Security

### Authentication & Authorization

```typescript
// Token-based access control
interface IAuthContext {
  userId: string;
  roles: string[];
  permissions: Set<string>;
}

// Check permissions before operation
async function deleteModule(moduleId: string, ctx: IAuthContext): Promise<void> {
  if (!ctx.permissions.has('module:delete')) {
    throw new ForbiddenError('Missing permission: module:delete');
  }
  await moduleRepository.delete(moduleId);
}
```

### Rate Limiting

```typescript
// Implement rate limiting for external APIs
const rateLimiter = {
  windowMs: 60 * 1000, // 1 minute
  max: 100, // 100 requests per minute
  message: 'Too many requests'
};
```

### Input Sanitization

```typescript
// Sanitize user input to prevent injection
import DOMPurify from 'isomorphic-dompurify';

function sanitizeInput(input: string): string {
  return DOMPurify.sanitize(input, {
    ALLOWED_TAGS: [], // Strip all HTML
    ALLOWED_ATTR: []
  });
}
```

---

## Security Error Handling

### Structured Security Errors

```typescript
class SecurityError extends Error {
  constructor(
    message: string,
    public readonly code: string,
    public readonly details: Record<string, unknown>
  ) {
    super(message);
    this.name = 'SecurityError';
  }
}

// Usage
throw new SecurityError(
  'Module integrity check failed',
  'MODULE_INTEGRITY_FAILURE',
  { moduleId, expectedChecksum, actualChecksum }
);
```

### Information Leakage Prevention

```typescript
// ❌ Bad: Expose internal details
catch (error) {
  res.status(500).json({
    error: error.message, // May expose sensitive info
    stack: error.stack
  });
}

// ✅ Good: Sanitized error response
catch (error) {
  logger.error({ error }, 'Module load failed');
  res.status(500).json({
    error: 'Internal server error',
    correlationId: generateCorrelationId()
  });
}
```

---

## Security Logging

### Audit Logging

```typescript
// Log security-relevant events
logger.info({
  event: 'MODULE_LOADED',
  moduleId: module.id,
  moduleVersion: module.version,
  securityClass: module.securityClass,
  timestamp: new Date().toISOString()
});

logger.warn({
  event: 'MODULE_LOAD_FAILED',
  moduleId: attemptedModuleId,
  reason: 'ALLOWLIST_REJECTED',
  timestamp: new Date().toISOString()
});
```

### Security Event Types

**MANDATORY to log:**
- Module load/unload events
- Authentication failures
- Authorization denials
- Integrity check failures
- Allowlist rejections
- Configuration validation failures

---

## Related Documents

- [ADR-0003 Module Loading Security](../../.context/02-architecture-design/adr/ADR-0003-module-loading-security-allowlist-integrity.md)
- [06 Phase - Security Controls](../../.context/04-implementation-plan/06-phase.md)
- [05 Quality Security Performance](../../.context/01-research/05-quality-security-performance.md)
