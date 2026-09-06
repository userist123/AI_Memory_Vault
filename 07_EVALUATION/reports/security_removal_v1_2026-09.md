# Security Removal V1 — Unsafe Active Skills Deletion

**Execution Date**: 2026-09-03  
**Authority**: Cognitive Security Core & Vault Cognitive Invariants P0-P18  
**Starting Commit**: `3a257c8d4d46970587b718d7b828a33ee25bc1d2`  
**Phase**: `SECURITY_REMOVAL_V1`  
**Status**: `ACTIVE_VAULT_CLEARED_CRITICAL_ZERO`  

---

## 1. Executive Summary & Required Invariant Metrics

```text
INSTALLED_BEFORE=3450
CRITICAL_REMOVED=2
HIGH_REMOVED=0
DEFENDER_CONFIRMED=0
TOTAL_REMOVED=2
INSTALLED_AFTER=3448

RAW_CORPUS_MODIFIED=NO
PROVENANCE_LOST=NO
DEFENDER_BYPASSED=NO
```

> **Note on directory counts**: Installed canonical skills decreased from `3,450` to `3,448`. Total directories under `.agents/skills/` (including 253 native skills) decreased from `3,703` to `3,701`.

---

## 2. Removed Active Skills (Forensic Registry)

| Skill Name | Security Risk | Removal Category | Source Repository | Files | Removal Reason |
| :--- | :---: | :---: | :---: | :---: | :--- |
| `sandbase-mcp` | **CRITICAL** | `REMOTE_CODE_EXECUTION` | `repo:agentic-awesome-skills` | 2 | CRITICAL security risk: dynamic unverified remote execution pattern. Prohibited in active vault by Security Policy V1. |
| `aspire` | **CRITICAL** | `REMOTE_CODE_EXECUTION` | `repo:awesome-copilot` | 11 | CRITICAL security risk: dynamic unverified remote execution pattern. Prohibited in active vault by Security Policy V1. |

### Forensic Details per Removed Skill

#### `sandbase-mcp`
- **Skill ID**: `skill:agentic-awesome-skills-main:sandbase-mcp`
- **Active Path Removed**: `.agents/skills/sandbase-mcp`
- **Raw Source Preserved**: `06_INBOX/RAW_IMPORTS/skills/agentic-awesome-skills-main/agentic-awesome-skills-main/skills/sandbase-mcp/SKILL.md`
- **Source URL**: https://github.com/agentic-awesome-skills
- **Detected Pattern**: `Remote download and pipe execution (curl | bash or iex webclient)`
- **Windows Defender Status**: `defender_detected = false` (proactively removed per CRITICAL policy)
- **File Hashes** (SHA-256):
  - `PROVENANCE.json`: `017f221b49e278b722b512b63e9c5116ee5d0c90f26a8727b5177274502c77ce`
  - `SKILL.md`: `bebe7de8ed56d59baa8d6738c7e3a730fa4176c5918b1148f989fa3bc0cb8f91`

#### `aspire`
- **Skill ID**: `skill:awesome-copilot:aspire`
- **Active Path Removed**: `.agents/skills/aspire`
- **Raw Source Preserved**: `06_INBOX/RAW_IMPORTS/skills/awesome-copilot/skills/aspire/SKILL.md`
- **Source URL**: https://github.com/github/awesome-copilot
- **Detected Pattern**: `Remote download and pipe execution (curl | bash or iex webclient)`
- **Windows Defender Status**: `defender_detected = false` (proactively removed per CRITICAL policy)
- **File Hashes** (SHA-256):
  - `PROVENANCE.json`: `e4863b9f59fd171e79ef551a33912d9d4d4ae2ac5292dc7614f4b0cd24095765`
  - `SKILL.md`: `abe75498a606a8954232ff047cd462f381b62419dba49f0ba6f1c3491302587e`
  - `references/architecture.md`: `5ee5e41609d2b7ace9a803fe42e740157173143dc88336768fdb84b143369ae0`
  - `references/cli-reference.md`: `1ee8f3d3d994958793d0dd8c63d8d00f2cd58b011814c08ad6cd872e99cf6f79`
  - `references/dashboard.md`: `0726bf1b36ae35113863fdb896b62e0b498f1b4f263d840d1c2986faaefcb9ad`
  - `references/deployment.md`: `c8564d8cf84e0e416a4b668b055558353d3ea82f1a0a80d4b31fbcf6d632e7f7`
  - `references/integrations-catalog.md`: `8edca39bfe49ddea6dd3d5140df4d378f31b72c791f81858dca9f154268f3401`
  - `references/mcp-server.md`: `3793e05e84b6b6c336c805279ee18a44c6ce2cc478f0e4618be5ab147492e2c4`
  - `references/polyglot-apis.md`: `be8467b89e200411074ac88ec0e4a2e62e1b38283097e0895a6b1ba3e6275b0a`
  - `references/testing.md`: `e9f8199f7dad7432f292c5a03ac6ea5ead213a29a955ad039d1dfa9e130c564f`
  - `references/troubleshooting.md`: `8ef6f2cd7f3fb1c74727922358581036074f36197548390f4bc49c3a2122f3d7`

