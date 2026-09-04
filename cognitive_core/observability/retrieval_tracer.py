import os
import re
import time
import json
import hashlib
from typing import List, Dict, Any, Optional, Tuple
from dataclasses import dataclass, field, asdict

from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from memory_controller.authority import get_authority_score
from memory_controller.security import sanitize_query, check_query_size
from memory_controller.context.query_classifier import QueryClassifier
from cognitive_core.semantic import SemanticProvider, DeterministicSemanticProvider
from cognitive_core.recall import RecallEngine

# Sensitive environment and key patterns for recursive redaction
REDACTION_PATTERNS = [
    re.compile(r"(sk-[a-zA-Z0-9_-]{20,})"),
    re.compile(r"(ghp_[a-zA-Z0-9_-]{20,})"),
    re.compile(r"(Bearer\s+[a-zA-Z0-9_.-]{20,})"),
]

def redact_sensitive(val: Any) -> Any:
    if isinstance(val, str):
        res = val
        for pat in REDACTION_PATTERNS:
            res = pat.sub("[REDACTED_SECRET]", res)
        return res
    elif isinstance(val, dict):
        return {k: redact_sensitive(v) for k, v in val.items()}
    elif isinstance(val, list):
        return [redact_sensitive(v) for v in val]
    return val

@dataclass
class CandidateTraceStage:
    note_id: str
    lifecycle: str
    provenance_source: str
    raw_similarity: float
    wm_similarity: float
    confidence_label: str
    confidence_score: float
    authority_score: float
    activation_score: float
    temporal_factor: float
    lifecycle_factor: float
    pre_lifecycle_score: float
    final_score: float
    rank_pre: int
    rank_post: int
    rank_delta: int
    lineage_successor: Optional[str] = None
    is_unverified_flagged: bool = False
    status: str = "INCLUDED"
    rejection_reason: Optional[str] = None

@dataclass
class StageTrace:
    stage_name: str
    stage_index: int
    data: Dict[str, Any]
    duration_ms: float

@dataclass
class RetrievalTrace:
    trace_id: str
    timestamp_utc: str
    query: str
    sanitized_query: str
    principal: str
    stages: List[StageTrace] = field(default_factory=list)
    candidates: List[CandidateTraceStage] = field(default_factory=list)
    abstained: bool = False
    abstention_threshold: float = 0.20
    abstention_reason: Optional[str] = None
    best_score: float = 0.0
    admitted_note_ids: List[str] = field(default_factory=list)
    context_sha256: str = ""
    context_char_length: int = 0
    context_estimated_tokens: int = 0
    total_latency_ms: float = 0.0

    def to_dict(self) -> Dict[str, Any]:
        d = asdict(self)
        return redact_sensitive(d)

    def to_json(self, indent: int = 2) -> str:
        return json.dumps(self.to_dict(), indent=indent, default=str)

    def save_trace(self, directory: str = "telemetry/retrieval_traces") -> str:
        os.makedirs(directory, exist_ok=True)
        file_path = os.path.join(directory, f"trace_{self.trace_id}.json")
        with open(file_path, "w", encoding="utf-8") as f:
            f.write(self.to_json())
        return file_path

    def to_markdown_table(self) -> str:
        lines = [
            f"# Retrieval Trace: `{self.trace_id}`",
            f"**Query**: `{self.query}`",
            f"**Principal**: `{self.principal}` | **Abstained**: `{self.abstained}` (Threshold: `{self.abstention_threshold:.4f}`)",
            f"**Total Duration**: `{self.total_latency_ms:.2f}ms` | **Context Digest**: `{self.context_sha256[:16]}...`",
            "",
            "### Candidate Scoring & Filtering Pipeline",
            "",
            "| Rank | ID | Lifecycle | Raw Sim | WM Sim | Conf/Auth | Activ. | Temp. | LC Mult. | Pre-Score | Final Score | Status | Reason |",
            "|---|---|---|---|---|---|---|---|---|---|---|---|---|"
        ]
        for c in self.candidates:
            lines.append(
                f"| {c.rank_post} | `{c.note_id}` | `{c.lifecycle}` | {c.raw_similarity:.4f} | "
                f"{c.wm_similarity:.4f} | {(c.confidence_score + c.authority_score)/2.0:.4f} | "
                f"{c.activation_score:.4f} | {c.temporal_factor:.4f} | {c.lifecycle_factor:.2f} | "
                f"{c.pre_lifecycle_score:.4f} | **{c.final_score:.4f}** | `{c.status}` | {c.rejection_reason or 'None'} |"
            )
        return "\n".join(lines)


