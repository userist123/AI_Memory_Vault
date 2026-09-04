"""Multi-graph memory: orthogonal semantic, temporal, causal, and entity graphs.

Derived, rebuildable indexes over canonical notes. Never a source of truth;
always reconstructible from Markdown + MemoryController storage.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime
from typing import Any, Dict, Iterable, List, Set, Tuple
import re

_ENTITY_RE = re.compile(r"\b[A-Z][a-zA-Z0-9_\-]{2,}\b")
_CAUSAL_RELATIONS = {"causes", "leads_to", "replaced_by", "replaces", "depends_on", "blocks"}

CONTROLLED_NODE_TYPES = {
    "fact",
    "decision",
    "procedure",
    "lesson",
    "task",
    "intent",
    "tool",
    "failure",
    "correction",
    "outcome",
}

DEFAULT_CATEGORY_NODE_TYPES = {
    "architecture": "decision",
    "architecture-review": "decision",
    "system-architecture": "decision",
    "enterprise-architecture": "decision",
    "decisions": "decision",
    "decision": "decision",
    "procedures": "procedure",
    "procedure": "procedure",
    "protocol": "procedure",
    "rules": "procedure",
    "policy-lesson": "lesson",
    "lessons": "lesson",
    "lesson": "lesson",
    "experiences": "lesson",
    "errors": "failure",
    "failure": "failure",
    "correction": "correction",
    "outcome": "outcome",
    "task": "task",
    "goals": "task",
    "intent": "intent",
    "tool": "tool",
    "soc-tooling": "tool",
    "session": "fact",
    "knowledge": "fact",
    "consolidated-knowledge": "fact",
    "audit": "fact",
    "secops": "fact",
    "security": "fact",
    "memory": "fact",
    "projects": "fact",
    "resources": "fact",
}


def validate_node_type(node_type: str, allow_custom: bool = True) -> str:
    """Validates node_type against controlled vocabulary or custom identifier."""
    if not isinstance(node_type, str):
        raise ValueError(f"node_type must be a string, got {type(node_type).__name__}")
    cleaned = node_type.strip().lower()
    if not cleaned:
        raise ValueError("node_type cannot be empty")
    if cleaned in CONTROLLED_NODE_TYPES:
        return cleaned
    if allow_custom and re.match(r"^[a-z0-9_\-]+$", cleaned):
        return cleaned
    raise ValueError(
        f"Invalid node_type '{node_type}'. Expected one of {sorted(CONTROLLED_NODE_TYPES)} or valid custom identifier."
    )


def resolve_node_type(note: Dict[str, Any]) -> str:
    """Resolves node_type for a note.

    If explicit node_type is provided, validates it.
    Otherwise maps from category or defaults to 'fact' with zero destructive migration.
    """
    raw_node_type = note.get("node_type")
    if raw_node_type is not None:
        return validate_node_type(str(raw_node_type))
    cat = str(note.get("category", "")).strip().lower()
    return DEFAULT_CATEGORY_NODE_TYPES.get(cat, "fact")


@dataclass
class Graph:
    """Simple directed labeled multigraph with typed nodes."""

    name: str
    nodes: Set[str] = field(default_factory=set)
    node_types: Dict[str, str] = field(default_factory=dict)
    edges: List[Tuple[str, str, Dict[str, Any]]] = field(default_factory=list)

    def add_node(self, node_id: str, node_type: Optional[str] = None) -> None:
        self.nodes.add(node_id)
        if node_type is not None:
            self.node_types[node_id] = validate_node_type(node_type)
        elif node_id not in self.node_types:
            self.node_types[node_id] = "fact"

    def get_node_type(self, node_id: str) -> Optional[str]:
        return self.node_types.get(node_id)

    def add_edge(self, source: str, target: str, **attrs: Any) -> None:
        self.add_node(source)
        self.add_node(target)
        self.edges.append((source, target, attrs))

    def neighbors(self, node_id: str) -> List[Tuple[str, Dict[str, Any]]]:
        result = []
        for source, target, attrs in self.edges:
            if source == node_id:
                result.append((target, attrs))
            elif target == node_id:
                result.append((source, attrs))
        return result

    def to_dict(self) -> dict:
        return {
            "name": self.name,
            "nodes": sorted(self.nodes),
            "node_types": dict(self.node_types),
            "edges": [{"source": s, "target": t, **a} for s, t, a in self.edges],
        }


class MultiGraphMemory:
    """Holds four orthogonal graphs derived from the same note corpus."""

    def __init__(self) -> None:
        self.semantic = Graph("semantic")
        self.temporal = Graph("temporal")
        self.causal = Graph("causal")
        self.entity = Graph("entity")

    def _extract_entities(self, text: str) -> Set[str]:
        return set(_ENTITY_RE.findall(text or ""))

    def build_from_notes(self, notes: Iterable[Dict[str, Any]]) -> "MultiGraphMemory":
        self.semantic = Graph("semantic")
        self.temporal = Graph("temporal")
        self.causal = Graph("causal")
        self.entity = Graph("entity")

        notes_list = [n for n in notes if n.get("id")]
        for note in notes_list:
            ntype = resolve_node_type(note)
            self.semantic.add_node(note["id"], node_type=ntype)
            self.temporal.add_node(note["id"], node_type=ntype)
            self.causal.add_node(note["id"], node_type=ntype)
            self.entity.add_node(note["id"], node_type=ntype)

        # Semantic graph: shared category or overlapping tags
        for i, a in enumerate(notes_list):
            for b in notes_list[i + 1:]:
                a_tags, b_tags = set(a.get("tags") or []), set(b.get("tags") or [])
                shared_tags = a_tags & b_tags
                same_category = a.get("category") and a.get("category") == b.get("category")
                if shared_tags or same_category:
                    weight = len(shared_tags) + (0.5 if same_category else 0.0)
                    self.semantic.add_edge(a["id"], b["id"], relation="semantic", weight=weight)

        # Temporal graph: chain notes within the same category by created date
        by_category: Dict[str, List[Dict[str, Any]]] = {}
        for note in notes_list:
            by_category.setdefault(note.get("category", "uncategorized"), []).append(note)
        for category_notes in by_category.values():
            def _created(n: Dict[str, Any]) -> str:
                return str(n.get("created") or n.get("updated") or "")
            ordered = sorted(category_notes, key=_created)
            for earlier, later in zip(ordered, ordered[1:]):
                self.temporal.add_edge(earlier["id"], later["id"], relation="precedes", weight=1.0)

        # Causal graph: explicit relations field on notes
        for note in notes_list:
            for relation in note.get("relations", []) or []:
                rel_type = str(relation.get("relation", "")).lower()
                target_id = relation.get("target_id")
                if rel_type in _CAUSAL_RELATIONS and target_id:
                    self.causal.add_edge(note["id"], target_id, relation=rel_type, weight=1.0)

        # Entity graph: notes sharing extracted capitalized entities
        entity_index: Dict[str, Set[str]] = {}
        for note in notes_list:
            entities = self._extract_entities(str(note.get("content", "")))
            entity_index[note["id"]] = entities
        ids = list(entity_index.keys())
        for i, note_id_a in enumerate(ids):
            for note_id_b in ids[i + 1:]:
                shared = entity_index[note_id_a] & entity_index[note_id_b]
                if shared:
                    self.entity.add_edge(note_id_a, note_id_b, relation="shared_entity",
                                          weight=len(shared), entities=sorted(shared))

        return self

    def to_dict(self) -> dict:
        return {
            "semantic": self.semantic.to_dict(),
            "temporal": self.temporal.to_dict(),
            "causal": self.causal.to_dict(),
            "entity": self.entity.to_dict(),
        }
