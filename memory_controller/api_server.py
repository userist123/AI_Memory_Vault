"""Local REST API Gateway for AI Memory Vault and JARVIS Command Center.
Zero external dependencies; binds to 127.0.0.1 by default.
"""
from __future__ import annotations

import datetime
import hashlib
import json
import os
import sys
import uuid
from pathlib import Path
from http.server import HTTPServer, BaseHTTPRequestHandler
from urllib.parse import parse_qs, urlparse

project_root = Path(__file__).resolve().parent.parent
if str(project_root) not in sys.path:
    sys.path.insert(0, str(project_root))

from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController
from memory_controller.storage.file_engine import FileStorageEngine
from cognitive_core.extraction import AtomicMemoryExtractor
from cognitive_core.proposal_queue import MemoryProposalQueue
from cognitive_core.queue_promoter import QueuePromoter


class APIJSONEncoder(json.JSONEncoder):
    def default(self, obj):
        if hasattr(obj, "value"):
            return obj.value
        if isinstance(obj, (datetime.date, datetime.datetime)):
            return obj.isoformat()
        return super().default(obj)


def _read_json(path: Path, fallback):
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return fallback


def _skill_catalog(root: Path):
    skills = []
    skill_root = root / ".agents" / "skills"
    if not skill_root.exists():
        return skills
    for skill_file in sorted(skill_root.glob("**/SKILL.md")):
        rel = skill_file.parent.relative_to(skill_root).as_posix()
        first = skill_file.read_text(encoding="utf-8", errors="ignore")[:400]
        name = rel
        for line in first.splitlines():
            if line.lower().startswith("name:"):
                name = line.split(":", 1)[1].strip().strip('"')
                break
        skills.append({"id": rel, "name": name, "path": skill_file.as_posix()})
    return skills


