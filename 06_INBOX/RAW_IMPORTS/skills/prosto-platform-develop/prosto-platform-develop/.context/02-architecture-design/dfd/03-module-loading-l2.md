# DFD-03 Module Loading Level (L2)

Date: 2026-03-24  
Scope: Detailed load pipeline for module activation

## Purpose
Provide a fine-grained data flow for discovery, verification, compatibility checks, and lifecycle transition into active state.

## DFD L2

```mermaid
flowchart TD
  E1["Operator Config Input"]
  D1[("D1: Allowlist + Policy Config")]
  D2[("D2: Package Registry")]
  D3[("D3: Module Catalog (security/support metadata)")]
  D4[("D4: Artifact Cache")]
  D5[("D5: Validated Manifest Set")]
  D6[("D6: Compatibility Results")]
  D7[("D7: Dependency Graph")]
  D8[("D8: Runtime Module State")]
  D9[("D9: Diagnostics")]

  P21(("P2.1 Parse Module Config"))
  P22(("P2.2 Resolve Artifacts"))
  P23(("P2.3 Verify Integrity"))
  P24(("P2.4 Validate Manifest Schema"))
  P25(("P2.5 Check Compatibility"))
  P26(("P2.6 Resolve Dependencies"))
  P27(("P2.7 Execute register/init/start"))
  P28(("P2.8 Apply Startup Policy"))
  P29(("P2.9 Emit Startup Report"))

  E1 --> P21
  P21 --> D1
  D1 --> P22

  P22 -->|"artifact request"| D2
  D2 -->|"artifact + metadata"| P22
  P22 --> D4
  D4 --> P23

  P23 --> D3
  D3 --> P23
  P23 -->|"verified artifact"| P24
  P23 -->|"integrity failure"| P28

  P24 --> D5
  P24 -->|"schema failure"| P28

  D5 --> P25
  P25 --> D6
  P25 -->|"compatibility failure"| P28

  D6 --> P26
  P26 --> D7
  P26 -->|"cycle/dependency failure"| P28

  D7 --> P27
  P27 --> D8
  P27 -->|"lifecycle failure"| P28

  P28 --> D8
  P28 --> D9
  D8 --> P29
  D9 --> P29
```

## Step-Level Controls

| Step | Control Objective | Failure Outcome |
|---|---|---|
| P2.1 Parse Module Config | Ensure only configured modules are candidates | Invalid config blocks startup |
| P2.2 Resolve Artifacts | Fetch immutable versioned artifacts | Missing artifact routed to policy evaluator |
| P2.3 Verify Integrity | Ensure artifact authenticity/trust | Reject module, raise security diagnostics |
| P2.4 Validate Manifest Schema | Contract consistency and required metadata | Reject module with schema errors |
| P2.5 Check Compatibility | Prevent runtime/API mismatch | Reject incompatible module |
| P2.6 Resolve Dependencies | Ensure valid acyclic initialization order | Reject or block startup per policy |
| P2.7 Execute Lifecycle | Controlled activation of approved modules | Failure isolated and escalated to policy |
| P2.8 Apply Startup Policy | Determine continue vs abort decision | `strict`: abort, `best-effort`: skip non-critical |
| P2.9 Emit Startup Report | Operator visibility and audit trail | Startup fails closed if report pipeline is critical |

## Security Enforcement Points
- Integrity verification before schema compatibility checks.
- Mandatory security classification and compatibility metadata.
- Block module if trust metadata is absent in production mode.

## Linked Decisions
- [ADR-0003 Module Loading Security](../adr/ADR-0003-module-loading-security-allowlist-integrity.md)
- [ADR-0004 Lifecycle Policies](../adr/ADR-0004-lifecycle-orchestration-and-startup-policies.md)
- [ADR-0006 Module Distribution Model](../adr/ADR-0006-external-module-repository-and-distribution-model.md)

## Linked Sequences
- [SEQ-01 Bootstrap Lifecycle](../sequence/01-bootstrap-lifecycle.md)
- [SEQ-04 Critical Module Failure](../sequence/04-critical-module-failure.md)