---

## 3. High-Risk Review Audit (7 Candidates)

The 7 skills marked HIGH in static evaluation were audited against empirical Defender evidence and host safety criteria:

| Skill Name | Active Status | Destructive / Privileged | Defender Detected | Final Verdict | Audit Findings |
| :--- | :---: | :---: | :---: | :---: | :--- |
| `containerize-aspnetcore` | Active | `False` | `False` | `RETAIN_WITH_SECURITY_REVIEW` | Static flag triggered on security documentation or standard container/devops patterns. No host-destructive operations or Defender detections on active path. |
| `mcp-implementation-security-review` | Active | `False` | `False` | `RETAIN_WITH_SECURITY_REVIEW` | Static flag triggered on security documentation or standard container/devops patterns. No host-destructive operations or Defender detections on active path. |
| `audit-skills` | Active | `False` | `False` | `RETAIN_WITH_SECURITY_REVIEW` | Static flag triggered on security documentation or standard container/devops patterns. No host-destructive operations or Defender detections on active path. |
| `claude-in-chrome-troubleshooting` | Active | `False` | `False` | `RETAIN_WITH_SECURITY_REVIEW` | Static flag triggered on security documentation or standard container/devops patterns. No host-destructive operations or Defender detections on active path. |
| `gcp-cloud-run` | Active | `False` | `False` | `RETAIN_WITH_SECURITY_REVIEW` | Static flag triggered on security documentation or standard container/devops patterns. No host-destructive operations or Defender detections on active path. |
| `manage-skills` | Active | `False` | `False` | `RETAIN_WITH_SECURITY_REVIEW` | Static flag triggered on security documentation or standard container/devops patterns. No host-destructive operations or Defender detections on active path. |
| `xss-html-injection` | Active | `False` | `False` | `RETAIN_WITH_SECURITY_REVIEW` | Static flag triggered on security documentation or standard container/devops patterns. No host-destructive operations or Defender detections on active path. |

### Windows Defender Threat Detection Correlation

A direct query of `Get-MpThreatDetection` on the host system revealed that all historical Defender alerts were strictly isolated to the unextracted raw imports repository:
- Target: `06_INBOX/RAW_IMPORTS/skills/.../pentest-tools/src-hunter/references/...`
- Active Vault: **0 Defender detections in `.agents/skills/`**
- Conclusion: Zero active skills have been quarantined or blocked by Windows Defender. No Defender exclusions or bypasses were created.

---

## 4. Integrity & Policy Verifications

1. **Active Vault Invariant**: `CRITICAL_REMAINING = 0`. Both `sandbase-mcp` and `aspire` have been completely removed from `.agents/skills/`.
2. **Raw Corpus Invariant**: `06_INBOX/RAW_IMPORTS/skills/` remains 100% immutable and unedited.
3. **Historical Audit Preservation**: `07_EVALUATION/skills_quality_v1/`, `skills_semantic_v1/`, and `runtime_v1/` remain untouched as historical baselines.
4. **Full Traceability**: All removed active files, their exact hashes, and raw paths are logged in `security_removal_ledger.jsonl` and `quarantine_manifest.json`.

## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
