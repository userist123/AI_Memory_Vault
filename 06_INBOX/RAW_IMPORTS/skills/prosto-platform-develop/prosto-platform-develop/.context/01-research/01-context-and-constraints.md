# 01 - Context And Constraints

Date: 2026-03-23

## Studied Documentation Summary
- `README.md`: platform intent is "headless + expandable plugins".
- `AGENTS.md`: strict TypeScript, micro-core architecture, SOLID, testing, security, performance.
- `.cursor/rules/*.md`: reinforces strict typing, modular boundaries, test strategy, and low coupling.
- `package.json`: ESM project, `build/dev/typecheck` scripts, minimal dependencies, Node `>=22`.
- `.editorconfig`: 2-space indentation, LF, line-length policy.

## Non-Negotiable Constraints
- Language: TypeScript with strict mode.
- Architecture: minimal micro-core, plugin-first expansion.
- Quality: high testability, explicit contracts, and clear module boundaries.
- Security: input validation, secure defaults, and safe plugin loading.
- Performance: avoid unnecessary abstractions and unnecessary third-party dependencies.

## Observations About Current Repository State
- There is no implementation code yet (`src/` is absent).
- This is the best stage to define:
  - core public contracts
  - plugin lifecycle
  - compatibility/versioning policy
  - module loading security model

## Design Implications
- If contracts are weak now, plugin ecosystem quality will degrade quickly.
- If core owns too many dependencies, future extension will become costly.
- If module loading is too dynamic by default, operational and security risks increase.

## Guiding Architectural Position
- Core should provide only:
  - lifecycle orchestration
  - dependency registration/lookup
  - event and hook system
  - configuration and capability negotiation
- All transport, storage, and integration concerns should be adapters/plugins.

Continue with: [02 - Micro-Core Architecture](./02-micro-core-architecture.md).
