"""HybridRetriever — P1.2 semantic cortex prototype (owner: claude-code).

Status: EXPERIMENTAL, NOT wired into MemoryController.search() or any runtime
retrieval path. This is a standalone, offline retriever used only by the
30_SCRIPTS/knowledge tools and by cognitive_core/benchmarks/retrieval_ab.py.

Replaces plain token-overlap (Jaccard) scoring with multi-signal Reciprocal Rank
Fusion over up to three independent retrievers:

  1. BM25Okapi lexical (pure Python, zero dependencies, air-gap safe)
  2. Entity/acronym matching (technical identifiers, invariant codes, CamelCase,
     snake_case, dotted version numbers) — a signal token-overlap misses entirely
  3. Dense embeddings — optional, via local Ollama (nomic-embed-text/bge-m3).
     Its absence degrades gracefully; it is never a hard requirement.

RRF: score(d) = Sum_r w_r / (k + rank_r(d)). This does NOT require calibrating
raw scores between retrievers (unlike a weighted sum of raw scores) because
fusion operates purely on each retriever's RANK, not its raw score scale.
"""
from __future__ import annotations

import json
import math
import re
import time
import urllib.error
import urllib.request
from collections import Counter, defaultdict
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any, Dict, Iterable, List, Optional, Sequence, Tuple

from .vault_index import Note, VaultIndex

TOKEN_RE = re.compile(r"[a-z0-9][a-z0-9_\-\.]*")
# Entity/identifier heuristics. NOTE: the "version" branch requires three
# dot-separated groups (X.Y.Z), not two (X.Y). A two-part pattern also matches
# generic decimal literals (probabilities, thresholds, scores like "0.15",
# "0.6") that are common across unrelated documents and were empirically found
# to produce spurious high-confidence "shared rare entity" matches in
# edge_proposer.py (20/1636 proposals on the real vault had evidence composed
# entirely of such decimals). Real version identifiers (e.g. "1.2.3", "3.14.2")
# still match; bare two-part decimals like "3.14" no longer do. This is a
# deliberate precision-over-recall tradeoff, made after measuring the false
# positive on real data, not a theoretical guess.
ENTITY_RE = re.compile(
    r"\b(?:[A-Z][a-z0-9]+(?:[A-Z][a-z0-9]+)+"              # CamelCase
    r"|[A-Z]{2,}(?:[-_][A-Z0-9]+)*"                         # ACRONYM, TOKEN-1, BM25, RRF
    r"|[A-Z][0-9]*(?:[-_][A-Z0-9]+)+"                       # I-001, P0-015, SHA-256
    r"|[a-z_]+_[a-z_]+"                                     # snake_case
    r"|\d+\.\d+\.\d+)\b"                                    # three-part versions only
)
STOP = set(
    "the a an and or of to in is are was were be been for on with as by at from "
    "this that it its not no if then than but can will should must do does si "
    "sa la de pe cu un o in este sunt care pentru din nu se al ale".split()
)


def tokenize(text: str) -> List[str]:
    return [t for t in TOKEN_RE.findall(text.lower()) if t not in STOP and len(t) > 1]


def entities(text: str) -> set:
    return {e.lower() for e in ENTITY_RE.findall(text)}


class BM25:
    """Minimal, deterministic BM25Okapi."""

    def __init__(self, corpus: Sequence[Sequence[str]], k1: float = 1.5, b: float = 0.75):
        self.k1, self.b = k1, b
        self.docs = [Counter(d) for d in corpus]
        self.lens = [sum(c.values()) for c in self.docs]
        self.avgdl = (sum(self.lens) / len(self.lens)) if self.lens else 0.0
        df: Counter = Counter()
        for c in self.docs:
            df.update(c.keys())
        n = len(self.docs)
        self.idf = {
            t: math.log(1 + (n - v + 0.5) / (v + 0.5)) for t, v in df.items()
        }

    def scores(self, query: Sequence[str]) -> List[float]:
        out = [0.0] * len(self.docs)
        for i, doc in enumerate(self.docs):
            dl = self.lens[i] or 1
            s = 0.0
            for term in query:
                tf = doc.get(term)
                if not tf:
                    continue
                denom = tf + self.k1 * (1 - self.b + self.b * dl / (self.avgdl or 1))
                s += self.idf.get(term, 0.0) * tf * (self.k1 + 1) / denom
            out[i] = s
        return out


class DenseProviderUnavailableError(RuntimeError):
    """Raised when the local dense embedding provider (e.g. Ollama) is offline/unavailable."""
    pass


