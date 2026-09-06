"""03_IMPLEMENTATION/packages/graph/plasticity.py — Attribution-Aware Synaptic Plasticity.

Closes the loop: retrieval outcome -> causal attribution -> bounded weight update.

Core Invariants:
1. Five States Model (Never collapse them):
   - PRESENT: Memory exists in vault/storage.
   - RETRIEVED_CANDIDATE: Surfaced by retrieval generator during search.
   - CONTEXT_PACKED: Included in final context pack.
   - ACTUALLY_USED: Explicitly cited, invoked, or utilized in execution.
   - PLAUSIBLY_CAUSED: Traversed edge whose target was ACTUALLY_USED in a verified outcome.
   Only states 4 & 5 participate in reinforcement. Nodes merely in context do not strengthen incoming edges.
2. Bounded Updates & Asymptotic Compounding:
   Weights in [0.0, 1.5]. Single update delta capped at MAX_SINGLE_DELTA = 0.15.
   Success approaches MAX_WEIGHT asymptotically: delta = min(0.15, rate * (1.5 - W)).
   Failure approaches MIN_WEIGHT asymptotically: delta = min(0.15, rate * W).
3. Failure is Signal:
   Verified failure depresses plausibly causal edges (negative feedback).
4. No Auto-Promotion (P0 Security Invariant):
   Changes edge weights only. NEVER modifies note frontmatter, lifecycle, or verification.
5. Reversible & Append-Only:
   Every weight update is recorded in an append-only telemetry journal with complete rollback.
6. Fail-Closed:
   Missing trace or unverified outcome produces ZERO weight updates and logs an explicit marker.
"""
from __future__ import annotations

import json
import os
import re
import threading
import uuid
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from enum import Enum
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Set, Tuple

# Constants
MIN_WEIGHT = 0.0
MAX_WEIGHT = 1.5
MAX_SINGLE_DELTA = 0.15
DEFAULT_LEARNING_RATE = 0.15

# Verification methods accepted for plasticity updates
VERIFIED_METHODS = frozenset({
    "test_pass",
    "exit_code",
    "human_confirmed",
    "pytest",
    "ci",
    "outcome_ledger",
    "human_verified",
    "manual_verified",
})
UNVERIFIED_METHODS = frozenset({"none", "unverified", "", None})


class MemoryAttributionState(str, Enum):
    """The 5 discrete states of memory in the retrieval-to-execution pipeline."""
    PRESENT = "present"                    # 1. Stored in vault / index
    RETRIEVED_CANDIDATE = "retrieved_candidate"  # 2. Candidate considered during search
    CONTEXT_PACKED = "context_packed"      # 3. Packed into final context
    ACTUALLY_USED = "actually_used"        # 4. Explicitly cited or invoked in execution
    PLAUSIBLY_CAUSED = "plausibly_caused"  # 5. Traversed edge whose target was ACTUALLY_USED


@dataclass
class AttributionResult:
    """Detailed attribution analysis output."""
    run_id: str
    node_states: Dict[str, MemoryAttributionState] = field(default_factory=dict)
    attributed_edges: List[Tuple[str, str, str]] = field(default_factory=list)  # (source, target, relation)
    used_node_ids: Set[str] = field(default_factory=set)
    context_packed_ids: Set[str] = field(default_factory=set)
    candidate_ids: Set[str] = field(default_factory=set)
    traversed_edges: List[Dict[str, Any]] = field(default_factory=list)

    def to_dict(self) -> Dict[str, Any]:
        return {
            "run_id": self.run_id,
            "node_states": {k: v.value for k, v in self.node_states.items()},
            "attributed_edges": [list(e) for e in self.attributed_edges],
            "used_node_ids": sorted(list(self.used_node_ids)),
            "context_packed_ids": sorted(list(self.context_packed_ids)),
            "candidate_ids": sorted(list(self.candidate_ids)),
            "traversed_edges": self.traversed_edges,
        }


