# AUDIT TRAIL

## Purpose

Security enforcement is only useful for incident response if the system can
reconstruct what happened. The security layer therefore maintains an optional
tamper-evident, metadata-only audit trail.

The trail records lifecycle events for:

- tool registration;
- tool request admission;
- authorization decisions;
- approval identifiers;
- tool execution failures;
- tool response validation;
- memory write denials;
- memory persistence;
- memory commit/commit denial;
- backend failures.

This follows the security principle that agent decisions, tool calls, outcomes
and security-relevant events need traceability, while sensitive prompts,
credentials and raw payloads should not be copied into logs. OWASP recommends
maintaining audit trails and structured decision metadata for high-risk agent
actions.

## What is recorded

Each AuditRecord contains:

- event type and outcome;
- actor;
- correlation ID;
- UTC timestamp;
- trust state where applicable;
- tool name where applicable;
- SHA-256 of target/resource;
- SHA-256 of normalized tool parameters;
- SHA-256 of memory/tool payloads;
- approval ID where applicable;
- authorization/scanner reason;
- exception type for failures, never the exception message;
- previous record SHA-256;
- current record SHA-256.

Raw prompts, tokens, passwords, cookies, tool parameters, memory contents and
exception messages are deliberately excluded.

## Chain integrity

Records are append-only through AuditTrail.record(). Every record commits the
hash of the preceding record. AuditTrail.verify() detects modified records,
deleted records, reordered records, or broken links.

This is tamper evidence, not a claim of non-repudiation. The process or host
holding the in-memory trail can still destroy it. Production deployments should
ship the exported audit stream to infrastructure outside the agent's control,
with restricted write/delete access and appropriate retention.

## Correlation

Runtime tool requests carry a correlation_id. Every audit record generated
for that execution uses the same identifier, allowing reconstruction of:

TOOL_REQUEST -> TOOL_AUTHORIZATION -> TOOL_RESPONSE

Memory writes similarly produce a correlation identifier covering:

MEMORY_WRITE_DENIED

or:

MEMORY_PERSIST -> MEMORY_COMMIT

The host may provide a correlation ID when entering the memory adapter so a
larger user/session/request trace can span retrieval, model decisions, tools
and memory.

## Failures

Audit failures must not become a covert bypass. Security enforcement remains
the authority. The audit implementation must never be used to decide whether
a tool or memory write is allowed.

If the production host requires guaranteed audit delivery, the in-memory
AuditTrail should be replaced or bridged to a durable append-only sink
before execution is considered complete.

## Operational monitoring

The audit stream is designed to feed LogAnalyzer/SIEM-style consumers.
Useful detections include:

- repeated approval_required or approval_parameter_mismatch;
- repeated blocked/untrusted requests;
- tool-definition mismatch or pin failures;
- sudden increases in tool calls;
- repeated tool-output injection detections;
- memory write denials or backend failures;
- broken audit-chain verification;
- unexpected new tools or changed tool definitions.

Audit records are metadata-only and should remain separate from ordinary
debug/application logs.