class RetrievalTracer:
    """
    Antigravity Developer Retrieval Tracer (R001).
    Captures complete visibility across all 14 canonical retrieval steps:
    QUERY -> SANITIZE -> CLASSIFY -> CANDIDATES -> SEMANTIC/LEXICAL ->
    RELEVANCE -> CONFIDENCE -> AUTHORITY -> ACTIVATION -> TEMPORAL ->
    LIFECYCLE -> FINAL RANK -> ABSTENTION -> FINAL CONTEXT.
    """
    def __init__(self, semantic_provider: Optional[SemanticProvider] = None):
        self.semantic_provider = semantic_provider or DeterministicSemanticProvider()

    def trace(self,
              query: str,
              controller: MemoryController,
              principal: Principal = Principal.AI_AGENT,
              wm_context: str = "",
              access_history: Optional[Dict[str, float]] = None,
              abstention_threshold: float = 0.20,
              page_size: int = 5) -> RetrievalTrace:
        start_total = time.perf_counter()
        trace_id = hashlib.sha256(f"{query}:{time.time_ns()}".encode()).hexdigest()[:12]
        iso_now = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())

        trace = RetrievalTrace(
            trace_id=trace_id,
            timestamp_utc=iso_now,
            query=query,
            sanitized_query="",
            principal=principal.value,
            abstention_threshold=abstention_threshold
        )

        # STAGE 1: QUERY
        t0 = time.perf_counter()
        tokens = query.split()
        stage1 = StageTrace("QUERY", 1, {
            "raw_query": query,
            "char_length": len(query),
            "token_count": len(tokens)
        }, (time.perf_counter() - t0) * 1000)
        trace.stages.append(stage1)

        # STAGE 2: SANITIZE
        t0 = time.perf_counter()
        size_valid = True
        try:
            check_query_size(query)
        except Exception:
            size_valid = False
        sanitized = sanitize_query(query)
        trace.sanitized_query = sanitized
        stage2 = StageTrace("SANITIZE", 2, {
            "sanitized_query": sanitized,
            "size_valid": size_valid,
            "modified": sanitized != query
        }, (time.perf_counter() - t0) * 1000)
        trace.stages.append(stage2)

        # STAGE 3: CLASSIFY
        t0 = time.perf_counter()
        classifier = QueryClassifier()
        q_analysis = classifier.classify(sanitized)
        cat = q_analysis.get("category", "unknown") if isinstance(q_analysis, dict) else getattr(q_analysis, "category", "unknown")
        tags = q_analysis.get("tags", []) if isinstance(q_analysis, dict) else getattr(q_analysis, "tags", [])
        stage3 = StageTrace("CLASSIFY", 3, {
            "category": cat,
            "tags": tags,
            "budget_tier": "full" if len(tokens) > 10 else "snippet"
        }, (time.perf_counter() - t0) * 1000)
        trace.stages.append(stage3)

        # STAGE 4: CANDIDATES
        t0 = time.perf_counter()
        all_notes = []
        if hasattr(controller.storage, "query"):
            try:
                all_notes = controller.storage.query(intent="search")
            except Exception:
                pass
        if not all_notes and hasattr(controller.storage, "id_to_path"):
            for nid in list(controller.storage.id_to_path.keys()):
                n = controller.storage.get(nid)
                if n:
                    all_notes.append(n)
        if not all_notes and hasattr(controller.storage, "search"):
            try:
                all_notes = controller.storage.search(principal=principal)
            except Exception:
                pass

        candidate_notes = []
        excluded_notes = []
        for n in all_notes:
            lc = str(n.get("lifecycle", "ACTIVE")).upper()
            if lc in ("RAW", "CLASSIFIED", "NORMALIZED"):
                excluded_notes.append((n.get("id"), "LIFECYCLE_RAW_EXCLUDED"))
            else:
                candidate_notes.append(n)

        stage4 = StageTrace("CANDIDATES", 4, {
            "total_notes_in_store": len(all_notes),
            "admitted_candidates_count": len(candidate_notes),
            "excluded_count": len(excluded_notes),
            "exclusions_sample": excluded_notes[:5]
        }, (time.perf_counter() - t0) * 1000)
        trace.stages.append(stage4)

        # Build RecallEngine reference for scoring formulas
        recall_engine = RecallEngine(controller, self.semantic_provider, abstention_threshold=abstention_threshold)

        # STAGES 5-11: PER-CANDIDATE MULTI-SIGNAL EVALUATION
        t0 = time.perf_counter()
        scored_candidates: List[CandidateTraceStage] = []

        for node in candidate_notes:
            nid = node.get("id", "unknown")
            content = node.get("content", "")
            lc = str(node.get("lifecycle", "ACTIVE")).upper()
            source_type = node.get("provenance", {}).get("source_type", "unknown")

            # Stage 5: Semantic / Lexical Score
            raw_sim = self.semantic_provider.compute_similarity(sanitized, content)

            # Stage 6: Working Memory Relevance
            wm_sim = 0.0
            if wm_context:
                wm_sim = self.semantic_provider.compute_similarity(wm_context, content)

            # Stage 7: Confidence
            conf_label = node.get("confidence", "unknown")
            conf_num = recall_engine.confidence_map.get(conf_label, 0.0)

            # Stage 8: Authority
            auth_score = get_authority_score(node)

            # Stage 9: Activation (ACT-R)
            act_score = 0.0
            if access_history and nid in access_history:
                act_score = access_history[nid]

            # Stage 10: Temporal
            from datetime import datetime, timezone
            temp_factor = 1.0
            is_historical_query = any(w in sanitized.lower() for w in ["legacy", "deprecated", "historical", "old", "superseded"])
            valid_from = node.get("valid_from")
            if valid_from:
                try:
                    start_date = datetime.strptime(valid_from, "%Y-%m-%d")
                    now = datetime.now(timezone.utc).replace(tzinfo=None)
                    if start_date > now:
                        temp_factor = min(temp_factor, 0.5)
                except Exception:
                    pass
            valid_until = node.get("valid_until")
            if valid_until:
                try:
                    expiry = datetime.strptime(valid_until, "%Y-%m-%d")
                    now = datetime.now(timezone.utc).replace(tzinfo=None)
                    if expiry < now:
                        factor = 0.8 if is_historical_query else 0.5
                        temp_factor = min(temp_factor, factor)
                except Exception:
                    pass

            # Stage 11: Lifecycle Factor & Lineage
            is_unverified = (lc == "REVIEW")
            lc_factor = 1.0
            if lc == "SUPERSEDED":
                lc_factor = 0.8 if is_historical_query else 0.3
            elif lc == "ARCHIVED":
                lc_factor = 0.6 if is_historical_query else 0.1

            successor_id = None
            if lc == "SUPERSEDED":
                try:
                    from memory_controller.validation.supersession import resolve_active_lineage
                    active_id = resolve_active_lineage(controller.storage, nid)
                    if active_id and active_id != nid:
                        successor_id = active_id
                except Exception:
                    pass

            # Compute pre-lifecycle and final score
            pre_score = (
                (recall_engine.weights["semantic"] * raw_sim) +
                (recall_engine.weights["wm_relevance"] * wm_sim) +
                (recall_engine.weights["confidence"] * ((conf_num + auth_score) / 2.0)) +
                (recall_engine.weights["activation"] * act_score) +
                (recall_engine.weights["authority"] * temp_factor)
            )
            final_score = pre_score * lc_factor

            c_stage = CandidateTraceStage(
                note_id=nid,
                lifecycle=lc,
                provenance_source=source_type,
                raw_similarity=raw_sim,
                wm_similarity=wm_sim,
                confidence_label=conf_label,
                confidence_score=conf_num,
                authority_score=auth_score,
                activation_score=act_score,
                temporal_factor=temp_factor,
                lifecycle_factor=lc_factor,
                pre_lifecycle_score=pre_score,
                final_score=final_score,
                rank_pre=0,
                rank_post=0,
                rank_delta=0,
                lineage_successor=successor_id,
                is_unverified_flagged=is_unverified,
                status="CANDIDATE"
            )
            scored_candidates.append(c_stage)

        stage5_11 = StageTrace("MULTI_SIGNAL_SCORING", 5, {
            "scored_count": len(scored_candidates)
        }, (time.perf_counter() - t0) * 1000)
        trace.stages.append(stage5_11)

        # STAGE 12: FINAL RANK & DELTA
        t0 = time.perf_counter()
        # Pre-ranking
        scored_candidates.sort(key=lambda c: c.pre_lifecycle_score, reverse=True)
        for i, c in enumerate(scored_candidates, 1):
            c.rank_pre = i

        # Post-ranking
        scored_candidates.sort(key=lambda c: c.final_score, reverse=True)
        for i, c in enumerate(scored_candidates, 1):
            c.rank_post = i
            c.rank_delta = c.rank_pre - c.rank_post  # positive = moved up, negative = moved down

        stage12 = StageTrace("FINAL_RANK", 12, {
            "top_candidate_id": scored_candidates[0].note_id if scored_candidates else None,
            "top_candidate_score": scored_candidates[0].final_score if scored_candidates else 0.0
        }, (time.perf_counter() - t0) * 1000)
        trace.stages.append(stage12)

        # STAGE 13: ABSTENTION
        t0 = time.perf_counter()
        best_pre = scored_candidates[0].pre_lifecycle_score if scored_candidates else 0.0
        trace.best_score = best_pre
        if not scored_candidates:
            trace.abstained = True
            trace.abstention_reason = "CANDIDATE_POOL_EMPTY"
        elif best_pre < abstention_threshold:
            trace.abstained = True
            trace.abstention_reason = f"BEST_PRE_SCORE_BELOW_THRESHOLD ({best_pre:.4f} < {abstention_threshold:.4f})"
        else:
            trace.abstained = False
            trace.abstention_reason = None

        stage13 = StageTrace("ABSTENTION", 13, {
            "abstained": trace.abstained,
            "best_pre_score": best_pre,
            "threshold": abstention_threshold,
            "reason": trace.abstention_reason
        }, (time.perf_counter() - t0) * 1000)
        trace.stages.append(stage13)

        # STAGE 14: FINAL CONTEXT ASSEMBLY
        t0 = time.perf_counter()
        admitted = []
        if not trace.abstained:
            for c in scored_candidates[:page_size]:
                c.status = "INCLUDED"
                admitted.append(c.note_id)
            for c in scored_candidates[page_size:]:
                c.status = "REJECTED"
                c.rejection_reason = "PAGE_SIZE_CUTOFF"
        else:
            for c in scored_candidates:
                c.status = "ABSTAINED"
                c.rejection_reason = trace.abstention_reason

        trace.candidates = scored_candidates
        trace.admitted_note_ids = admitted

        # Compute context string and sha256
        context_body = "\n---\n".join([c.note_id for c in scored_candidates if c.status == "INCLUDED"])
        trace.context_char_length = len(context_body)
        trace.context_estimated_tokens = len(context_body.split())
        trace.context_sha256 = hashlib.sha256(context_body.encode("utf-8")).hexdigest()

        stage14 = StageTrace("FINAL_CONTEXT", 14, {
            "admitted_count": len(admitted),
            "context_sha256": trace.context_sha256,
            "context_tokens": trace.context_estimated_tokens
        }, (time.perf_counter() - t0) * 1000)
        trace.stages.append(stage14)

        trace.total_latency_ms = (time.perf_counter() - start_total) * 1000
        return trace
