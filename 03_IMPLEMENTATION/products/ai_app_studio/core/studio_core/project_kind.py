"""ProjectKind seam (AAS-owned, not vendored).

The vendored Studio core (services.py) no longer imports website modules (renderer, templates, component_model,
codefiles, scene3d). Everything a project *kind* decides goes through this protocol instead:

- ``validate``  : gate for a code-file write (path, and content when given) - called before any lock/transaction;
- ``build``     : materialise a revision snapshot into the project workspace (was ``Studio._render``);
- ``publish``   : name the workspace subtree that ``Studio.export_project`` archives and what it must contain.

Kinds are registered by name in a process-local registry; ``Studio`` resolves its kind on use, so an unregistered
kind fails closed with ``Unavailable`` instead of falling back to anything. Only ``NullKind`` exists so far (tests);
the ``app`` kind arrives in a later slice (docs/DESIGN.md section 1).
"""

from __future__ import annotations

import html
import re
import threading
from dataclasses import dataclass, field
from typing import Any, Protocol, runtime_checkable

from .errors import Unavailable, ValidationFailed
from .workspace import Workspace

DEFAULT_KIND = "app"
_NAME_RE = re.compile(r"^[a-z][a-z0-9_]{0,31}$")


@dataclass(frozen=True)
class Publication:
    """What ``export_project`` archives: files under ``root`` (workspace-relative), minus ``exclude``;
    the export is ``verified`` only when every name in ``required`` (archive-relative) is present."""

    root: str
    required: tuple[str, ...]
    exclude: frozenset[str] = field(default_factory=frozenset)

    def __post_init__(self) -> None:
        if not isinstance(self.required, tuple) or not self.required or not all(
                isinstance(r, str) and r for r in self.required):
            raise ValueError("Publication.required must be a non-empty tuple of non-empty names")


@runtime_checkable
class ProjectKind(Protocol):
    name: str
    max_files: int
    max_total_bytes: int

    def initial_settings(self, template: str) -> dict[str, Any]:
        """Project settings for a new project; raises ValidationFailed for an unknown template."""
        ...

    def validate(self, path: str, content: str | None = None) -> dict[str, Any]:
        """Check a code-file path (and content when given). Returns file metadata stored next to the content
        (at least ``language`` when content is given). Raises ValidationFailed."""
        ...

    def build(self, project: dict[str, Any], snapshot: dict[str, Any], ws: Workspace) -> None:
        """Materialise a revision snapshot into the workspace (runs inside the revision transaction).

        The snapshot is UNTRUSTED input: restore_revision, restore_from_git and recover_from_git replay settings
        (including code files) from stored revisions or a git commit without calling ``validate`` again, so build
        must re-check every path/content it writes (or call ``validate`` itself) and never trust snapshot data."""
        ...

    def publish(self, project: dict[str, Any], ws: Workspace) -> Publication:
        """Describe the exported subtree of the workspace."""
        ...


_lock = threading.Lock()
_registry: dict[str, ProjectKind] = {}


def register(kind: ProjectKind) -> ProjectKind:
    if not isinstance(kind, ProjectKind):
        raise TypeError("object does not implement ProjectKind")
    if not isinstance(kind.name, str) or not _NAME_RE.match(kind.name):
        raise ValueError(f"invalid project kind name {kind.name!r}")
    with _lock:
        existing = _registry.get(kind.name)
        if existing is not None and existing is not kind:
            raise ValueError(f"project kind {kind.name!r} is already registered")
        _registry[kind.name] = kind
    return kind


def unregister(name: str) -> None:
    with _lock:
        _registry.pop(name, None)


def registered() -> list[str]:
    with _lock:
        return sorted(_registry)


def resolve(name: str) -> ProjectKind:
    with _lock:
        kind = _registry.get(name)
    if kind is None:
        raise Unavailable(f"project kind {name!r} is not registered", kind=name, available=registered())
    return kind


class NullKind:
    """Minimal kind for tests of the vendored core: plain-text files, a static placeholder page, no components."""

    name = "null"
    max_files = 20
    max_total_bytes = 256 * 1024
    max_file_bytes = 64 * 1024
    templates = frozenset({"blank"})
    _PATH = re.compile(r"^[a-z0-9][a-z0-9_\-]{0,40}(/[a-z0-9][a-z0-9_\-]{0,40}){0,3}\.txt$")

    def initial_settings(self, template: str) -> dict[str, Any]:
        if template not in self.templates:
            raise ValidationFailed(f"unknown template {template!r}", available=sorted(self.templates))
        return {}

    def validate(self, path: str, content: str | None = None) -> dict[str, Any]:
        if not isinstance(path, str) or not self._PATH.match(path):
            raise ValidationFailed("file path must look like 'notes/a.txt' (lowercase, max 4 folders)")
        if content is None:
            return {}
        if not isinstance(content, str) or "\x00" in content:
            raise ValidationFailed("content must be text without NUL characters")
        if len(content.encode("utf-8")) > self.max_file_bytes:
            raise ValidationFailed(f"file exceeds {self.max_file_bytes} bytes")
        return {"language": "text"}

    def build(self, project: dict[str, Any], snapshot: dict[str, Any], ws: Workspace) -> None:
        if snapshot.get("components"):
            raise ValidationFailed("the null kind has no components")
        page = (f"<!doctype html><html><head><meta charset=\"utf-8\"><title>{html.escape(project['name'])}</title>"
                f"</head><body data-revision=\"{int(project['revision'])}\"></body></html>\n")
        ws.write_text("site/index.html", page)
        ws.write_text("site/.revision", str(int(project["revision"])))

    def publish(self, project: dict[str, Any], ws: Workspace) -> Publication:
        return Publication(root="site", required=("index.html",), exclude=frozenset({"site/.revision"}))
