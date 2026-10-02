# Security Event Contract for LogAnalyzer

This contract defines the metadata-only event stream produced by the Memory Vault security boundary and consumed by forensic tooling such as LogAnalyzer.UI.

## Trust boundary

The agent-side security layer is the policy authority for execution. LogAnalyzer is the forensic consumer and must not be treated as the execution authority.

External content remains DATA, not authority.

## Event shape

- `schema_version`: integer, currently `1`.
- `timestamp`: UTC ISO-8601 string.
- `event_type`: stable event name.
- `source`: component producing the event.
- `actor`: agent or service identifier.
- `correlation_id`: identifier shared by related acquisition, scan, trust and tool events.
- `trust_state`: `UNTRUSTED`, `REVIEW`, `TRUSTED`, or `BLOCKED`, when a trust decision exists.
- `scanner_verdict`: scanner result when available.
- `tool`: optional object containing tool name, allow/deny result and policy reason.
- `artifact`: optional object containing the artifact SHA-256.
- `metadata`: allowlisted provenance/evidence metadata only.

Raw prompts, cookies, session tokens, API keys, passwords, browser databases and arbitrary command output are not event payloads.

## Event types

| Event | Meaning |
|---|---|
| `EXTERNAL_CONTENT_IMPORTED` | External material was acquired as data. |
| `SCAN_COMPLETED` | Static security analysis completed. |
| `TRUST_DECISION` | A trust state was assigned. |
| `TOOL_REQUESTED` | A tool request reached the authorization boundary. |
| `TOOL_ALLOWED` | The policy boundary allowed the tool request. |
| `TOOL_BLOCKED` | The policy boundary denied the tool request. |
| `EXECUTION_STARTED` | An already-authorized execution began. |
| `EXECUTION_FINISHED` | An already-authorized execution completed. |
| `IOC_OBSERVED` | A forensic IOC was observed or correlated. |

## Correlation

A single external artifact should retain one `correlation_id` from acquisition through scan, trust decision and tool authorization. Runtime evidence can attach the same correlation ID when the execution layer supports it.

The artifact SHA-256 is the stable content reference. Source URL, repository, path, commit, verification type and confidence may be retained as provenance metadata.

## LogAnalyzer mapping

The contract is deliberately independent of the WPF application. LogAnalyzer.UI can ingest the events as forensic telemetry and correlate them with:

- evidence SHA-256 values;
- append-only chain-of-custody entries;
- IOC records;
- Windows event timelines;
- process/network observations;
- incident correlation IDs.

The existing LogAnalyzer `EvidenceIntakeService` and `ChainOfCustodyService` remain the evidence authority for imported forensic files. This event stream records the agent-side security decision that led to an action; it does not replace chain of custody.

## Security invariants

1. A tool request marked as originating from `external_content` is never an authorization source.
2. `BLOCKED` content cannot be promoted by human approval through the tool authorization API.
3. `UNTRUSTED` content cannot authorize a tool.
4. `REVIEW` content requires explicit human approval before execution.
5. Side effects and data export require explicit human approval even for `TRUSTED` content.
6. Security events contain metadata, not secrets or raw external instructions.
