# Architecture Guidelines

## Project Overview

- **Project Type**: Headless platform, expandable with plug-in modules
- **Language**: TypeScript
- **Architecture**: Micro-core architecture (headless design)
- **Purpose**: Provide a flexible platform for building applications

## Micro-core Architecture

- Maintain a minimal platform core with expansion through plug-in modules
- Use clear module boundaries and interfaces
- Ensure modules are independently testable and deployable
- Document module APIs and dependencies
- Follow consistent naming conventions across modules

## Core Design Principles

- **Object-Oriented Design**: Prefer object-oriented composition for production code when it improves clarity, extensibility, and testability
- **Clean Architecture**: Keep domain policies independent from framework and infrastructure details
- **SOLID**: Apply all SOLID principles explicitly in design and refactoring
- **Single Responsibility**: Each module should have one reason to change
- **Loose Coupling**: Minimize dependencies between modules
- **High Cohesion**: Related functionality should be grouped together
- **Dependency Injection**: Use DI for better testability and flexibility
- **Interface Segregation**: Define small, focused interfaces

## Package Boundary Rules (ADR-0001)

### `platform-core` MUST:
- Only own lifecycle orchestration, service registry, event/hook bus, configuration validation, module loading, and compatibility checks
- Remain minimal and long-lived
- Import only from `platform-sdk` and vetted runtime libraries

### `platform-core` MUST NOT:
- Import from adapter packages
- Import feature modules
- Own HTTP framework specifics
- Own ORM/persistence specifics
- Own vendor integrations
- Own feature domain logic

### `platform-sdk` MUST:
- Keep external runtime dependencies minimal and justified
- Prefer TypeScript and platform-native APIs
- Only export contracts, types, interfaces, tokens, and validation primitives

### `platform-sdk` MUST NOT:
- Depend on other platform runtime packages
- Own full contract conformance test suites (that's `platform-contract-tests`)

### Adapters MAY:
- Depend on `platform-sdk`
- Depend on framework-specific libraries (Fastify, Express, etc.)

### Adapters MUST NOT:
- Depend on other adapters' internals
- Depend on feature modules
- Export framework-specific types in public API

### Modules MUST:
- Only import from `platform-sdk` in their public API
- Declare compatibility metadata in manifest

### Modules MUST NOT:
- Import from `platform-core` internals
- Import from other modules' internals
- Have side effects at import time

## Current vs Target State

- Keep a hard distinction between current-state repository and target-state architecture
- `.context/02-architecture-design/*` documents planned micro-core boundaries, not implemented package code in this repo
- Treat architecture docs as target constraint for future implementation planning
- Architecture docs assume separate module repositories for feature modules and a separate admin shell repository

## Security Model (ADR-0003)

- Draft security model requires production module loading by explicit allowlist plus artifact integrity verification and module trust classification (`trusted`, `internal`, `third-party-reviewed`)

## Contract Authority Split

- `platform-sdk` owns contracts
- `platform-admin-contracts` owns admin contracts
- `platform-contract-tests` owns full conformance suites
- Avoid merging these responsibilities

## Admin UI Model (ADR-0009)

- Hybrid separation: separate `admin-shell`, contract package, and `platform-adapter-admin-bff`
- No UI runtime in `platform-core`

## Kernel Component Model

- Pre-start acyclic dependency resolution
- Structured lifecycle error mapping fields (`moduleId`, `phase`, `errorCode`, `remediationHint`)

## Error Handling Strategy

```typescript
// Custom error classes
class ValidationError extends Error {
  constructor(message: string, public field: string) {
    super(message);
    this.name = 'ValidationError';
  }
}

// Proper error handling
async function processUser(userData: unknown): Promise<User> {
  if (!isValidUser(userData)) {
    throw new ValidationError('Invalid user data', 'userData');
  }
  
  try {
    return await userService.create(userData);
  } catch (error) {
    logger.error('Failed to create user', error);
    throw new ServiceError('User creation failed', error);
  }
}
```

## Implementation Constraints

- Require object-oriented composition, Clean Architecture dependency direction, and SOLID-driven decomposition as first-class design constraints
