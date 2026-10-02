# Runtime Security Boundary

PR #204 adds a security core that protects program construction and persistent memory without freezing normal development.

## Design rule

External content is data, never authority. The model may propose a change, but a deterministic policy layer decides whether that proposal can become an execution.

## Runtime flow

1. Acquire external content as data.
2. Preserve provenance and content SHA-256.
3. Scan prompts, Markdown, scripts, code and metadata.
4. Assign UNTRUSTED, REVIEW, TRUSTED or BLOCKED.
5. Resolve a scoped capability for the actor, tool and target.
6. For sensitive actions, require a short-lived approval bound to actor, tool, target and normalized-parameter SHA-256.
7. Reject expired and replayed approvals.
8. Verify the pinned tool definition before execution.
9. Execute only through a runtime adapter that re-checks authorization immediately before the side effect.
10. Emit metadata-only security events for forensic correlation.

## Memory protection

The MemoryLedger is an append-only integrity layer:

- each record has a monotonically increasing version;
- each record contains the previous record SHA-256;
- record content is canonically hashed;
- rewrite/delete operations are rejected;
- UNTRUSTED and BLOCKED proposals are never committed;
- REVIEW proposals require explicit approval;
- verification detects history tampering.

This layer is intentionally independent of any particular database or vector store. Existing memory implementations can place it in front of their write path without changing retrieval behavior.

## Tool protection

ToolDefinition is canonically serialized and SHA-256 pinned. A changed description, schema, server identity or tool name fails verification and must not silently inherit prior trust.

Capabilities are scoped to actor, tool, target scope and expiry.

The runtime enforcer is fail-closed for blocked/untrusted content and sensitive actions.

## What this does not claim

The security core does not magically control a host that bypasses it. A real agent runtime must route every tool execution and memory write through these boundaries. Direct calls to a tool provider or storage backend remain outside this module's control until the host integrates the adapter.

This distinction is deliberate: the security layer is portable and testable, while host-specific execution remains an integration boundary.

## Why this preserves productivity

Normal development remains allowed:

- trusted read-only actions can proceed without an approval prompt;
- projects and memory can keep evolving;
- new content can be proposed without becoming policy;
- only privileged transitions are gated;
- rejected content is preserved as evidence rather than silently deleted.

The result is a quarantine-and-authorization model, not a read-only development model.
