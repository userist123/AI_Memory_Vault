"""Optional semantic retrieval: Ollama embeddings + Qdrant vector search.

Both Ollama and Qdrant are accessed via local HTTP (urllib), matching the
zero-external-dependency style used throughout Memory V6. On any connection
or parsing failure this module degrades to an empty result list; it never
raises into the existing search() pipeline and never mutates canonical memory.
"""
from __future__ import annotations

import hashlib
import json
import urllib.error
import urllib.request
from typing import Any, Dict, Iterable, List, Optional, Tuple


class OllamaEmbedder:
    """Calls a local Ollama embedding model (e.g. nomic-embed-text)."""

    def __init__(self, model: str = "nomic-embed-text", host: str = "http://localhost:11434",
                 timeout_seconds: int = 15):
        self.model = model
        self.host = host.rstrip("/")
        self.timeout_seconds = timeout_seconds

    def embed(self, text: str) -> Optional[List[float]]:
        payload = {"model": self.model, "prompt": text}
        request = urllib.request.Request(
            url=f"{self.host}/api/embeddings",
            data=json.dumps(payload).encode("utf-8"),
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        try:
            with urllib.request.urlopen(request, timeout=self.timeout_seconds) as response:
                raw = json.loads(response.read().decode("utf-8"))
        except (urllib.error.URLError, OSError, ValueError):
            return None
        vector = raw.get("embedding")
        return vector if isinstance(vector, list) else None


class QdrantIndex:
    """Thin REST client for a local/remote Qdrant instance. No qdrant-client dependency."""

    def __init__(self, collection: str = "vault_memory", host: str = "http://localhost:6333",
                 vector_size: int = 768, timeout_seconds: int = 15):
        self.collection = collection
        self.host = host.rstrip("/")
        self.vector_size = vector_size
        self.timeout_seconds = timeout_seconds

    def _request(self, method: str, path: str, body: Optional[dict] = None) -> Optional[dict]:
        data = json.dumps(body).encode("utf-8") if body is not None else None
        request = urllib.request.Request(
            url=f"{self.host}{path}",
            data=data,
            headers={"Content-Type": "application/json"},
            method=method,
        )
        try:
            with urllib.request.urlopen(request, timeout=self.timeout_seconds) as response:
                return json.loads(response.read().decode("utf-8"))
        except (urllib.error.URLError, OSError, ValueError):
            return None

    def ensure_collection(self) -> bool:
        path = f"/collections/{self.collection}"
        existing = self._request("GET", path)
        if existing is not None:
            vectors = (existing.get("result", {})
                       .get("config", {})
                       .get("params", {})
                       .get("vectors", {}))
            return (isinstance(vectors, dict)
                    and vectors.get("size") == self.vector_size
                    and str(vectors.get("distance", "")).lower() == "cosine")
        result = self._request("PUT", path, {
            "vectors": {"size": self.vector_size, "distance": "Cosine"}
        })
        return result is not None and result.get("status") == "ok"

    @staticmethod
    def _stable_point_id(point_id: str) -> int:
        """Derive a process-independent Qdrant point ID from the canonical note ID."""
        digest = hashlib.sha256(str(point_id).encode("utf-8")).digest()
        return int.from_bytes(digest[:8], "big") & ((1 << 63) - 1)

    def upsert(self, points: Iterable[Tuple[str, List[float], Dict[str, Any]]]) -> bool:
        payload_points = [
            {"id": self._stable_point_id(point_id), "vector": vector,
             "payload": {**payload, "note_id": point_id}}
            for point_id, vector, payload in points
        ]
        if not payload_points:
            return True
        result = self._request("PUT", f"/collections/{self.collection}/points", {"points": payload_points})
        return result is not None and result.get("status") == "ok"

    def scroll_points(self, page_size: int = 100) -> Optional[List[dict]]:
        """Return all points, or None when enumeration is incomplete."""
        if page_size < 1:
            return None
        points: List[dict] = []
        offset = None
        seen_offsets = set()
        while True:
            body = {"limit": page_size, "with_payload": True, "with_vector": False}
            if offset is not None:
                body["offset"] = offset
            result = self._request("POST", f"/collections/{self.collection}/points/scroll", body)
            page = result.get("result") if isinstance(result, dict) else None
            if not isinstance(page, dict) or not isinstance(page.get("points"), list):
                return None
            if not all(isinstance(point, dict) for point in page["points"]):
                return None
            points.extend(page["points"])
            offset = page.get("next_page_offset")
            if offset is None:
                return points
            marker = json.dumps(offset, sort_keys=True)
            if marker in seen_offsets:
                return None
            seen_offsets.add(marker)

    def delete_points(self, point_ids: Iterable[Any]) -> bool:
        ids = list(point_ids)
        if not ids:
            return True
        result = self._request("POST", f"/collections/{self.collection}/points/delete",
                               {"points": ids})
        return result is not None and result.get("status") == "ok"

    def search(self, vector: List[float], top_k: int = 10) -> List[str]:
        result = self._request("POST", f"/collections/{self.collection}/points/search", {
            "vector": vector, "limit": top_k, "with_payload": True,
        })
        if not result:
            return []
        hits = result.get("result", [])
        return [hit["payload"]["note_id"] for hit in hits if hit.get("payload", {}).get("note_id")]


class SemanticRetrieval:
    """Embeds canonical ACTIVE/VERIFIED notes and serves semantic search over them.

    Reindexing reconciles eligible notes without mutating canonical memory.
    """

    def __init__(self, controller, embedder: Optional[OllamaEmbedder] = None,
                 index: Optional[QdrantIndex] = None):
        self.controller = controller
        self.embedder = embedder or OllamaEmbedder()
        self.index = index or QdrantIndex()

    def reindex(self) -> int:
        if not self.index.ensure_collection():
            return 0
        notes = {str(n["id"]): n for n in self.controller.storage.store.values()
                 if n.get("id") is not None
                 and n.get("lifecycle") in {"ACTIVE", "VERIFIED"}
                 and n.get("content")}
        existing = self.index.scroll_points()
        if existing is None:
            return 0
        points = []
        for note_id, note in notes.items():
            vector = self.embedder.embed(str(note["content"]))
            if vector is None:
                continue
            points.append((note_id, vector, {"category": note.get("category", "")}))
        if points and not self.index.upsert(points):
            return 0
        embedded = {note_id for note_id, _, _ in points}
        stale = []
        for point in existing:
            payload = point.get("payload")
            point_id = point.get("id")
            note_id = payload.get("note_id") if isinstance(payload, dict) else None
            if not isinstance(note_id, str) or point_id is None:
                continue
            if note_id not in notes or (note_id in embedded and
                                        point_id != self.index._stable_point_id(note_id)):
                stale.append(point_id)
        if stale and not self.index.delete_points(stale):
            return 0
        return len(points)

    def query(self, text: str, top_k: int = 10) -> List[str]:
        vector = self.embedder.embed(text)
        if vector is None:
            return []
        return self.index.search(vector, top_k=top_k)