class AttributionModel:
    """Causal attribution engine distinguishing the 5 memory states."""

    # Regex patterns for citation detection
    _CITATION_PATTERNS = [
        re.compile(r"\[\[([a-zA-Z0-9_\-\.\s]+)\]\]"),  # [[wikilink]]
        re.compile(r"\[([a-zA-Z0-9_\-\.]+)\]"),        # [note_id]
        re.compile(r"\b(note_[a-zA-Z0-9_]+)\b"),       # note_...
    ]

    @classmethod
    def extract_citations(cls, text: str, valid_ids: Iterable[str]) -> Set[str]:
        """Extracts referenced memory IDs from execution output text."""
        if not text or not isinstance(text, str):
            return set()

        valid_set = set(valid_ids)
        lower_map = {v.lower(): v for v in valid_set}
        found: Set[str] = set()

        for pat in cls._CITATION_PATTERNS:
            for match in pat.finditer(text):
                token = match.group(1).strip()
                if token in valid_set:
                    found.add(token)
                elif token.lower() in lower_map:
                    found.add(lower_map[token.lower()])

        return found

    @classmethod
    def attribute(
        cls,
        candidate_trace: Optional[Dict[str, Any]] = None,
        used_memory_ids: Optional[Iterable[str]] = None,
        execution_output: Optional[str] = None,
        observed_capabilities: Optional[Dict[str, Any]] = None,
        vault_present_ids: Optional[Iterable[str]] = None,
        run_id: str = "unknown_run",
    ) -> AttributionResult:
        """Computes the 5-state attribution mapping for a run.
        
        Guarantees that an edge whose target is merely in context (CONTEXT_PACKED)
        without being ACTUALLY_USED is never attributed as PLAUSIBLY_CAUSED.
        """
        node_states: Dict[str, MemoryAttributionState] = {}
        candidate_ids: Set[str] = set()
        context_packed_ids: Set[str] = set()
        used_node_ids: Set[str] = set()
        traversed_edges: List[Dict[str, Any]] = []

        # 1. State 1: PRESENT (if provided)
        if vault_present_ids:
            for nid in vault_present_ids:
                node_states[nid] = MemoryAttributionState.PRESENT

        # 2. State 2: RETRIEVED_CANDIDATE
        if candidate_trace and isinstance(candidate_trace, dict):
            raw_candidates = candidate_trace.get("candidates_considered", [])
            for c in raw_candidates:
                cid = c if isinstance(c, str) else c.get("id") if isinstance(c, dict) else None
                if cid:
                    candidate_ids.add(str(cid))
                    node_states[str(cid)] = MemoryAttributionState.RETRIEVED_CANDIDATE

            # Also seed IDs
            for sid in candidate_trace.get("graph_seed_ids", []):
                if sid:
                    candidate_ids.add(str(sid))
                    node_states[str(sid)] = MemoryAttributionState.RETRIEVED_CANDIDATE

            # 3. State 3: CONTEXT_PACKED
            raw_packed = (
                candidate_trace.get("final_context_ids")
                or candidate_trace.get("retrieved_memory_ids")
                or []
            )
            for pid in raw_packed:
                if pid:
                    pid_str = str(pid)
                    context_packed_ids.add(pid_str)
                    node_states[pid_str] = MemoryAttributionState.CONTEXT_PACKED

            # Extract traversed edges
            raw_edges = candidate_trace.get("graph_edges_traversed") or candidate_trace.get("observed_edges") or []
            for edge in raw_edges:
                if isinstance(edge, dict) and "source" in edge and "target" in edge:
                    traversed_edges.append({
                        "source": str(edge["source"]),
                        "target": str(edge["target"]),
                        "relation": str(edge.get("relation", "related_to")),
                    })
                elif isinstance(edge, (list, tuple)) and len(edge) >= 2:
                    rel = str(edge[2]) if len(edge) >= 3 else "related_to"
                    traversed_edges.append({
                        "source": str(edge[0]),
                        "target": str(edge[1]),
                        "relation": rel,
                    })

        # 4. State 4: ACTUALLY_USED
        # Explicitly declared
        if used_memory_ids:
            for uid in used_memory_ids:
                if uid:
                    used_node_ids.add(str(uid))

        # Observed capabilities (e.g., knowledge_refs, procedure_refs)
        if observed_capabilities and isinstance(observed_capabilities, dict):
            for k in ("knowledge_refs", "procedure_refs", "memories_used"):
                for ref in observed_capabilities.get(k, []):
                    if ref:
                        used_node_ids.add(str(ref))

        # Text output citation extraction
        if execution_output and (context_packed_ids or candidate_ids):
            extracted = cls.extract_citations(execution_output, context_packed_ids | candidate_ids)
            used_node_ids.update(extracted)

        # Mark ACTUALLY_USED on node_states
        for uid in used_node_ids:
            node_states[uid] = MemoryAttributionState.ACTUALLY_USED

        # 5. State 5: PLAUSIBLY_CAUSED (Edges)
        # An edge u -> v plausibly caused the outcome ONLY IF target v was ACTUALLY_USED!
        attributed_edges: List[Tuple[str, str, str]] = []
        for e in traversed_edges:
            src = e["source"]
            tgt = e["target"]
            rel = e.get("relation", "related_to")

            # Check if target was ACTUALLY_USED
            if tgt in used_node_ids:
                attributed_edges.append((src, tgt, rel))
            elif src in used_node_ids and tgt in context_packed_ids:
                # If source was used and target was only in context, do NOT attribute:
                # Strictly reject: an edge whose target is merely in context must NOT strengthen.
                pass

        return AttributionResult(
            run_id=run_id,
            node_states=node_states,
            attributed_edges=attributed_edges,
            used_node_ids=used_node_ids,
            context_packed_ids=context_packed_ids,
            candidate_ids=candidate_ids,
            traversed_edges=traversed_edges,
        )