class OllamaEmbedder:
    """Local embeddings via Ollama. A network failure disables it
    (self.available=False, self.status="DENSE_PROVIDER_UNAVAILABLE") rather than
    silently succeeding. Callers MUST check `.available` or status; never present
    a degraded run as if dense embeddings had succeeded.
    """

    def __init__(self, model: str = "nomic-embed-text",
                 host: str = "http://localhost:11434", timeout: float = 10.0):
        self.model, self.host, self.timeout = model, host, timeout
        self.available = True
        self.status = "AVAILABLE"

    def check_availability(self) -> bool:
        """Probe Ollama without running a full embedding."""
        req = urllib.request.Request(f"{self.host}/api/tags")
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                self.available = (resp.status == 200)
                self.status = "AVAILABLE" if self.available else "DENSE_PROVIDER_UNAVAILABLE"
                return self.available
        except Exception:
            self.available = False
            self.status = "DENSE_PROVIDER_UNAVAILABLE"
            return False

    def embed(self, text: str) -> Optional[List[float]]:
        if not self.available:
            return None
        payload = json.dumps({"model": self.model, "prompt": text[:8000]}).encode()
        req = urllib.request.Request(
            f"{self.host}/api/embeddings", data=payload,
            headers={"Content-Type": "application/json"},
        )
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                return json.loads(resp.read()).get("embedding")
        except (urllib.error.URLError, OSError, ValueError, TimeoutError):
            self.available = False
            self.status = "DENSE_PROVIDER_UNAVAILABLE"
            return None


def cosine(a: Sequence[float], b: Sequence[float]) -> float:
    num = sum(x * y for x, y in zip(a, b))
    na = math.sqrt(sum(x * x for x in a)) or 1.0
    nb = math.sqrt(sum(y * y for y in b)) or 1.0
    return num / (na * nb)


@dataclass
class Hit:
    note: Note
    score: float
    signals: Dict[str, int]  # retriever name -> rank (1-based); for the observed trace


