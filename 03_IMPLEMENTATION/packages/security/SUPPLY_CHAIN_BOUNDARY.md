# Software and AI Supply-Chain Boundary

All externally sourced executable or executable-adjacent components MUST pass
the common supply-chain policy before they are admitted to the runtime.

Covered component classes:

- software
- libraries/packages
- MCP servers
- AI models
- AI skills
- plugins
- catalogs/security updates
- containers
- scripts
- tools

## Default deployment blacklist

The default local deployment policy blocks components whose declared origin
country is:

- Russia
- China
- India
- North Korea

The same rule can be extended with jurisdiction identifiers.

## Important distinction

A non-blocked country is NOT a trust decision. Every component still requires
provenance, artifact SHA-256, signer verification, and applicable static,
behavioral, sandbox and adversarial checks.

Country/jurisdiction is a deployment policy signal. It is not treated as
technical proof that an artifact is malicious.

## Enforcement points

The policy is enforced at tool registration when the runtime adapter is
configured with SoftwareAISupplyChainPolicy. Security-update installation has
its own provenance gate. Host integrations must pass provenance before
admitting models, skills, plugins, MCP servers, packages or other executable
components.

No component should be admitted merely because it came through a different
transport, mirror, CDN or URL.

## Fail-closed requirements

Missing signer or missing artifact hash is BLOCKED.

An unallowlisted signer is REVIEW when signer allowlisting is configured.

A blocked origin cannot be overridden by a valid package hash, a valid
signature, human approval, or a newer version.
