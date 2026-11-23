"""The ``app`` ProjectKind (React + TypeScript + Vite web-core app, DESIGN.md sections 1 and 3).

The agent can write only the files accepted by ``appfiles`` (src/** and public/**). The studio-owned kit
(package.json, vite/capacitor config, index.html, ...) is never part of the project's files and is never written
by ``build`` here - it is copied into the build context by the build pipeline (a later slice).
"""

from __future__ import annotations

from typing import Any

from . import app_templates, appfiles, project_kind
from .errors import ValidationFailed
from .project_kind import Publication
from .workspace import Workspace

_MAIN_TSX = """import { createRoot } from "react-dom/client";
import App from "./App";

createRoot(document.getElementById("root")!).render(<App />);
"""

_APP_TSX = """export default function App() {
  return (
    <main>
      <h1>Hello from AI App Studio</h1>
      <img src="/icon.svg" alt="" width={64} height={64} />
    </main>
  );
}
"""

_ICON_SVG = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" width="64" height="64">
  <rect width="64" height="64" rx="14" fill="#2563eb"/>
  <path d="M20 44 32 18l12 26" fill="none" stroke="#ffffff" stroke-width="5" stroke-linejoin="round"/>
</svg>
"""

BLANK_FILES = {"src/main.tsx": _MAIN_TSX, "src/App.tsx": _APP_TSX, "public/icon.svg": _ICON_SVG}
TEMPLATE_FILES: dict[str, dict[str, str]] = {"blank": BLANK_FILES, **app_templates.TEMPLATES}  # S9b: tabs, list-detail


class AppKind:
    name = "app"
    max_files = appfiles.MAX_FILES
    max_total_bytes = appfiles.MAX_TOTAL_BYTES
    max_file_bytes = appfiles.MAX_FILE_BYTES
    templates = frozenset(TEMPLATE_FILES)
    file_roots = ("src", "public")  # committed to the project's git repo with model/project.json (F3)

    def initial_settings(self, template: str) -> dict[str, Any]:
        if template not in self.templates:
            raise ValidationFailed(f"unknown template {template!r}", available=sorted(self.templates))
        return {"files": {p: {"content": c, "language": appfiles.language_of(p)} for p, c in TEMPLATE_FILES[template].items()}}

    def validate(self, path: str, content: str | None = None) -> dict[str, Any]:
        appfiles.validate_path(path)
        if content is None:
            return {}
        appfiles.validate_content(path, content)
        return {"language": appfiles.language_of(path)}

    def build(self, project: dict[str, Any], snapshot: dict[str, Any], ws: Workspace) -> None:
        """Mirror the revision's files into the workspace. The snapshot is untrusted: every path and content is
        re-validated, and files on disk that the snapshot does not list are removed from src/ and public/."""
        files = ((snapshot.get("project") or {}).get("settings") or {}).get("files") or {}
        if snapshot.get("components"):
            raise ValidationFailed("the app kind has no components")
        if not isinstance(files, dict) or len(files) > self.max_files:
            raise ValidationFailed("invalid or too many files in snapshot")
        total = 0
        for path, f in files.items():
            self.validate(path)
            content = f.get("content") if isinstance(f, dict) else None
            total += appfiles.validate_content(path, content)  # type: ignore[arg-type]
        if total > self.max_total_bytes:
            raise ValidationFailed("snapshot exceeds the total size limit")
        for path, f in files.items():
            ws.write_text(path, f["content"])
        wanted = set(files)
        for root in self.file_roots:
            for rel in ws.iter_files(root):
                if rel not in wanted:
                    ws.resolve(rel).unlink(missing_ok=True)

    def publish(self, project: dict[str, Any], ws: Workspace) -> Publication:
        return Publication(root="src", required=("main.tsx", "App.tsx"))


_KIND = AppKind()


def register_app_kind() -> AppKind:
    """Idempotent: registers the process-wide AppKind singleton."""
    project_kind.register(_KIND)
    return _KIND
