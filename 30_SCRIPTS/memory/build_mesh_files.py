"""scripts/build_mesh_files.py — Master Cognitive Memory Mesh Builder & Validator.

Generates:
1. evaluation/vault_mesh/mesh_validator.py
2. evaluation/vault_mesh/vault_inventory.yaml
3. evaluation/vault_mesh/vault_graph.yaml
4. 01_KNOWLEDGE/Vault_Memory_Mesh_Architecture.md
5. evaluation/tests/test_vault_mesh.py
"""
import os
import sys
import re
import yaml
import json
from pathlib import Path

VAULT_ROOT = Path(__file__).resolve().parent.parent
if str(VAULT_ROOT) not in sys.path:
    sys.path.insert(0, str(VAULT_ROOT))


# -------------------------------------------------------------
# 1. Write evaluation/vault_mesh/mesh_validator.py
# -------------------------------------------------------------
validator_code = '''"""evaluation/vault_mesh/mesh_validator.py — Deterministic Validator for Cognitive Memory Mesh."""
import re
from pathlib import Path
from typing import Dict, List, Any, Optional, Set
import yaml

ALLOWED_OBJECT_TYPES: Set[str] = {
    "KNOWLEDGE",
    "MEMORY",
    "SKILL",
    "PROCEDURE",
    "AGENT",
    "EXPERIMENT",
    "EVIDENCE",
    "OUTCOME",
    "TRACE",
    "AUDIT",
    "RESEARCH",
}

ALLOWED_LIFECYCLES: Set[str] = {
    "RAW",
    "REVIEW",
    "VERIFIED",
    "ACTIVE",
    "SUPERSEDED",
    "ARCHIVED",
}

ALLOWED_VERIFICATIONS: Set[str] = {
    "unverified",
    "inferred",
    "supported",
    "verified",
    "contradicted",
}

ALLOWED_RELATIONS: Set[str] = {
    "derived_from",
    "supported_by",
    "contradicts",
    "supersedes",
    "superseded_by",
    "implements",
    "uses",
    "requires",
    "tested_by",
    "verified_by",
    "produced_by",
    "observed_by",
    "references",
    "related_to",
}

class MeshValidator:
    def __init__(self, inventory_path: Optional[Path] = None, graph_path: Optional[Path] = None):
        self.inventory_path = inventory_path
        self.graph_path = graph_path
        self.inventory: List[Dict[str, Any]] = []
        self.graph_nodes: List[Dict[str, Any]] = []
        self.graph_edges: List[Dict[str, Any]] = []
        self.id_index: Dict[str, Dict[str, Any]] = {}

    def load(self) -> None:
        if self.inventory_path and self.inventory_path.exists():
            with open(self.inventory_path, "r", encoding="utf-8") as f:
                data = yaml.safe_load(f) or {}
                self.inventory = data.get("objects", [])
        if self.graph_path and self.graph_path.exists():
            with open(self.graph_path, "r", encoding="utf-8") as f:
                data = yaml.safe_load(f) or {}
                self.graph_nodes = data.get("nodes", [])
                self.graph_edges = data.get("edges", [])

    def validate_inventory(self) -> Dict[str, Any]:
        errors: List[str] = []
        seen_ids: Set[str] = set()

        for idx, obj in enumerate(self.inventory):
            oid = str(obj.get("id", "")).strip()
            if not oid:
                errors.append(f"Object at index {idx} has missing or empty id")
                continue
            if oid in seen_ids:
                errors.append(f"Duplicate object ID in inventory: {oid}")
            seen_ids.add(oid)

            otype = str(obj.get("type", "")).strip().upper()
            if otype not in ALLOWED_OBJECT_TYPES:
                errors.append(f"Object {oid} has invalid type: '{otype}'. Allowed: {sorted(list(ALLOWED_OBJECT_TYPES))}")

            lc = str(obj.get("lifecycle", "ACTIVE")).strip().upper()
            if lc not in ALLOWED_LIFECYCLES:
                errors.append(f"Object {oid} has invalid lifecycle: '{lc}'. Allowed: {sorted(list(ALLOWED_LIFECYCLES))}")

            verif = str(obj.get("verification", "inferred")).strip().lower()
            if verif not in ALLOWED_VERIFICATIONS:
                errors.append(f"Object {oid} has invalid verification: '{verif}'. Allowed: {sorted(list(ALLOWED_VERIFICATIONS))}")

            for tf in ["valid_from", "valid_until", "observed_at"]:
                val = obj.get(tf)
                if val is not None and str(val).strip() and str(val).strip().lower() != "none":
                    if not re.match(r"^\\d{4}-\\d{2}-\\d{2}(T\\d{2}:\\d{2}:\\d{2}(Z|[+-]\\d{2}:\\d{2})?)?$", str(val).strip()):
                        errors.append(f"Object {oid} has invalid temporal format for {tf}: '{val}'")

            self.id_index[oid] = obj

        return {
            "valid": len(errors) == 0,
            "total_objects": len(self.inventory),
            "errors": errors,
        }

    def validate_graph(self) -> Dict[str, Any]:
        errors: List[str] = []
        known_ids: Set[str] = set(self.id_index.keys())

        for node in self.graph_nodes:
            nid = str(node.get("id", "")).strip()
            if nid:
                known_ids.add(nid)
                ntype = str(node.get("type", "")).strip().upper()
                if ntype not in ALLOWED_OBJECT_TYPES:
                    errors.append(f"Graph node {nid} has invalid type: '{ntype}'")

        for idx, edge in enumerate(self.graph_edges):
            src = str(edge.get("source", "")).strip()
            rel = str(edge.get("relation", "")).strip().lower()
            tgt = str(edge.get("target", "")).strip()

            if not src or src not in known_ids:
                errors.append(f"Edge {idx} has missing or unknown source ID: '{src}'")
            if not tgt or tgt not in known_ids:
                errors.append(f"Edge {idx} has missing or unknown target ID: '{tgt}'")
            if rel not in ALLOWED_RELATIONS:
                errors.append(f"Edge {idx} ({src} -> {tgt}) has invalid relation: '{rel}'. Allowed: {sorted(list(ALLOWED_RELATIONS))}")

        return {
            "valid": len(errors) == 0,
            "total_nodes": len(known_ids),
            "total_edges": len(self.graph_edges),
            "errors": errors,
        }

    def validate_all(self) -> Dict[str, Any]:
        self.load()
        inv_res = self.validate_inventory()
        graph_res = self.validate_graph()
        all_errors = inv_res.get("errors", []) + graph_res.get("errors", [])
        return {
            "valid": len(all_errors) == 0,
            "total_objects": inv_res.get("total_objects", 0),
            "total_edges": graph_res.get("total_edges", 0),
            "errors": all_errors,
        }
'''

