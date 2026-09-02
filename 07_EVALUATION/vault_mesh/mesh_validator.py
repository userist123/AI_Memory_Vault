"""evaluation/vault_mesh/mesh_validator.py — Deterministic Validator for Cognitive Memory Mesh."""
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
                    if not re.match(r"^\d{4}-\d{2}-\d{2}(T\d{2}:\d{2}:\d{2}(Z|[+-]\d{2}:\d{2})?)?$", str(val).strip()):
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
