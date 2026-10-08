# Red Team Attack Surface Map: AI Memory Vault

> **Status note (2026-10-07).** This is a point-in-time report. Its `VERIFIED_FIXED` / `FIXED + VERIFIED BY TEST` entries for the runtime-authority layer (B1/Gate 1, B2, B4/Gate 2, M01, M02, M03, M07, U02, U03 and the nonce-persistence part of M08) mean **hardened in the library, not yet wired into production**: `security/runtime_enforcer.py`, `runtime_adapter.py`, `memory_adapter.py`, `memory_boundary.py` and `security_update_manager.py` have no importer outside `security/` and the tests, and nothing runs with `production_mode=True`. The model-facing quarantine described here (M04/M05/B3) was also narrowed afterwards: an ACTIVE note keeps its content whatever its verification label, and an unverified REVIEW candidate is served flagged `unverified` by the controlled `memory_access` tools. The current classification is in [`AUDIT_REMEDIATION.md`](AUDIT_REMEDIATION.md).

**Generated**: 2026-10-05T02:26:00+03:00  
**Scope**: Full Repository Attack-Surface Enumeration (Authority, Memory, Evidence, CI/CD, Agent Interfaces)  
**Security Model**: Zero-Trust, Malicious-Agent Threat Model  

---

## 1. Attack Surface Matrix