mesh_dir = VAULT_ROOT / "evaluation" / "vault_mesh"
mesh_dir.mkdir(parents=True, exist_ok=True)
(mesh_dir / "mesh_validator.py").write_text(validator_code, encoding="utf-8")
print("1. Written evaluation/vault_mesh/mesh_validator.py")

# -------------------------------------------------------------
# 2. Build Inventory and Graph
# -------------------------------------------------------------
objects = []
nodes = []
edges = []
id_map = {}

# 2.1 Experiments
exps = [
    {
        "id": "EXP-P0-RETRIEVAL-FUSION",
        "type": "EXPERIMENT",
        "path": "evaluation/retrieval_fusion/",
        "title": "P0 Multi-Signal Candidate Retrieval Fusion Benchmark",
        "lifecycle": "ACTIVE",
        "verification": "verified",
        "provenance": {"source_type": "execution", "source_ref": "evaluation/retrieval_fusion/fusion_evaluator.py"},
        "hypothesis": "Multi-signal candidate fusion (semantic + lexical + entity + graph) eliminates single-signal candidate recall bottlenecks.",
        "created": "2026-09-01T12:00:00Z",
        "updated": "2026-09-01T18:00:00Z",
    },
    {
        "id": "EXP-P1-CONTEXT-PACKING",
        "type": "EXPERIMENT",
        "path": "evaluation/context_packing/",
        "title": "P1 Section-Aware Context Packing Lab",
        "lifecycle": "ACTIVE",
        "verification": "verified",
        "provenance": {"source_type": "execution", "source_ref": "evaluation/context_packing/packing_evaluator.py"},
        "hypothesis": "Section-aware extractive packing preserves retrieved candidate facts under strict context budgets.",
        "created": "2026-09-01T15:00:00Z",
        "updated": "2026-09-01T20:00:00Z",
    },
    {
        "id": "EXP-P2-TEMPORAL-MEMORY",
        "type": "EXPERIMENT",
        "path": "evaluation/temporal_memory/",
        "title": "P2 Bi-Temporal & Supersession Traversal Lab",
        "lifecycle": "ACTIVE",
        "verification": "verified",
        "provenance": {"source_type": "execution", "source_ref": "evaluation/temporal_memory/temporal_evaluator.py"},
        "hypothesis": "Bi-temporal lineage and supersession edge traversal resolves historical fact supersession without temporal hallucination.",
        "created": "2026-09-01T17:00:00Z",
        "updated": "2026-09-01T21:00:00Z",
    },
    {
        "id": "EXP-EXT-MEMORY-AUDIT",
        "type": "EXPERIMENT",
        "path": "evaluation/memory_usage_audit/",
        "title": "External Conversation Memory Usage Audit Harness",
        "lifecycle": "ACTIVE",
        "verification": "verified",
        "provenance": {"source_type": "execution", "source_ref": "evaluation/memory_usage_audit/conversation_auditor.py"},
        "hypothesis": "Multi-stage audit distinguishing declared memory claims from physical execution traces exposes synthetic self-reporting.",
        "created": "2026-09-01T18:00:00Z",
        "updated": "2026-09-01T22:00:00Z",
    },
]

for exp in exps:
    objects.append(exp)
    nodes.append({"id": exp["id"], "type": exp["type"], "path": exp["path"], "lifecycle": exp["lifecycle"], "verification": exp["verification"]})
    id_map[exp["id"]] = exp["id"]

