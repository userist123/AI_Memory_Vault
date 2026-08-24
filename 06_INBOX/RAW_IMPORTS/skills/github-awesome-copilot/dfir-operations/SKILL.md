---
name: dfir-operations
description: >-
  Runbook and expert multi-step procedure for interacting with the LogAnalyzer DFIR
  Enterprise platform, analyzing forensic evidence, running YARA and Sigma engines offline,
  and executing containment playbooks.
---

# DFIR Operations Runbook

## Overview
This skill equips Antigravity agents to assist security analysts, incident responders, and forensic investigators in operating the **LogAnalyzer DFIR Enterprise** air-gapped platform.

---

## 1. Operating Environment & Integrity
- **Database Engine:** SQLite in WAL mode with connection encryption.
- **Evidence Integrity:** SHA-256 calculation for all input artifacts (EVTX, Registry Hives, Triage CSVs).
- **Air-Gapped Policy:** Strictly offline. Zero external network or cloud telemetry calls.

---

## 2. Core Detection & Analysis Engines
1. **Sigma Rule Engine (`SigmaRuleEngine`):**
   - Evaluates system events (Windows Event Log IDs 4625, 4688, 7045, 1102, etc.) against MITRE-mapped YAML signatures.
2. **YARA Signature Engine (`YaraRuleEngine`):**
   - Offline pattern matching for Web Shells, Mimikatz artifacts, Cobalt Strike Named Pipes, and Ransomware indicators.
3. **Shannon Entropy & Anomaly Engine (`AnomalyDetectionEngine`):**
   - Dynamic entropy scoring ($H > 4.8$) for detecting Base64/obfuscated command lines, process masquerading, and off-hours logons.
4. **Kill Chain Storyline Engine (`AttackStorylineEngine`):**
   - Chronological synthesis of multi-stage attack paths from Initial Access to Impact.
5. **APT Attribution Engine (`AptAttributionEngine`):**
   - Offline profiling of advanced persistent threat groups (APT28, APT29, Lazarus, Sandworm, LockBit) matching identified TTPs.

---

## 3. Incident Response & Containment Workflow
When an incident is confirmed:
1. **Analyze:** Inspect high-confidence alerts and Shannon entropy scores.
2. **Isolate:** Generate `Isolate-Host.ps1` to sever compromised endpoints from the network while preserving SOC management tunnels.
3. **Neutralize:** Generate `Kill-ProcessTree.ps1` for malicious process trees.
4. **Remediate:** Clean registry persistence keys (`Run`, `RunOnce`, `Winlogon`, `Services`).
5. **Export:** Generate standardized **STIX 2.1 Bundles** and **MISP Event JSON** for threat sharing.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[10 Imports and Sources Map]]
- [[Master_Skills_Catalog_251]]
- [[Knowledge Graph Home]]
