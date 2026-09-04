# TypeScript Guidelines

## Configuration

- Use strict TypeScript configuration
- Enable all strict type checking options
- Configure proper module resolution
- Set up path mapping for cleaner imports
- Use ESLint for consistent code style (see `eslint.config.mjs`)
- Use Prettier for code formatting (see `.prettierrc.json`)
- Root package is ESM (`"type": "module"` in `package.json`), use `.js` file extensions in relative imports

## Type Definitions

- Always provide explicit type annotations for public APIs
- Use interfaces for object shapes and classes for implementations
- Prefer union types over `any` when possible
- Use generic types for reusable components
- Implement proper type guards for runtime type checking
- Use proper abstraction layers

## Import/Export Strategy

```typescript
// ✅ Good: Organized imports with ESM (.js extensions for relative paths)
import type { IModuleContext } from '@prosto/platform-sdk';
import { UserService } from './services/user.service.js';
import { Logger } from './utils/logger.js';
import { Database } from './database.js';

// Barrel exports for clean module interfaces
export type * from './types.js';
export * from './services.js';
export * from './utils.js';

// ✅ Good: Named imports for better tree-shaking
import { ServiceRegistry } from '@prosto/platform-core';
```

### Import Organization Order

```typescript
// 1. Node.js built-in modules
import { EventEmitter } from 'node:events';

// 2. Third-party dependencies
import { FastifyInstance } from 'fastify';
import { z } from 'zod';

// 3. Platform SDK (contract package)
import { PlatformModule, LifecyclePhase } from '@prosto/platform-sdk';

// 4. Platform core (runtime)
import { ServiceRegistry } from '@prosto/platform-core';

// 5. Same-package imports (relative)
import { User } from '../types/user.types';
import { UserService } from './services/user.service';
```

### Cross-Package Imports

```typescript
// ✅ Good: Using @prosto/* scoped imports for cross-package
import { PlatformModule } from '@prosto/platform-sdk';
import { ModuleLifecycleOrchestrator } from '@prosto/platform-core';

// ❌ Bad: Direct cross-package relative imports
import { PlatformModule } from '../../platform-sdk/src/types';

// ❌ Bad: Module-to-module imports
import { AuthModule } from '@prosto/platform-module-auth';
```

## Error Handling

- Use custom error classes that extend Error
- Implement proper error types with meaningful messages
- Use try-catch blocks for async operations
- Validate input parameters with proper type guards
- Implement proper error handling at module boundaries
- Add meaningful comments for complex logic

## Performance Considerations

- Use `readonly` modifiers for immutable properties
- Implement proper memoization for expensive calculations
- Avoid unnecessary object creation in hot paths
- Use proper data structures for specific use cases
- Optimize critical paths for performance
- Use lazy loading for non-critical features
- Implement pagination for large datasets

## Code Organization

- Group related functionality in modules
- Use namespaces or folders for logical separation
- Prefer object-oriented composition for production code when it improves maintainability and testability
- Implement proper abstraction layers
- Follow Clean Architecture boundaries between domain and infrastructure concerns
- Follow SOLID principles
- Maintain weak component coupling
- Design for extensibility and testability
