# ADR-0006: External Module Repository And Distribution Model

Date: 2026-03-24  
Status: Draft

## Context
Platform architecture is plugin-first and explicitly supports module development in independent repositories. Without a unified distribution model, runtime reproducibility and governance degrade due to inconsistent packaging, incompatible release practices, and weak traceability of module provenance.

The architecture package already defines immutable artifacts, compatibility metadata, and operational catalog expectations for modules consumed by runtime.

## Decision
Adopt a repository and distribution model centered on versioned package artifacts and catalog governance:
- Modules are developed in independent repositories with one module per repository as the default model.
- Production runtime consumes only immutable versioned artifacts from package registries (npm or GitHub Packages).
- Production runtime must not load modules directly from raw Git URLs.
- Module manifest metadata is mandatory and includes identity, version, platform compatibility range, dependency metadata, and security classification.
- A central module catalog is required to track stable version, compatibility matrix, support status, and security review status.
- Module repositories must enforce CI gates for manifest schema validation, contract tests, and tagged-release publishing policy.

## Consequences

### Positive
- Predictable and reproducible runtime composition based on immutable artifacts.
- Decoupled module release cadence without violating platform compatibility rules.
- Stronger governance via centralized compatibility and security metadata.
- Better incident triage through explicit module provenance and support status.

### Negative
- Additional operational overhead to maintain catalog and compatibility metadata.
- Higher process discipline required for module release and tagging.
- Slower ad hoc experimentation in production-like environments due to stricter distribution rules.

## Alternatives Considered
- Keep all modules in the platform monorepo: rejected due to reduced ecosystem scalability and tighter coupling of release cadence.
- Load modules from Git references at runtime in production: rejected due to integrity, reproducibility, and auditability risks.
- Artifact-only distribution without central catalog: rejected due to weak governance of compatibility/support/security status.

## Related Artifacts
- [02 Domain And Capability Model](../02-domain-and-capability-model.md)
- [C4-04 Deployment View](../c4/04-deployment-view.md)
- [DFD-03 Module Loading L2](../dfd/03-module-loading-l2.md)
- [ADR-0002 SDK Contract And Semver Governance](./ADR-0002-sdk-contract-and-semver-governance.md)
- [ADR-0003 Module Loading Security (Allowlist + Integrity)](./ADR-0003-module-loading-security-allowlist-integrity.md)
