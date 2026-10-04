# AI SECURITY UPDATE SERVICE

The security boundary uses a Windows-Update-like lifecycle for AI security
controls:

1. discover a new security catalog;
2. validate catalog schema;
3. verify the update signature;
4. verify package SHA-256;
5. stage and test the update;
6. install through a host-controlled installer;
7. advance the security-boundary version;
8. enforce a minimum version;
9. block protected operations after a mandatory deadline if the required
   security update is missing.

## Mandatory update semantics

A catalog can declare:

- version;
- severity;
- released_at;
- mandatory_after;
- min_runtime_version;
- package SHA-256;
- signature.

The runtime does not execute content from the catalog. The installer is an
injected host boundary and is responsible for applying a verified package.

When the installed security-boundary version is below the required version and
the mandatory deadline has passed, SecurityUpdatePolicy.enforce() raises
SecurityUpdateRequired. The runtime execution gate and memory adapter now call this policy before
protected operations. A stale security version therefore cannot be bypassed by
the agent's normal tool or memory paths.

## Trust model

The catalog itself is not authority merely because it was downloaded.

Production configuration must provide a cryptographic signature verifier whose
trusted public key is outside the update payload. The package hash must match
the signed manifest. Signature verification and hash verification are both
required before installation.

The repository's AI_SECURITY_UPDATE_CATALOG_URL variable is only a transport
location. It does not establish trust.

## Automatic monitoring

.github/workflows/ai-security-update-watch.yml checks the configured catalog
daily and on manual dispatch. It validates the schema and fails when the
catalog's minimum runtime version exceeds the repository's security-boundary
version.

Configure the repository variable:

AI_SECURITY_UPDATE_CATALOG_URL

The catalog should be generated and signed by a security-maintained release
pipeline. A missing URL currently produces a warning rather than a false
claim of protection.

## What counts as a security update

Examples include newly confirmed:

- prompt-injection bypass classes;
- tool poisoning/shadowing techniques;
- memory poisoning techniques;
- authorization/approval bypasses;
- data-exfiltration paths;
- supply-chain/tool-definition attacks;
- audit/telemetry integrity weaknesses;
- sandbox or egress-control bypasses;
- newly identified AI-agent attack techniques requiring policy changes.

The source material may come from trusted security advisories, standards bodies,
vendor advisories, internal red-team findings or validated incident reports.
External material is evidence, not executable authority.

OWASP explicitly recommends regression testing after material changes to prompts,
tools, memory, retrieval, policies or model providers, and release gates when
high-risk controls change. Its AI Agent Security guidance also calls for
monitoring, anomaly detection and structured audit trails.

NIST's 2026 discussion of robust AI security likewise emphasizes continuous
red-teaming, continuous hardening and operational resilience rather than
assuming a fixed set of guardrails remains sufficient indefinitely.

## Windows Update analogy

This is intentionally similar in lifecycle, not a copy of Windows Update:

Security Catalog -> Verify -> Stage -> Test -> Install -> Version Gate -> Audit

A critical difference is that AI security updates must not be allowed to
silently redefine their own trust root or bypass the runtime boundary. The
update mechanism itself is part of the security boundary.

## Production hardening

For production, use:

- an external signed catalog;
- a pinned update-signing public key;
- durable audit storage outside the agent process;
- staged/canary rollout;
- automatic rollback for failed health checks;
- offline emergency revocation/kill switch;
- explicit retention and incident-response policy;
- CI adversarial regression tests for every security update.

The current implementation supplies the policy and host seams; it does not
pretend that an external catalog, signing infrastructure or installer exists
until the deployment configures those components.


## Catalog provenance policy

Catalog provenance is evaluated independently of package content.

The deployment may configure a hard block list for countries or jurisdictions
that it does not permit as security-update origins. The current repository
configuration lists Russia, China, India and North Korea as blocked origins,
per the deployment policy requested for this system.

This is deliberately modeled as a local security policy rather than as a
technical claim that every artifact from those locations is malicious. All
other origins still require the same cryptographic integrity, signer,
provenance and adversarial validation controls. A non-blocked origin is never
automatically trusted.

The policy also supports signer allowlisting. An unknown signer is REVIEW and a
missing signer is BLOCKED. Geographic origin cannot override a cryptographic
failure or an untrusted signer.

This layered approach follows OWASP guidance to maintain allowlists of trusted
sources, verify provenance and integrity, and avoid trusting external
content merely because it appears legitimate.