class HybridRetriever:
    DEFAULT_WEIGHTS = {"bm25": 1.0, "entity": 0.8, "dense": 1.2}
    RRF_K = 60

    def __init__(self, index: VaultIndex, embedder: Optional[OllamaEmbedder] = None,
                 weights: Optional[Dict[str, float]] = None):
        self.index = index
        self.notes = index.notes
        self.weights = dict(weights or self.DEFAULT_WEIGHTS)
        self._tokens = [tokenize(n.text) for n in self.notes]
        self.bm25 = BM25(self._tokens)
        self._entities = [entities(n.text) | set(n.tags) for n in self.notes]
        self.embedder = embedder
        self._vectors: Optional[List[Optional[List[float]]]] = None

    @property
    def dense_active(self) -> bool:
        """True only if a dense vector index was actually built successfully."""
        return bool(self._vectors)

    def build_dense_index(self) -> bool:
        """Precompute vectors. Returns False if the embedder is missing or the
        provider became unavailable partway through (never raises)."""
        if self.embedder is None:
            return False
        vectors = []
        for note in self.notes:
            vectors.append(self.embedder.embed(note.text[:4000]))
            if not self.embedder.available:
                self._vectors = None
                return False
        self._vectors = vectors
        return True

    def _ranked(self, scores: List[float]) -> List[int]:
        return [p[0] for p in sorted(enumerate(scores), key=lambda p: (-p[1], self.notes[p[0]].id)) if p[1] > 0]

    def bm25_only(self, query: str, top_k: int = 10) -> List[Note]:
        """Pure BM25 ranking, no RRF fusion, no entity/dense signal. Exposed
        for cognitive_core/benchmarks/retrieval_ab.py's isolated-arm reporting
        (arm 2 of 5) -- not used by search()."""
        ranked = self._ranked(self.bm25.scores(tokenize(query)))
        return [self.notes[i] for i in ranked[:top_k]]

    def entity_only(self, query: str, top_k: int = 10) -> List[Note]:
        """Pure entity-overlap ranking, no fusion. Exposed for the benchmark's
        isolated-arm reporting (arm 3 of 5) -- not used by search()."""
        q_entities = entities(query) | set(tokenize(query))
        scores = [
            len(q_entities & ents) / math.sqrt(len(ents) + 1)
            for ents in self._entities
        ]
        ranked = self._ranked(scores)
        return [self.notes[i] for i in ranked[:top_k]]

    def dense_only(self, query: str, top_k: int = 10, raise_on_unavailable: bool = False) -> List[Note]:
        """Pure dense embedding ranking. Fails closed with DenseProviderUnavailableError
        if Ollama is offline and raise_on_unavailable=True."""
        if self.embedder is None or not self.embedder.available:
            if raise_on_unavailable:
                raise DenseProviderUnavailableError("DENSE_PROVIDER_UNAVAILABLE")
            return []
        if self._vectors is None:
            ok = self.build_dense_index()
            if not ok:
                if raise_on_unavailable:
                    raise DenseProviderUnavailableError("DENSE_PROVIDER_UNAVAILABLE")
                return []
        q_vec = self.embedder.embed(query)
        if not q_vec:
            if raise_on_unavailable:
                raise DenseProviderUnavailableError("DENSE_PROVIDER_UNAVAILABLE")
            return []
        dense = [cosine(q_vec, v) if v else 0.0 for v in self._vectors]
        ranked = self._ranked(dense)
        return [self.notes[i] for i in ranked[:top_k]]

    def search(
        self,
        query: str,
        top_k: int = 10,
        lifecycles: Optional[Iterable[str]] = None,
        allowed_lifecycles: Optional[Iterable[str]] = None,
        types: Optional[Iterable[str]] = None,
        allowed_types: Optional[Iterable[str]] = None,
        verification: Optional[Iterable[str]] = None,
        allowed_verification: Optional[Iterable[str]] = None,
        secure: bool = False,
    ) -> List[Hit]:
        hits, _ = self.search_with_trace(
            query=query,
            top_k=top_k,
            lifecycles=lifecycles,
            allowed_lifecycles=allowed_lifecycles,
            types=types,
            allowed_types=allowed_types,
            verification=verification,
            allowed_verification=allowed_verification,
            secure=secure,
        )
        return hits

    def secure_search(
        self,
        query: str,
        top_k: int = 10,
        allowed_lifecycles: Optional[Iterable[str]] = None,
        allowed_types: Optional[Iterable[str]] = None,
        allowed_verification: Optional[Iterable[str]] = None,
    ) -> List[Hit]:
        """Convenience method enforcing security boundary (ACTIVE + verified by default)."""
        return self.search(
            query=query,
            top_k=top_k,
            allowed_lifecycles=allowed_lifecycles or {"ACTIVE"},
            allowed_types=allowed_types,
            allowed_verification=allowed_verification or {"verified"},
            secure=True,
        )

    def search_with_trace(
        self,
        query: str,
        top_k: int = 10,
        lifecycles: Optional[Iterable[str]] = None,
        allowed_lifecycles: Optional[Iterable[str]] = None,
        types: Optional[Iterable[str]] = None,
        allowed_types: Optional[Iterable[str]] = None,
        verification: Optional[Iterable[str]] = None,
        allowed_verification: Optional[Iterable[str]] = None,
        secure: bool = False,
    ) -> Tuple[List[Hit], Dict[str, Any]]:
        t0 = time.perf_counter()
        q_tokens = tokenize(query)
        q_entities = entities(query) | set(q_tokens)

        runs: Dict[str, List[int]] = {}
        runs["bm25"] = self._ranked(self.bm25.scores(q_tokens))
        ent_scores = [
            len(q_entities & ents) / math.sqrt(len(ents) + 1)
            for ents in self._entities
        ]
        runs["entity"] = self._ranked(ent_scores)

        if self._vectors:
            q_vec = self.embedder.embed(query) if self.embedder else None
            if q_vec:
                dense = [cosine(q_vec, v) if v else 0.0 for v in self._vectors]
                runs["dense"] = self._ranked(dense)

        fused: Dict[int, float] = defaultdict(float)
        signals: Dict[int, Dict[str, int]] = defaultdict(dict)
        for name, ranking in runs.items():
            w = self.weights.get(name, 1.0)
            for rank, idx in enumerate(ranking[:200], start=1):
                fused[idx] += w / (self.RRF_K + rank)
                signals[idx][name] = rank

        # Resolve security filters
        eff_lifecycles_in = allowed_lifecycles or lifecycles
        if secure and not eff_lifecycles_in:
            eff_lifecycles_in = {"ACTIVE"}
        eff_lifecycles = {l.upper() for l in eff_lifecycles_in} if eff_lifecycles_in else None

        eff_types_in = allowed_types or types
        eff_types = {t.lower() for t in eff_types_in} if eff_types_in else None

        eff_verif_in = allowed_verification or verification
        if secure and not eff_verif_in:
            eff_verif_in = {"verified"}
        eff_verif = {v.lower() for v in eff_verif_in} if eff_verif_in else None

        hits = []
        # Deterministic tie-break: secondary sort key is note ID lexicographically
        for idx, score in sorted(fused.items(), key=lambda p: (-p[1], self.notes[p[0]].id)):
            note = self.notes[idx]
            if eff_lifecycles and note.lifecycle not in eff_lifecycles:
                continue
            if eff_types and note.type not in eff_types:
                continue
            if eff_verif and note.verification not in eff_verif:
                continue
            hits.append(Hit(note=note, score=round(score, 6), signals=signals[idx]))
            if len(hits) >= top_k:
                break

        elapsed_ms = round((time.perf_counter() - t0) * 1000, 3)
        candidate_ids = {
            arm: [self.notes[i].id for i in ranking[:top_k]]
            for arm, ranking in runs.items()
        }
        trace = {
            "query": query,
            "top_k": top_k,
            "filters": {
                "lifecycles": sorted(eff_lifecycles) if eff_lifecycles else None,
                "types": sorted(eff_types) if eff_types else None,
                "verification": sorted(eff_verif) if eff_verif else None,
                "secure": secure,
            },
            "candidate_rankings": candidate_ids,
            "fused_ranking": [
                {"rank": r, "id": h.note.id, "score": h.score, "signals": h.signals}
                for r, h in enumerate(hits, 1)
            ],
            "timestamp_utc": datetime.now(timezone.utc).isoformat(),
            "latency_ms": elapsed_ms,
        }
        return hits, trace


def coverage(hits: Sequence[Hit], required_facts: Sequence[str]) -> float:
    """Diagnostic metric: fraction of required facts present in the retrieved text."""
    blob = " ".join(h.note.text.lower() for h in hits)
    if not required_facts:
        return 0.0
    return sum(1 for f in required_facts if f.lower() in blob) / len(required_facts)