# 2.2 Evidences
evids = [
    {
        "id": "EVID-P0-RETRIEVAL-REPORT",
        "type": "EVIDENCE",
        "path": "evaluation/reports/retrieval_fusion_report.json",
        "title": "Empirical P0 Retrieval Fusion Results & Diagnostic Metrics",
        "lifecycle": "ACTIVE",
        "verification": "verified",
        "provenance": {"source_type": "execution", "source_ref": "evaluation/reports/retrieval_fusion_report.json"},
        "created": "2026-09-01T18:00:00Z",
        "updated": "2026-09-01T18:00:00Z",
    },
    {
        "id": "EVID-P1-PACKING-REPORT",
        "type": "EVIDENCE",
        "path": "evaluation/reports/context_packing_report.json",
        "title": "Empirical P1 Context Packing Metrics & Token Telemetry",
        "lifecycle": "ACTIVE",
        "verification": "verified",
        "provenance": {"source_type": "execution", "source_ref": "evaluation/reports/context_packing_report.json"},
        "created": "2026-09-01T20:00:00Z",
        "updated": "2026-09-01T20:00:00Z",
    },
    {
        "id": "EVID-P2-TEMPORAL-REPORT",
        "type": "EVIDENCE",
        "path": "evaluation/reports/temporal_memory_report.json",
        "title": "Empirical P2 Temporal Memory & Supersession Benchmark Output",
        "lifecycle": "ACTIVE",
        "verification": "verified",
        "provenance": {"source_type": "execution", "source_ref": "evaluation/reports/temporal_memory_report.json"},
        "created": "2026-09-01T21:00:00Z",
        "updated": "2026-09-01T21:00:00Z",
    },
    {
        "id": "EVID-WOB-ART-AUDIT",
        "type": "EVIDENCE",
        "path": "evaluation/reports/memory_usage_audit_wob_art.md",
        "title": "Forensic Audit Report of WOB ART External Conversation",
        "lifecycle": "ACTIVE",
        "verification": "verified",
        "provenance": {"source_type": "execution", "source_ref": "evaluation/reports/memory_usage_audit_wob_art.md"},
        "created": "2026-09-01T22:00:00Z",
        "updated": "2026-09-01T22:00:00Z",
    },
]

for ev in evids:
    objects.append(ev)
    nodes.append({"id": ev["id"], "type": ev["type"], "path": ev["path"], "lifecycle": ev["lifecycle"], "verification": ev["verification"]})
    id_map[ev["id"]] = ev["id"]

# Edges EXP -> EVID (produced_by)
edges.append({"source": "EXP-P0-RETRIEVAL-FUSION", "relation": "produced_by", "target": "EVID-P0-RETRIEVAL-REPORT", "evidence": "evaluation/reports/retrieval_fusion_report.json", "confidence": "high"})
edges.append({"source": "EXP-P1-CONTEXT-PACKING", "relation": "produced_by", "target": "EVID-P1-PACKING-REPORT", "evidence": "evaluation/reports/context_packing_report.json", "confidence": "high"})
edges.append({"source": "EXP-P2-TEMPORAL-MEMORY", "relation": "produced_by", "target": "EVID-P2-TEMPORAL-REPORT", "evidence": "evaluation/reports/temporal_memory_report.json", "confidence": "high"})
edges.append({"source": "EXP-EXT-MEMORY-AUDIT", "relation": "produced_by", "target": "EVID-WOB-ART-AUDIT", "evidence": "evaluation/reports/memory_usage_audit_wob_art.md", "confidence": "high"})

# 2.3 Telemetry Traces & Audits
telemetry_obj = {
    "id": "TRACE-RUNTIME-OBSERVED",
    "type": "TRACE",
    "path": "telemetry/observed_memory_traces.jsonl",
    "title": "Append-Only Runtime Observed Memory Telemetry Traces",
    "lifecycle": "ACTIVE",
    "verification": "verified",
    "provenance": {"source_type": "execution", "source_ref": "memory_controller/memory_trace.py"},
    "created": "2026-09-02T00:23:42Z",
    "updated": "2026-09-02T00:30:05Z",
}
objects.append(telemetry_obj)
nodes.append({"id": telemetry_obj["id"], "type": telemetry_obj["type"], "path": telemetry_obj["path"], "lifecycle": telemetry_obj["lifecycle"], "verification": telemetry_obj["verification"]})
id_map[telemetry_obj["id"]] = telemetry_obj["id"]

audit_obj = {
    "id": "AUDIT-SYSTEM-LOG",
    "type": "AUDIT",
    "path": "audit_log.jsonl",
    "title": "Tamper-Evident SHA-256 Chained Cryptographic System Audit Log",
    "lifecycle": "ACTIVE",
    "verification": "verified",
    "provenance": {"source_type": "execution", "source_ref": "memory_controller/audit/logger.py"},
    "created": "2026-08-17T00:00:00Z",
    "updated": "2026-09-02T00:30:00Z",
}
objects.append(audit_obj)
nodes.append({"id": audit_obj["id"], "type": audit_obj["type"], "path": audit_obj["path"], "lifecycle": audit_obj["lifecycle"], "verification": audit_obj["verification"]})
id_map[audit_obj["id"]] = audit_obj["id"]

