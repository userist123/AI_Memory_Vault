"""Optional semantic retrieval: Ollama embeddings + Qdrant vector search.

Both Ollama and Qdrant are accessed via local HTTP (urllib), matching the
zero-external-dependency style used throughout Memory V6. On any connection
or parsing failure this module degrades to an empty result list; it never
raises into the existing search() pipeline and never mutates canonical memory.
"""
from __future__ import annotations

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
        result = self._request("PUT", f"/collections/{self.collection}", {
            "vectors": {"size": self.vector_size, "distance": "Cosine"}
        })
        return result is not None

    def upsert(self, points: Iterable[Tuple[str, List[float], Dict[str, Any]]]) -> bool:
        payload_points = [
            {"id": abs(hash(point_id)) % (2 ** 31), "vector": vector, "payload": {**payload, "note_id": point_id}}
            for point_id, vector, payload in points
        ]
        if not payload_points:
            return True
        result = self._request("PUT", f"/collections/{self.collection}/points", {"points": payload_points})
        return result is not None

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

    Fully optional: if Ollama or Qdrant are unreachable, reindex() and query()
    both degrade to no-ops / empty lists rather than raising.
    """

    def __init__(self, controller, embedder: Optional[OllamaEmbedder] = None,
                 index: Optional[QdrantIndex] = None):
        self.controller = controller
        self.embedder = embedder or OllamaEmbedder()
        self.index = index or QdrantIndex()

    def reindex(self) -> int:
        self.index.ensure_collection()
        notes = [
            n for n in self.controller.storage.store.values()
            if n.get("lifecycle") in {"ACTIVE", "VERIFIED"} and n.get("content")
        ]
        points = []
        for note in notes:
            vector = self.embedder.embed(str(note["content"]))
            if vector is None:
                continue
            points.append((note["id"], vector, {"category": note.get("category", "")}))
        if points:
            self.index.upsert(points)
        return len(points)

    def query(self, text: str, top_k: int = 10) -> List[str]:
        vector = self.embedder.embed(text)
        if vector is None:
            return []
        return self.index.search(vector, top_k=top_k)
