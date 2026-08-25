"""Rebuildable repository spatial index; derived metadata is never canonical memory."""
from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path
from typing import Dict, List, Set
import ast
import json
import re


class SpatialIndex:
    def __init__(self, vault_root: str | Path):
        self.root = Path(vault_root).resolve()
        self.nodes: Dict[str, dict] = {}
        self.edges: List[dict] = []

    def _relative(self, path: Path) -> str:
        return path.relative_to(self.root).as_posix()

    def _add_edge(self, source: str, target: str, relation: str) -> None:
        edge = {"source": source, "target": target, "relation": relation}
        if edge not in self.edges:
            self.edges.append(edge)

    def _python_imports(self, path: Path) -> Set[str]:
        try:
            tree = ast.parse(path.read_text(encoding="utf-8", errors="replace"))
        except SyntaxError:
            return set()
        imports: Set[str] = set()
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                imports.update(alias.name for alias in node.names)
            elif isinstance(node, ast.ImportFrom) and node.module:
                imports.add(node.module)
        return imports

    def rebuild(self) -> dict:
        self.nodes.clear()
        self.edges.clear()
        markdown_link = re.compile(r"\[\[([^\]]+)\]\]|\[[^\]]+\]\(([^)]+)\)")
        for path in self.root.rglob("*"):
            if not path.is_file() or ".git" in path.parts or "__pycache__" in path.parts:
                continue
            rel = self._relative(path)
            node = {"path": rel, "extension": path.suffix.lower(), "size": path.stat().st_size}
            self.nodes[rel] = node
            if path.suffix == ".py":
                imports = sorted(self._python_imports(path))
                node["imports"] = imports
                for imported in imports:
                    self._add_edge(rel, imported, "imports")
            elif path.suffix.lower() == ".md":
                text = path.read_text(encoding="utf-8", errors="replace")
                links = []
                for match in markdown_link.finditer(text):
                    target = match.group(1) or match.group(2)
                    links.append(target)
                    self._add_edge(rel, target, "references")
                node["links"] = links
            parts = set(path.parts)
            if "agents" in parts:
                node["domain"] = "agent"
            elif "skills" in parts:
                node["domain"] = "skill"
            elif "03_PROCEDURES" in parts:
                node["domain"] = "procedure"
            elif "04_MEMORY" in parts:
                node["domain"] = "memory"
            elif "cognitive_core" in parts:
                node["domain"] = "cognitive_core"
        return self.to_dict()

    def query_path(self, term: str) -> List[dict]:
        needle = term.lower()
        return [node for path, node in self.nodes.items() if needle in path.lower()]

    def to_dict(self) -> dict:
        return {"generated_at": datetime.now(timezone.utc).isoformat(),
                "root": str(self.root), "nodes": self.nodes, "edges": self.edges}

    def save(self, output_path: str | Path) -> Path:
        target = Path(output_path)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(self.to_dict(), ensure_ascii=False, indent=2), encoding="utf-8")
        return target