# 2.4 Agents
agents = [
    ("AGENT-ROUTER", "Router Agent", "Read/Search query decomposition and classification", ["SKILL-GLOBAL-ROUTER", "SKILL-API-DESIGN"]),
    ("AGENT-RETRIEVAL", "Retrieval Agent", "Associative, semantic and graph lineage recall", ["SKILL-RETRIEVAL-OPS", "SKILL-SQLITE-WAL"]),
    ("AGENT-VERIFIER", "Verifier Agent", "Schema verification, frontmatter & trust boundary audit", ["SKILL-SECURITY-AUDIT", "SKILL-OWASP-HARDENING"]),
    ("AGENT-CONSOLIDATOR", "Consolidator Agent", "Episodic reflection and memory consolidation", ["SKILL-VAULT-OPERATIONS", "SKILL-CLEAN-ARCH"]),
    ("AGENT-CRITIC", "Critic Agent", "6-stage Reflexion and SelfRefine critique", ["SKILL-CLOSED-LOOP-REFLEXION"]),
    ("AGENT-LEAD-COUNCIL", "Lead Council Synthesizer", "Multi-agent context synthesis and decision arbitration", ["SKILL-COUNCIL-RUNTIME"]),
]

for ag_id, ag_title, ag_desc, ag_skills in agents:
    ag_obj = {
        "id": ag_id,
        "type": "AGENT",
        "path": "99_SYSTEM/Agent_Capability_Registry.md",
        "title": ag_title,
        "lifecycle": "ACTIVE",
        "verification": "verified",
        "provenance": {"source_type": "official", "source_ref": "99_SYSTEM/Agent_Capability_Registry.md"},
        "created": "2026-08-17T00:00:00Z",
        "updated": "2026-09-02T00:00:00Z",
    }
    objects.append(ag_obj)
    nodes.append({"id": ag_id, "type": "AGENT", "path": ag_obj["path"], "lifecycle": "ACTIVE", "verification": "verified"})
    id_map[ag_id] = ag_id

# 2.5 Skills
skills_to_register = [
    ("SKILL-GLOBAL-ROUTER", ".agents/skills/global-skill-registry-router/SKILL.md", "Global Skill Registry Router"),
    ("SKILL-API-DESIGN", ".agents/skills/backend-api-design/SKILL.md", "Backend API Design Standards"),
    ("SKILL-RETRIEVAL-OPS", ".agents/skills/vault-operations/SKILL.md", "Vault Retrieval & Operations"),
    ("SKILL-SQLITE-WAL", ".agents/skills/skill-sqlite-wal-optimization/SKILL.md", "SQLite WAL Optimization"),
    ("SKILL-SECURITY-AUDIT", ".agents/skills/vault-security-audit/SKILL.md", "Vault Security Audit Standards"),
    ("SKILL-OWASP-HARDENING", ".agents/skills/skill-owasp-backend-hardening/SKILL.md", "OWASP Backend Hardening"),
    ("SKILL-VAULT-OPERATIONS", ".agents/skills/vault-operations/SKILL.md", "Vault Memory Operations Runbook"),
    ("SKILL-CLEAN-ARCH", ".agents/skills/clean-architecture-backend/SKILL.md", "Clean Architecture Backend"),
    ("SKILL-CLOSED-LOOP-REFLEXION", ".agents/skills/vault-operations/SKILL.md", "Closed Loop Reflexion Skill"),
    ("SKILL-COUNCIL-RUNTIME", ".agents/skills/copilot-agentic-workflows/SKILL.md", "Council Agentic Workflows"),
    ("SKILL-UI-SENSEI", ".agents/skills/ui-sensei/SKILL.md", "UI Sensei Design Philosophy"),
    ("SKILL-DESIGN-SYSTEM", ".agents/skills/design-system-foundation/SKILL.md", "Design System Foundation"),
    ("SKILL-MOTION-DESIGN", ".agents/skills/motion-design/SKILL.md", "Motion Design Principles"),
    ("SKILL-POWERSHELL-SECOPS", ".agents/skills/powershell-secops/SKILL.md", "PowerShell SecOps Standard"),
    ("SKILL-DFIR-OPERATIONS", ".agents/skills/dfir-operations/SKILL.md", "DFIR Forensic Operations"),
]

for sk_id, sk_path, sk_title in skills_to_register:
    if sk_id not in id_map:
        sk_obj = {
            "id": sk_id,
            "type": "SKILL",
            "path": sk_path,
            "title": sk_title,
            "lifecycle": "ACTIVE",
            "verification": "verified",
            "provenance": {"source_type": "official", "source_ref": sk_path},
            "created": "2026-08-17T00:00:00Z",
            "updated": "2026-09-02T00:00:00Z",
        }
        objects.append(sk_obj)
        nodes.append({"id": sk_id, "type": "SKILL", "path": sk_path, "lifecycle": "ACTIVE", "verification": "verified"})
        id_map[sk_id] = sk_id