| Surface | Entry Point | Trust Boundary | Attacker | Protected Asset | Enforcement Mechanism | Existing Tests | Red Team Status | Unknown / Blindspot |
|---|---|---|---|---|---|---|---|---|
| **Authority** | `ApprovalBroker.issue_approval()` | Agent Runtime vs. Owner Host | Compromised AI Agent / Supply Chain | Cryptographic Approval Tokens (`ApprovalToken`) | HMAC-SHA256 signature, Nonce replay store, Timestamp expiration | `test_audit_remediation.py::test_m01_*` | `VERIFIED_FIXED` | Subprocess memory inspection of parent environment |
| **Authority** | `RuntimeEnforcer.authorize()` | Execution Boundary | Agent attempting unapproved tool call | Host OS / Subprocess Execution (`tool_name`, `target`, `revision_id`) | Fail-closed policy, HMAC verification, single-use nonce consumption, strict param/revision hash binding | `test_runtime_enforcer.py`, `test_audit_remediation.py` | `VERIFIED_FIXED` | Tool parameters omitted from request (patched in U02) |
| **Authority** | `RuntimeAdapter.issue_approval()` | Agent Host API | Agent possessing adapter reference | Local Approval Signing Key | Prohibited in `production_mode=True` | `test_runtime_adapter_production_mode_forbids_self_approval` | `VERIFIED_FIXED` (U03) | Non-production dev mocks left enabled in prod configs |
| **Authority** | `03_IMPLEMENTATION/packages/interfaces/api_server.py` (`POST /api/v1/proposals/<id>/decision`) | HTTP REST API (port 8000) | Local network attacker / Web browser agent | Human Reviewer Attestation & Queue Promotion | **None** (No auth, CORS `*`, hardcoded `jarvis-human` reviewer) | `test_api_server.py` | `VULNERABLE` (U04) | Unauthenticated REST API bypasses all memory invariants |
| **Authority** | `03_IMPLEMENTATION/packages/interfaces/api_server.py` (`POST /api/v1/proposals/promote-approved`) | HTTP REST API (port 8000) | Unauthenticated caller | Promotion to `ACTIVE` lifecycle | **None** (Invokes `QueuePromoter(..., Principal.ADMIN)` directly) | `test_api_server.py` | `VULNERABLE` (U04) | Unauthenticated privilege escalation to `Principal.ADMIN` |
| **Authority** | `Windows NTFS Filesystem` | Host OS Permissions | Compromised agent process | Secret keys, vault configuration, core source | NTFS ACLs (`Install-MemoryVaultAgentBoundary.ps1`) | PR #207 (Open) | `UNVERIFIED` (U07) | DENY ACEs not deployed on current workstation; local agent has Full Control |
| **Memory** | `MemoryController.update()` | Memory Mutation Boundary | Agent with `Principal.AI_AGENT` | Settled `ACTIVE` canonical memories | `controller.py:1661` allows AI agents to mutate `ACTIVE` note content & tags | M3 empirical challenge tests explicitly permit AI agent update | `VULNERABLE` (U01) | Canonical memory content poisoning by AI agents |
| **Memory** | `MemoryController.propose()` | Memory Ingestion Boundary | Malicious / Hallucinating Agent | Vault Memory Integrity & Storage | Provenance check, Lifecycle forced to `{RAW, CLASSIFIED, NORMALIZED, REVIEW}`, Quarantine if untrusted | `test_security_hardening.py`, `test_m02_*` | `VERIFIED_FIXED` | Unbounded body size leading to memory exhaustion (U08) |
| **Memory** | `MemoryController.search()` | Retrieval Boundary | AI Agent / User | Context Pack & Quarantined Content | `Principal.AI_AGENT` filtered to exclude unverified/quarantined memory, progressive disclosure budget | `test_memory_boundary.py`, `test_context_pack_*` | `VERIFIED_FIXED` | Graph expansion 1-hop traversal across untrusted relations |
| **Memory** | `03_IMPLEMENTATION/packages/interfaces/api_server.py` (`GET /api/v1/search`) | REST Storage Retrieval | Unauthenticated caller | Quarantined / Sensitive Notes | Directly queries `self.storage` bypassing `MemoryController` | `test_api_server.py` | `VULNERABLE` (U04) | Bypasses quarantine, provenance filtering, and token budget |
| **Evidence** | `EvidenceEvaluator` / Benchmark Runners | Evaluation & Scoring | Biased / Adversarial Prompt | Benchmark Accuracy & Promotion Eligibility | Execution provenance required, tamper-evident hash chaining | `test_observed_memory_trace.py` | `TESTED_ONLY` | Synthetic benchmark traces generated via mock execution |
| **Evidence** | `03_IMPLEMENTATION/packages/lifecycle/policy.py:255` | Promotion Policy | Agent or Admin promoting note | Promotion Gate: Verification Requirement | `RESTORE_PROMOTE_VERIFICATION_GATE = False` skips verification check | None | `VULNERABLE` (U05) | Unverified notes promoted directly to `ACTIVE` |
| **CI/CD** | `.github/workflows/security-audit.yml` | GitHub Actions Runner | PR author / Compromised dependency | Build Artifacts & Security Verdicts | Token permissions `read`, PR checks fail-closed, PR comment bots | `test_workflow_security_audit.py` | `VERIFIED_FIXED` | Workflow triggers on untrusted fork pull requests |
| **CI/CD** | `requirements.txt` / Pip dependencies | Upstream Package Repositories (PyPI) | Malicious package maintainer / typosquat | Execution Environment Integrity | **None** (No SHA-256 hash pinning; 10/11 loose `>=` bounds) | None | `VULNERABLE` (U06) | Silent installation of compromised upstream wheel |
| **Agent Interfaces** | `recall_cli.py` | Terminal / Subprocess CLI | Local agent executing CLI | Memory Retrieval Boundary | Calls `MemoryController.search(Principal.AI_AGENT)` fail-closed | `test_adversarial_p0_p15_invariants.py` | `VERIFIED_FIXED` | CLI parameters passed via unquoted shell command line |
| **Agent Interfaces** | MCP Server `vault-memory` | Stdio IPC Protocol | MCP Client (Claude, Antigravity) | Canonical Memory Graph | MCP server wraps `MemoryController` as `Principal.AI_AGENT` | `test_memory_adapter.py` | `VERIFIED_FIXED` | Stdio stream injection / buffer overflow on large notes |

---

## 2. Threat Actor Profiles & Capabilities

1. **Untrusted LLM / Subagent**:
   - Capabilities: Can generate arbitrary Python code, inspect local git repositories, invoke exposed tool interfaces, submit memory proposals, and attempt to call internal APIs.
   - Objective: Exfiltrate secrets, persist poisoned instructions into active canonical memory, bypass approval prompts, downgrade audit findings.

2. **Compromised Supply Chain Dependency**:
   - Capabilities: Arbitrary code execution during `pip install` or package runtime import.
   - Objective: Inspect process environment (`os.environ`), read files in workspace, hook network sockets, patch Python built-ins.

3. **Local Unauthenticated Process / Network Peer**:
   - Capabilities: Can connect to localhost ports (`8000`), issue HTTP requests, exploit CORS wildcards.
   - Objective: Exploit `api_server.py` to trigger admin promotion, dump raw storage without authentication.