class BrowserMemoryAPIHandler(BaseHTTPRequestHandler):
    vault_root = Path(os.getenv("AI_MEMORY_VAULT_ROOT", str(project_root))).resolve()
    storage = FileStorageEngine(str(vault_root))
    controller = MemoryController(storage)
    queue = MemoryProposalQueue(vault_root / "06_INBOX" / "memory_proposals.jsonl")

    def log_message(self, format, *args):
        return

    def _set_headers(self, status=200):
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type, Authorization, Mcp-Version")
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.end_headers()

    def _json(self, status, payload):
        self._set_headers(status)
        self.wfile.write(json.dumps(payload, ensure_ascii=False, cls=APIJSONEncoder).encode("utf-8"))

    def _body(self):
        length = int(self.headers.get("Content-Length", 0) or 0)
        raw = self.rfile.read(length)
        return json.loads(raw.decode("utf-8")) if raw else {}

    def do_OPTIONS(self):
        self._set_headers(200)

    def do_GET(self):
        parsed = urlparse(self.path)
        path = parsed.path
        params = parse_qs(parsed.query)

        if path in {"/", "/api/v1/status"}:
            agents = _read_json(self.vault_root / "projects" / "jarvis_web" / "data" / "agents.json", {"agents": []}).get("agents", [])
            skills = _skill_catalog(self.vault_root)
            self._json(200, {
                "status": "online",
                "service": "AI Memory Vault Browser Gateway",
                "vault_root": str(self.vault_root),
                "indexed_notes": len(self.storage.id_to_path),
                "agents": len(agents),
                "skills": len(skills),
            })
            return

        if path == "/api/v1/metrics":
            agents = _read_json(self.vault_root / "projects" / "jarvis_web" / "data" / "agents.json", {"agents": []}).get("agents", [])
            skills = _skill_catalog(self.vault_root)
            counts = self.queue.status()
            pending = counts.get("PENDING_REVIEW", 0)
            self._json(200, {
                "memory_items": len(self.storage.id_to_path),
                "agents_online": sum(1 for a in agents if a.get("status") == "ONLINE"),
                "agents_total": len(agents),
                "skills_operational": len(skills),
                "proposals_pending": pending,
                "engine": "V6",
                "retrieval": "MemoryController",
            })
            return

        if path == "/api/v1/agents":
            self._json(200, _read_json(self.vault_root / "projects" / "jarvis_web" / "data" / "agents.json", {"agents": []}))
            return

        if path == "/api/v1/skills":
            skills = _skill_catalog(self.vault_root)
            query = params.get("q", [""])[0].lower().strip()
            if query:
                skills = [s for s in skills if query in s["id"].lower() or query in s["name"].lower()]
            self._json(200, {"total": len(skills), "skills": skills[:1000]})
            return

        if path == "/api/v1/proposals":
            records = self.queue._load()
            pending = [p for p in records if p.get("queue_status") == "PENDING_REVIEW"]
            self._json(200, {
                "total": len(records),
                "pending": pending[:100],
                "status": {
                    "PENDING_REVIEW": len(pending),
                    "APPROVED": sum(1 for p in records if p.get("queue_status") == "APPROVED"),
                    "REJECTED": sum(1 for p in records if p.get("queue_status") == "REJECTED"),
                    "PROMOTED": sum(1 for p in records if p.get("queue_status") == "PROMOTED"),
                },
            })
            return

        if path == "/api/v1/search":
            q = params.get("q", [""])[0].strip()
            notes = self.storage.query(intent=q)
            notes.sort(key=lambda n: str(n.get("updated", n.get("created", ""))), reverse=True)
            self._json(200, {"query": q, "total_results": len(notes), "results": notes[:20]})
            return

        if path.startswith("/api/v1/note/"):
            note_id = path.replace("/api/v1/note/", "", 1)
            note = self.storage.get(note_id)
            self._json(200 if note else 404, note if note else {"error": "Note not found", "id": note_id})
            return

        self._json(404, {"error": "Endpoint not found"})

    def do_POST(self):
        path = urlparse(self.path).path
        try:
            data = self._body()
        except (UnicodeDecodeError, json.JSONDecodeError):
            self._json(400, {"error": "Invalid JSON body"})
            return

        if path == "/api/v1/propose":
            content = str(data.get("content", "")).strip()
            kind = str(data.get("type", "fact")).lower()
            if not content:
                self._json(400, {"error": "content is required"})
                return
            if kind not in {"fact", "decision", "preference", "task", "lesson", "procedure"}:
                kind = "fact"
            extractor = AtomicMemoryExtractor()
            candidates = extractor._candidate(kind, content, "jarvis:web:proposal")
            added = self.queue.enqueue([candidates])
            self._json(201, {
                "status": "queued",
                "candidate_id": candidates.candidate_id,
                "added": added,
                "queue_status": "PENDING_REVIEW",
            })
            return

        if path.startswith("/api/v1/proposals/") and path.endswith("/decision"):
            candidate_id = path.split("/api/v1/proposals/", 1)[1].rsplit("/decision", 1)[0]
            decision = str(data.get("decision", "")).upper()
            if decision not in {"APPROVED", "REJECTED"}:
                self._json(400, {"error": "decision must be APPROVED or REJECTED"})
                return
            try:
                self.queue.mark(candidate_id, decision, reviewer="jarvis-human")
                self._json(200, {"status": decision, "candidate_id": candidate_id})
            except KeyError as exc:
                self._json(404, {"error": str(exc)})
            return

        if path == "/api/v1/proposals/promote-approved":
            promoter = QueuePromoter(self.queue, self.controller, Principal.ADMIN)
            promoted = promoter.promote_approved()
            self._json(200, {"status": "promoted", "ids": promoted})
            return

        if path == "/api/v1/route":
            task = str(data.get("task", "")).strip().lower()
            agents = _read_json(self.vault_root / "projects" / "jarvis_web" / "data" / "agents.json", {"agents": []}).get("agents", [])
            if not task:
                self._json(400, {"error": "task is required"})
                return
            tokens = {t for t in task.replace("/", " ").replace("-", " ").split() if len(t) > 2}
            scored = []
            for agent in agents:
                haystack = " ".join([
                    str(agent.get("id", "")), str(agent.get("name", "")), str(agent.get("domain", "")),
                    " ".join(agent.get("skills", [])),
                ]).lower()
                matched = sorted(token for token in tokens if token in haystack)
                scored.append({**agent, "route_score": len(matched), "matched_terms": matched})
            scored.sort(key=lambda item: (-item["route_score"], item.get("name", "")))
            self._json(200, {"task": task, "selected": scored[:5], "routing": "domain-and-skill-match"})
            return

        self._json(404, {"error": "Endpoint not found"})


def run_server(port=8000):
    server_address = ("127.0.0.1", port)
    httpd = HTTPServer(server_address, BrowserMemoryAPIHandler)
    print(f"[BROWSER GATEWAY] Running REST API server at http://127.0.0.1:{port}...")
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("[BROWSER GATEWAY] Stopping server.")
        httpd.server_close()


if __name__ == "__main__":
    port = 8000
    if len(sys.argv) > 1:
        try:
            port = int(sys.argv[1])
        except ValueError:
            pass
    run_server(port)