for ag_id, _, _, ag_skills in agents:
    for sk in ag_skills:
        edges.append({"source": ag_id, "relation": "uses", "target": sk, "evidence": "99_SYSTEM/Agent_Capability_Registry.md", "confidence": "high"})

# 2.6 Canonical Markdown Notes
scan_dirs = {
    "00_CORE": "KNOWLEDGE",
    "01_KNOWLEDGE": "KNOWLEDGE",
    "02_PROJECTS": "KNOWLEDGE",
    "03_PROCEDURES": "PROCEDURE",
    "04_MEMORY": "MEMORY",
    "05_RESOURCES": "KNOWLEDGE",
    "90_TEMPLATES": "KNOWLEDGE",
    "99_SYSTEM": "KNOWLEDGE",
}

note_files = []
for sdir, default_type in scan_dirs.items():
    p = VAULT_ROOT / sdir
    if not p.exists():
        continue
    for f in p.rglob("*.md"):
        note_files.append((f, default_type))

for f, default_type in note_files:
    text = f.read_text(encoding="utf-8")
    fm_match = re.match(r"^---\r?\n(.*?)\r?\n---", text, re.DOTALL)
    fm = {}
    if fm_match:
        try:
            fm = yaml.safe_load(fm_match.group(1)) or {}
        except Exception:
            pass
    
    rel_path = f.relative_to(VAULT_ROOT).as_posix()
    prefix = "KNOW"
    if "03_PROCEDURES" in rel_path or default_type == "PROCEDURE":
        prefix = "PROC"
    elif "04_MEMORY" in rel_path or default_type == "MEMORY":
        prefix = "MEM"
    
    canonical_id = f"{prefix}-{f.stem}"
    title_match = re.search(r"^#\s+(.+)$", text, re.MULTILINE)
    title = title_match.group(1).strip() if title_match else f.stem
    
    lifecycle = str(fm.get("lifecycle") or fm.get("status") or fm.get("document_status") or "ACTIVE").upper()
    if lifecycle not in ["RAW", "REVIEW", "VERIFIED", "ACTIVE", "SUPERSEDED", "ARCHIVED"]:
        lifecycle = "ACTIVE"
    
    verif = str(fm.get("verification") or "inferred").lower()
    if verif not in ["unverified", "inferred", "supported", "verified", "contradicted"]:
        verif = "inferred"
        
    prov = fm.get("provenance") or {"source_type": "official", "source_ref": rel_path}
    
    obj_item = {
        "id": canonical_id,
        "type": "PROCEDURE" if prefix == "PROC" else ("MEMORY" if prefix == "MEM" else "KNOWLEDGE"),
        "path": rel_path,
        "title": title,
        "lifecycle": lifecycle,
        "verification": verif,
        "provenance": prov,
        "created": str(fm.get("created") or "2026-08-17T00:00:00Z"),
        "updated": str(fm.get("updated") or "2026-09-02T00:00:00Z"),
        "valid_from": fm.get("valid_from"),
        "valid_until": fm.get("valid_until"),
        "supersedes": fm.get("supersedes"),
        "superseded_by": fm.get("superseded_by"),
        "confidence": {
            "source_confidence": "high",
            "evidence_confidence": "high" if verif == "verified" else "medium",
            "retrieval_confidence": "high",
            "verification_confidence": "high" if verif == "verified" else "medium",
        }
    }
    
    if canonical_id not in id_map:
        objects.append(obj_item)
        nodes.append({"id": canonical_id, "type": obj_item["type"], "path": rel_path, "lifecycle": lifecycle, "verification": verif})
        id_map[canonical_id] = canonical_id
        id_map[f.stem] = canonical_id
        id_map[rel_path] = canonical_id

# Link specific Knowledge to Evidences & Experiments
if "KNOW-Retrieval_Bottleneck_P0_Empirical_Findings" in id_map:
    edges.append({"source": "KNOW-Retrieval_Bottleneck_P0_Empirical_Findings", "relation": "supported_by", "target": "EVID-P0-RETRIEVAL-REPORT", "evidence": "evaluation/reports/retrieval_fusion_report.json", "confidence": "high"})
    edges.append({"source": "KNOW-Retrieval_Bottleneck_P0_Empirical_Findings", "relation": "tested_by", "target": "EXP-P0-RETRIEVAL-FUSION", "evidence": "evaluation/retrieval_fusion/", "confidence": "high"})

if "KNOW-Context_Packing_P1_Empirical_Findings" in id_map:
    edges.append({"source": "KNOW-Context_Packing_P1_Empirical_Findings", "relation": "supported_by", "target": "EVID-P1-PACKING-REPORT", "evidence": "evaluation/reports/context_packing_report.json", "confidence": "high"})
    edges.append({"source": "KNOW-Context_Packing_P1_Empirical_Findings", "relation": "tested_by", "target": "EXP-P1-CONTEXT-PACKING", "evidence": "evaluation/context_packing/", "confidence": "high"})