@dataclass
class JournalEntry:
    """Immutable audit entry for a synaptic weight modification."""
    entry_id: str
    run_id: str
    timestamp: str
    action: str  # "reinforce" | "depress" | "rollback"
    source_id: str
    target_id: str
    relation: str
    old_weight: float
    new_weight: float
    delta: float
    outcome: str
    verification_method: str
    attribution_state: str = MemoryAttributionState.PLAUSIBLY_CAUSED.value
    metadata: Dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> JournalEntry:
        return cls(**data)


@dataclass
class RollbackResult:
    """Outcome of a journal rollback operation."""
    run_id: str
    success: bool
    edges_reverted: int
    reverted_entries: List[Dict[str, Any]] = field(default_factory=list)
    error: Optional[str] = None


class PlasticityJournal:
    """Append-only, thread-safe telemetry journal for synaptic weight changes."""

    def __init__(self, journal_path: Optional[Path | str] = None):
        if journal_path is not None:
            self.path = Path(journal_path).resolve()
        else:
            telemetry_base = Path(os.getenv("ANTIGRAVITY_TELEMETRY_DIR", "telemetry"))
            self.path = (telemetry_base / "plasticity_journal.jsonl").resolve()

        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._lock = threading.Lock()

    def append(self, entry: JournalEntry) -> str:
        """Appends an entry to the journal. Thread-safe and atomic."""
        with self._lock:
            with open(self.path, "a", encoding="utf-8") as f:
                f.write(json.dumps(entry.to_dict(), ensure_ascii=False) + "\n")
        return entry.entry_id

    def load_entries(self, run_id: Optional[str] = None) -> List[JournalEntry]:
        """Loads entries from the append-only journal, optionally filtered by run_id."""
        if not self.path.exists():
            return []

        entries: List[JournalEntry] = []
        with self._lock:
            try:
                with open(self.path, "r", encoding="utf-8") as f:
                    for line in f:
                        line = line.strip()
                        if not line:
                            continue
                        try:
                            data = json.loads(line)
                            entry = JournalEntry.from_dict(data)
                            if run_id is None or entry.run_id == run_id:
                                entries.append(entry)
                        except Exception:
                            continue
            except Exception:
                return []
        return entries

    def rollback(self, run_id: str, synapse_store: Any) -> RollbackResult:
        """Reverses all weight updates made for `run_id` on the given `synapse_store`.
        
        Maintains append-only integrity by writing compensating rollback audit records.
        """
        all_entries = self.load_entries()
        run_entries = [e for e in all_entries if e.run_id == run_id and e.action in {"reinforce", "depress"}]

        if not run_entries:
            return RollbackResult(run_id=run_id, success=True, edges_reverted=0)

        # Check if already rolled back
        rollback_entries = [e for e in all_entries if e.run_id == run_id and e.action == "rollback"]
        already_rolled_back_keys = {(e.source_id, e.target_id, e.relation) for e in rollback_entries}

        active_updates = [
            e for e in run_entries
            if (e.source_id, e.target_id, e.relation) not in already_rolled_back_keys
        ]

        if not active_updates:
            return RollbackResult(run_id=run_id, success=True, edges_reverted=0)

        reverted_count = 0
        reverted_details = []

        now_ts = datetime.now(timezone.utc).isoformat(timespec="seconds")

        for entry in active_updates:
            # Find synapse in store
            matched_synapses = []
            for syn in synapse_store.all():
                if syn.source_id == entry.source_id and syn.target_id == entry.target_id:
                    if syn.relation == entry.relation or not entry.relation:
                        matched_synapses.append(syn)

            for syn in matched_synapses:
                current_weight = syn.weight
                syn.weight = entry.old_weight
                syn.updated = now_ts
                reverted_count += 1

                # Record compensating rollback entry
                rollback_record = JournalEntry(
                    entry_id=f"plj_rb_{uuid.uuid4().hex[:10]}",
                    run_id=run_id,
                    timestamp=now_ts,
                    action="rollback",
                    source_id=syn.source_id,
                    target_id=syn.target_id,
                    relation=syn.relation,
                    old_weight=current_weight,
                    new_weight=entry.old_weight,
                    delta=round(entry.old_weight - current_weight, 6),
                    outcome="rollback",
                    verification_method=entry.verification_method,
                    attribution_state=entry.attribution_state,
                    metadata={"target_entry_id": entry.entry_id},
                )
                self.append(rollback_record)
                reverted_details.append({
                    "source": syn.source_id,
                    "target": syn.target_id,
                    "relation": syn.relation,
                    "from_weight": current_weight,
                    "to_weight": entry.old_weight,
                })

        return RollbackResult(
            run_id=run_id,
            success=True,
            edges_reverted=reverted_count,
            reverted_entries=reverted_details,
        )