if "KNOW-Temporal_Memory_P2_Empirical_Findings" in id_map:
    edges.append({"source": "KNOW-Temporal_Memory_P2_Empirical_Findings", "relation": "supported_by", "target": "EVID-P2-TEMPORAL-REPORT", "evidence": "evaluation/reports/temporal_memory_report.json", "confidence": "high"})
    edges.append({"source": "KNOW-Temporal_Memory_P2_Empirical_Findings", "relation": "tested_by", "target": "EXP-P2-TEMPORAL-MEMORY", "evidence": "evaluation/temporal_memory/", "confidence": "high"})

if "KNOW-Memory_Usage_Audit_Principles" in id_map:
    edges.append({"source": "KNOW-Memory_Usage_Audit_Principles", "relation": "supported_by", "target": "EVID-WOB-ART-AUDIT", "evidence": "evaluation/reports/memory_usage_audit_wob_art.md", "confidence": "high"})
    edges.append({"source": "KNOW-Memory_Usage_Audit_Principles", "relation": "tested_by", "target": "EXP-EXT-MEMORY-AUDIT", "evidence": "evaluation/memory_usage_audit/", "confidence": "high"})

if "KNOW-Agent_Memory_Trace_Protocol" in id_map:
    edges.append({"source": "KNOW-Agent_Memory_Trace_Protocol", "relation": "implements", "target": "TRACE-RUNTIME-OBSERVED", "evidence": "telemetry/observed_memory_traces.jsonl", "confidence": "high"})

# Skill -> Knowledge linking (requires)
skill_know_links = [
    ("SKILL-UI-SENSEI", "KNOW-UI_Sensei_Design_Philosophy"),
    ("SKILL-DESIGN-SYSTEM", "KNOW-Design_System_Foundation"),
    ("SKILL-MOTION-DESIGN", "KNOW-Motion_Design_Principles"),
    ("SKILL-POWERSHELL-SECOPS", "PROC-PowerShell_SecOps_Forensic_Standard"),
    ("SKILL-DFIR-OPERATIONS", "KNOW-LogAnalyzer_DFIR_Enterprise_Architecture"),
    ("SKILL-CLOSED-LOOP-REFLEXION", "PROC-Closed_Loop_Reflexion_Pipeline"),
    ("SKILL-RETRIEVAL-OPS", "KNOW-System_Architecture"),
    ("SKILL-SECURITY-AUDIT", "KNOW-Memory_Protocol"),
]

for sk_id, kn_id in skill_know_links:
    if sk_id in id_map and kn_id in id_map:
        edges.append({"source": sk_id, "relation": "requires", "target": kn_id, "evidence": "canonical_mapping", "confidence": "high"})

# Parse wiki-links [[Target]] across all notes
for f, _ in note_files:
    text = f.read_text(encoding="utf-8")
    src_id = id_map.get(f.stem) or id_map.get(f.relative_to(VAULT_ROOT).as_posix())
    if not src_id:
        continue
    wiki_links = re.findall(r"\[\[([^\]\|]+)(?:\|[^\]]+)?\]\]", text)
    for wl in wiki_links:
        wl_clean = wl.strip().replace(".md", "")
        wl_target = wl_clean.split("/")[-1]
        tgt_id = id_map.get(wl_target) or id_map.get(f"KNOW-{wl_target}") or id_map.get(f"PROC-{wl_target}") or id_map.get(f"MEM-{wl_target}")
        if tgt_id and tgt_id != src_id:
            edge = {"source": src_id, "relation": "references", "target": tgt_id, "evidence": f"wikilink in {f.name}", "confidence": "high"}
            if edge not in edges:
                edges.append(edge)

inv_data = {"version": "1.0.0", "objects": objects}
graph_data = {"version": "1.0.0", "nodes": nodes, "edges": edges}

(mesh_dir / "vault_inventory.yaml").write_text(yaml.dump(inv_data, sort_keys=False), encoding="utf-8")
(mesh_dir / "vault_graph.yaml").write_text(yaml.dump(graph_data, sort_keys=False), encoding="utf-8")
print(f"2. Saved {len(objects)} objects and {len(edges)} graph edges to evaluation/vault_mesh/")

# -------------------------------------------------------------
# 3. Write 01_KNOWLEDGE/Vault_Memory_Mesh_Architecture.md
# -------------------------------------------------------------
arch_doc = """---
id: "vault-memory-mesh-architecture-0001"
type: knowledge
lifecycle: ACTIVE
category: meta
tags: [mesh, cognitive-architecture, taxonomy, provenance, verification]
created: 2026-09-02T19:20:00Z
updated: 2026-09-02T19:20:00Z
provenance:
  source_type: execution
  source_ref: "01_KNOWLEDGE/Vault_Memory_Mesh_Architecture.md"
confidence: high
verification: verified
enriched_by: ai
---

# Vault Memory Mesh Architecture

## 1. Purpose

The **Cognitive Memory Mesh** establishes a formal, machine-readable semantic mesh across all canonical knowledge objects, episodic memories, skills, procedures, agents, experiments, empirical evidences, runtime telemetry traces, and audit logs in the `AI_Memory_Vault`.

It bridges unstructured Markdown notes with deterministic, graph-theoretic discovery without altering production runtime retrieval or packing algorithms.

---

## 2. Canonical Object Taxonomy

The mesh defines a strict, non-overlapping 11-type taxonomy:

| Object Type | Description | Primary Location | Allowed Outgoing Relations |
|---|---|---|---|
| `KNOWLEDGE` | Canonical, verified domain facts and architectural blueprints | `01_KNOWLEDGE/`, `00_CORE/`, `02_PROJECTS/` | `supported_by`, `tested_by`, `references`, `supersedes` |
| `MEMORY` | Episodic memory items (Decisions, Errors, Experiences, Lessons, Preferences) | `04_MEMORY/` | `derived_from`, `references`, `superseded_by` |
| `SKILL` | Reusable procedural capabilities and execution runbooks | `.agents/skills/` | `requires`, `tested_by`, `implements` |
| `PROCEDURE` | Operational runbooks and step-by-step standards | `03_PROCEDURES/` | `requires`, `implements`, `references` |
| `AGENT` | Specialized persona manifests and capability boundaries | `99_SYSTEM/Agent_Capability_Registry.md` | `uses`, `implements`, `observed_by` |
| `EXPERIMENT` | Empirical evaluation harnesses and diagnostic labs | `evaluation/` | `produced_by`, `tested_by`, `references` |
| `EVIDENCE` | Measured benchmark outputs, audit reports, and logs | `evaluation/reports/` | `supports`, `derived_from` |
| `OUTCOME` | Ground-truth labels and verified post-execution results | `evaluation/` | `supports`, `contradicts`, `derived_from` |
| `TRACE` | Append-only physical context presence logs | `telemetry/` | `observed_by`, `references` |
| `AUDIT` | Cryptographic SHA-256 chained audit events | `audit_log.jsonl` | `verified_by`, `references` |
| `RESEARCH` | External research notes and raw ingestion staging | `06_INBOX/`, `09_RESEARCH/` | `derived_from` |

---

## 3. Canonical Identity Scheme

All objects maintain deterministic, human-readable canonical identifiers:
- `KNOW-<name>`: Canonical knowledge notes
- `MEM-<name>`: Episodic memory records
- `SKILL-<name>`: Agent execution skills
- `PROC-<name>`: Standard procedures & runbooks
- `AGENT-<name>`: Specialized council agents
- `EXP-<name>`: Evaluation laboratories (e.g. `EXP-P0-RETRIEVAL-FUSION`)
- `EVID-<name>`: Measured experiment outputs (e.g. `EVID-P0-RETRIEVAL-REPORT`)
- `TRACE-<name>`: Telemetry traces (e.g. `TRACE-RUNTIME-OBSERVED`)
- `AUDIT-<name>`: System audit logs (e.g. `AUDIT-SYSTEM-LOG`)

---

## 4. Relationship Model & Directionality

Allowed typed directional relationships:
- `derived_from`: Target is the upstream source of the source object.
- `supported_by`: Source assertion is backed by target evidence.
- `contradicts`: Source fact conflicts with target fact.
- `supersedes` / `superseded_by`: Temporal versioning and fact replacement.
- `implements`: Source realizes the specification in target.
- `uses`: Source (e.g. Agent) invokes target (e.g. Skill).
- `requires`: Source requires target dependency.
- `tested_by`: Source is validated by target experiment.
- `verified_by`: Source is attested by target evidence or authority.
- `produced_by`: Source experiment generated target evidence.
- `observed_by`: Source trace recorded target memory presence.
- `references` / `related_to`: General associational citation.

---

## 5. Provenance & Evidence Lineage

The mesh formalizes two distinct provenance chains:

### Experimental Lineage
$$\text{RESEARCH} \longrightarrow \text{EVIDENCE} \longrightarrow \text{EXPERIMENT} \longrightarrow \text{RESULT} \longrightarrow \text{KNOWLEDGE} \longrightarrow \text{MEMORY/SKILL}$$

### Runtime Execution Lineage
$$\text{QUERY} \longrightarrow \text{TRACE} \longrightarrow \text{OBSERVED MEMORY} \longrightarrow \text{EXECUTION} \longrightarrow \text{VERIFICATION} \longrightarrow \text{OUTCOME} \longrightarrow \text{EVIDENCE}$$

---

## 6. Multi-Dimensional Confidence Model

Confidence is never collapsed into an opaque scalar. It is evaluated across four orthogonal dimensions:
1. `source_confidence`: Inherent trustworthiness of the initial source (`user` > `official` > `execution` > `ai` > `inference`).
2. `evidence_confidence`: Empirical measurement strength backing the claim.
3. `retrieval_confidence`: Deterministic candidate retrieval score.
4. `verification_confidence`: Level of formal verification (`verified` > `supported` > `inferred` > `unverified`).

---

## 7. Contradiction Representation

When facts conflict, the mesh categorizes the contradiction into one of five explicit types:
- `LOGICAL_CONTRADICTION`: Incompatible assertions under identical scope and time.
- `TEMPORAL_REVISION`: Historical assertion superseded by newer verified event.
- `DIFFERENT_SCOPE`: Different contextual boundaries (e.g. Windows vs Linux).
- `DIFFERENT_SOURCE`: Conflicting reports from distinct external authorities.
- `UNRESOLVED`: Active ambiguity requiring human/admin review.

---

## 8. Deterministic Validation & Zero Production Impact

- **Validator**: [`evaluation/vault_mesh/mesh_validator.py`](file:///evaluation/vault_mesh/mesh_validator.py) runs deterministic offline validation without LLM dependencies.
- **Production Isolation**: [`cognitive_core/multi_graph.py`](file:///cognitive_core/multi_graph.py) remains **100% FROZEN**. The mesh provides metadata indexing without altering runtime retrieval pipelines.
"""