@dataclass
class PlasticityResult:
    """Outcome of a bounded plasticity update cycle."""
    status: str  # "applied" | "no_attributed_edges" | "unverified_outcome" | "unsupported_outcome" | "trace_missing" | "malformed_trace"
    run_id: str
    applied_count: int = 0
    updated_edges: List[Dict[str, Any]] = field(default_factory=list)
    reason: Optional[str] = None
    attribution: Optional[AttributionResult] = None
    journal_entry_ids: List[str] = field(default_factory=list)


class PlasticityEngine:
    """Bounded, attribution-aware synaptic weight update engine."""

    def __init__(
        self,
        journal: Optional[PlasticityJournal] = None,
        default_rate: float = DEFAULT_LEARNING_RATE,
        max_single_delta: float = MAX_SINGLE_DELTA,
        min_weight: float = MIN_WEIGHT,
        max_weight: float = MAX_WEIGHT,
    ):
        self.journal = journal or PlasticityJournal()
        self.default_rate = default_rate
        self.max_single_delta = max_single_delta
        self.min_weight = min_weight
        self.max_weight = max_weight

    def apply_outcome(
        self,
        synapse_store: Any,
        candidate_trace: Optional[Dict[str, Any]] = None,
        outcome_record: Optional[Any] = None,
        used_memory_ids: Optional[Iterable[str]] = None,
        execution_output: Optional[str] = None,
        observed_capabilities: Optional[Dict[str, Any]] = None,
        vault_present_ids: Optional[Iterable[str]] = None,
        run_id: Optional[str] = None,
        rate: Optional[float] = None,
    ) -> PlasticityResult:
        """Evaluates causal attribution and applies bounded weight updates.
        
        Strictly fail-closed:
        - Missing trace -> returns 'trace_missing'
        - Unverified outcome -> returns 'unverified_outcome'
        - Unsupported outcome (e.g. unknown, partial) -> returns 'unsupported_outcome'
        - Zero attributed edges -> returns 'no_attributed_edges'
        - Zero lifecycle mutation: never modifies note content or status.
        """
        # Resolve run_id
        resolved_run_id = (
            run_id
            or (getattr(outcome_record, "run_id", None))
            or (isinstance(outcome_record, dict) and outcome_record.get("run_id"))
            or (candidate_trace.get("run_id") if isinstance(candidate_trace, dict) else None)
            or "unspecified_run"
        )

        # 1. Fail-closed: Validate Outcome Record
        if outcome_record is None:
            return PlasticityResult(
                status="unverified_outcome",
                run_id=resolved_run_id,
                reason="Outcome record missing: fail-closed",
            )

        # Extract outcome & verification method
        if isinstance(outcome_record, dict):
            outcome_val = str(outcome_record.get("outcome", "")).lower()
            verif_val = str(
                outcome_record.get("verification_method")
                or outcome_record.get("verification_source")
                or outcome_record.get("source")
                or ""
            ).lower()
        else:
            outcome_val = str(getattr(outcome_record, "outcome", "")).lower()
            verif_val = str(getattr(outcome_record, "verification_method", "")).lower()
            if hasattr(outcome_record, "observed_capabilities") and not observed_capabilities:
                obs_caps = getattr(outcome_record, "observed_capabilities")
                if hasattr(obs_caps, "to_dict"):
                    observed_capabilities = obs_caps.to_dict()
                elif isinstance(obs_caps, dict):
                    observed_capabilities = obs_caps

        if outcome_val not in {"success", "fail"}:
            return PlasticityResult(
                status="unsupported_outcome",
                run_id=resolved_run_id,
                reason=f"Outcome '{outcome_val}' unsupported; must be 'success' or 'fail'",
            )

        if not verif_val or verif_val in UNVERIFIED_METHODS or verif_val not in VERIFIED_METHODS:
            return PlasticityResult(
                status="unverified_outcome",
                run_id=resolved_run_id,
                reason=f"Verification method '{verif_val}' is unverified; external proof required",
            )

        # 2. Fail-closed: Validate Candidate Trace
        if candidate_trace is None:
            return PlasticityResult(
                status="trace_missing",
                run_id=resolved_run_id,
                reason="Candidate trace missing: fail-closed",
            )

        if not isinstance(candidate_trace, dict):
            return PlasticityResult(
                status="malformed_trace",
                run_id=resolved_run_id,
                reason="Candidate trace must be a dictionary",
            )

        # 3. Perform 5-State Attribution
        attribution = AttributionModel.attribute(
            candidate_trace=candidate_trace,
            used_memory_ids=used_memory_ids,
            execution_output=execution_output,
            observed_capabilities=observed_capabilities,
            vault_present_ids=vault_present_ids,
            run_id=resolved_run_id,
        )

        if not attribution.attributed_edges:
            return PlasticityResult(
                status="no_attributed_edges",
                run_id=resolved_run_id,
                applied_count=0,
                attribution=attribution,
                reason="No edges reached state 5 (PLAUSIBLY_CAUSED)",
            )

        # 4. Apply Bounded Asymptotic Updates
        learning_rate = rate if rate is not None else self.default_rate
        is_success = (outcome_val == "success")
        action_name = "reinforce" if is_success else "depress"
        now_ts = datetime.now(timezone.utc).isoformat(timespec="seconds")

        # Map existing synapses in store
        synapses_by_pair: Dict[Tuple[str, str], List[Any]] = {}
        for syn in synapse_store.all():
            synapses_by_pair.setdefault((syn.source_id, syn.target_id), []).append(syn)

        updated_edges: List[Dict[str, Any]] = []
        journal_ids: List[str] = []

        for src, tgt, rel in attribution.attributed_edges:
            matched_syns = synapses_by_pair.get((src, tgt), [])
            if rel:
                filtered = [s for s in matched_syns if s.relation == rel]
                target_syns = filtered if filtered else matched_syns
            else:
                target_syns = matched_syns

            for syn in target_syns:
                w_old = syn.weight

                if is_success:
                    # Asymptotic compounding towards MAX_WEIGHT
                    raw_delta = learning_rate * (self.max_weight - w_old)
                    delta = min(self.max_single_delta, max(0.0, raw_delta))
                    w_new = min(self.max_weight, w_old + delta)
                    syn.reinforcements += 1
                    if resolved_run_id not in syn.evidence:
                        syn.evidence.append(resolved_run_id)
                else:
                    # Failure depression towards MIN_WEIGHT
                    raw_delta = learning_rate * w_old
                    delta = min(self.max_single_delta, max(0.0, raw_delta))
                    w_new = max(self.min_weight, w_old - delta)
                    syn.depressions += 1

                syn.weight = round(w_new, 6)
                syn.updated = now_ts

                # Journal entry
                entry = JournalEntry(
                    entry_id=f"plj_{uuid.uuid4().hex[:12]}",
                    run_id=resolved_run_id,
                    timestamp=now_ts,
                    action=action_name,
                    source_id=syn.source_id,
                    target_id=syn.target_id,
                    relation=syn.relation,
                    old_weight=round(w_old, 6),
                    new_weight=round(w_new, 6),
                    delta=round(delta if is_success else -delta, 6),
                    outcome=outcome_val,
                    verification_method=verif_val,
                    attribution_state=MemoryAttributionState.PLAUSIBLY_CAUSED.value,
                    metadata={
                        "learning_rate": learning_rate,
                        "used_target": tgt in attribution.used_node_ids,
                    },
                )
                jid = self.journal.append(entry)
                journal_ids.append(jid)

                updated_edges.append({
                    "source": syn.source_id,
                    "target": syn.target_id,
                    "relation": syn.relation,
                    "old_weight": round(w_old, 6),
                    "new_weight": round(w_new, 6),
                    "delta": round(delta if is_success else -delta, 6),
                })

        return PlasticityResult(
            status="applied",
            run_id=resolved_run_id,
            applied_count=len(updated_edges),
            updated_edges=updated_edges,
            attribution=attribution,
            journal_entry_ids=journal_ids,
        )