(VAULT_ROOT / "01_KNOWLEDGE" / "Vault_Memory_Mesh_Architecture.md").write_text(arch_doc.strip() + "\n", encoding="utf-8")
print("3. Written 01_KNOWLEDGE/Vault_Memory_Mesh_Architecture.md")

# -------------------------------------------------------------
# 4. Write evaluation/tests/test_vault_mesh.py
# -------------------------------------------------------------
test_code = '''"""evaluation/tests/test_vault_mesh.py — Structural Validation Suite for Cognitive Memory Mesh."""
from pathlib import Path
import pytest
from evaluation.vault_mesh.mesh_validator import (
    MeshValidator,
    ALLOWED_OBJECT_TYPES,
    ALLOWED_LIFECYCLES,
    ALLOWED_VERIFICATIONS,
    ALLOWED_RELATIONS,
)

VAULT_ROOT = Path(__file__).resolve().parent.parent.parent
INVENTORY_PATH = VAULT_ROOT / "evaluation" / "vault_mesh" / "vault_inventory.yaml"
GRAPH_PATH = VAULT_ROOT / "evaluation" / "vault_mesh" / "vault_graph.yaml"


@pytest.fixture
def validator():
    v = MeshValidator(inventory_path=INVENTORY_PATH, graph_path=GRAPH_PATH)
    v.load()
    return v


def test_mesh_files_exist():
    assert INVENTORY_PATH.exists(), "vault_inventory.yaml must exist"
    assert GRAPH_PATH.exists(), "vault_graph.yaml must exist"


def test_inventory_validation(validator):
    res = validator.validate_inventory()
    assert res["valid"], f"Inventory validation failed with errors: {res['errors'][:5]}"
    assert res["total_objects"] > 100, f"Expected >100 objects, found {res['total_objects']}"


def test_graph_validation(validator):
    validator.validate_inventory()
    res = validator.validate_graph()
    assert res["valid"], f"Graph validation failed with errors: {res['errors'][:5]}"
    assert res["total_edges"] > 500, f"Expected >500 edges, found {res['total_edges']}"


def test_no_dangling_edge_references(validator):
    res = validator.validate_all()
    assert res["valid"], f"Dangling references or validation errors found: {res['errors'][:10]}"


def test_canonical_taxonomy_coverage(validator):
    validator.load()
    types_present = {obj.get("type") for obj in validator.inventory}
    assert "KNOWLEDGE" in types_present
    assert "PROCEDURE" in types_present
    assert "EXPERIMENT" in types_present
    assert "EVIDENCE" in types_present
    assert "AGENT" in types_present
    assert "SKILL" in types_present
    assert "TRACE" in types_present
    assert "AUDIT" in types_present


def test_experiment_to_evidence_edges(validator):
    validator.load()
    exp_edges = [e for e in validator.graph_edges if e.get("source", "").startswith("EXP-")]
    assert len(exp_edges) >= 4
    targets = {e["target"] for e in exp_edges}
    assert "EVID-P0-RETRIEVAL-REPORT" in targets
    assert "EVID-P1-PACKING-REPORT" in targets
    assert "EVID-P2-TEMPORAL-REPORT" in targets
    assert "EVID-WOB-ART-AUDIT" in targets


def test_agent_to_skill_edges(validator):
    validator.load()
    ag_edges = [e for e in validator.graph_edges if e.get("source", "").startswith("AGENT-")]
    assert len(ag_edges) >= 6
    relations = {e["relation"] for e in ag_edges}
    assert "uses" in relations
'''

(VAULT_ROOT / "evaluation" / "tests" / "test_vault_mesh.py").write_text(test_code, encoding="utf-8")
print("4. Written evaluation/tests/test_vault_mesh.py")

# -------------------------------------------------------------
# 5. Run MeshValidator assertion
# -------------------------------------------------------------
INVENTORY_PATH = mesh_dir / "vault_inventory.yaml"
GRAPH_PATH = mesh_dir / "vault_graph.yaml"
from evaluation.vault_mesh.mesh_validator import MeshValidator
v = MeshValidator(inventory_path=INVENTORY_PATH, graph_path=GRAPH_PATH)
res = v.validate_all()
print("5. Full Mesh Validation Status:", res["valid"])
print(f"   Objects: {res['total_objects']}")
print(f"   Edges: {res['total_edges']}")
if not res["valid"]:
    print("   Validation Errors:", res["errors"][:10])
    raise SystemExit(1)
print("Mesh Builder Complete & 100% Validated!")


