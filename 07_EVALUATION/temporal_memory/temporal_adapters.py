"""evaluation/temporal_memory/temporal_adapters.py — P2 Temporal Memory Adapters.

Implements isolated temporal retrieval and lineage traversal adapters:
  - T0: Control Baseline (R4 candidate generation + P2 packing, no temporal logic)
  - T1: Valid-Time Filtering (valid_from / valid_until validity window resolution)
  - T2: Supersession Traversal (supersedes / superseded_by recursive lineage)
  - T3: Valid-Time + Supersession Lineage Fusion
  - T4: Bi-Temporal Traversal (Valid Time vs Transaction/Observation Time)
"""
from __future__ import annotations

import re
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional, Set, Tuple

from evaluation.context_packing.packer_adapters import PackerAdapters


def audit_temporal_metadata(notes: List[Dict[str, Any]]) -> Dict[str, Any]:
    """Audits the actual existence of temporal fields across all memory notes."""
    total = len(notes)
    counts = {
        "created": 0,
        "updated": 0,
        "valid_from": 0,
        "valid_until": 0,
        "supersedes": 0,
        "superseded_by": 0,
        "lifecycle": 0,
        "version": 0,
        "observation_time": 0,
    }

    for n in notes:
        if n.get("created"):
            counts["created"] += 1
        if n.get("updated"):
            counts["updated"] += 1
        if n.get("valid_from"):
            counts["valid_from"] += 1
        if n.get("valid_until"):
            counts["valid_until"] += 1
        if n.get("supersedes"):
            counts["supersedes"] += 1
        if n.get("superseded_by"):
            counts["superseded_by"] += 1
        if n.get("lifecycle"):
            counts["lifecycle"] += 1
        if n.get("version"):
            counts["version"] += 1
        if n.get("observation_time") or n.get("provenance", {}).get("timestamp"):
            counts["observation_time"] += 1

    status = {}
    for k, v in counts.items():
        status[k] = {
            "count": v,
            "pct": round(v / total * 100.0, 1) if total > 0 else 0.0,
            "status": "AVAILABLE" if v > 0 else "MISSING",
        }
    return {"total_notes": total, "fields": status}


class TemporalAdapters:
    """Temporal retrieval and graph traversal adapters."""

    @staticmethod
    def _parse_date(d_val: Any) -> Optional[datetime]:
        if not d_val:
            return None
        if isinstance(d_val, datetime):
            return d_val.replace(tzinfo=None)
        try:
            # Handle YYYY-MM-DD or ISO strings
            clean = str(d_val).split("T")[0]
            return datetime.strptime(clean, "%Y-%m-%d")
        except Exception:
            return None

    @classmethod
    def apply_t1_valid_time_filter(
        cls,
        candidates: List[Dict[str, Any]],
        query: str,
        as_of_date: Optional[datetime] = None,
    ) -> List[Dict[str, Any]]:
        """T1: Scores and sorts candidate notes based on validity intervals."""
        ref_date = as_of_date or datetime.now(timezone.utc).replace(tzinfo=None)
        lowered_q = query.lower()
        is_historical = any(w in lowered_q for w in ["historical", "legacy", "previous", "old", "superseded", "prior"])

        filtered = []
        for n in candidates:
            item = dict(n)
            vf = cls._parse_date(item.get("valid_from"))
            vu = cls._parse_date(item.get("valid_until"))

            validity_score = 1.0
            # Expired note
            if vu and vu < ref_date:
                validity_score = 0.8 if is_historical else 0.3
            # Future note
            elif vf and vf > ref_date:
                validity_score = 0.2

            item["_temporal_validity_score"] = validity_score
            filtered.append(item)

        # Sort by validity score descending
        return sorted(filtered, key=lambda x: x.get("_temporal_validity_score", 1.0), reverse=True)

    @classmethod
    def apply_t2_supersession_traversal(
        cls,
        candidates: List[Dict[str, Any]],
        all_notes_by_id: Dict[str, Dict[str, Any]],
        max_hops: int = 5,
    ) -> List[Dict[str, Any]]:
        """T2: Traverses supersession chains to pull active successors or historical predecessors."""
        expanded = list(candidates)
        seen_ids = {str(c.get("id")) for c in candidates}

        for c in list(candidates):
            curr = c
            hops = 0
            # Traverse forward (superseded_by -> active successor)
            while curr.get("superseded_by") and hops < max_hops:
                succ_id = str(curr.get("superseded_by"))
                if succ_id in seen_ids or succ_id not in all_notes_by_id:
                    break
                succ_note = dict(all_notes_by_id[succ_id])
                succ_note["_supersession_relation"] = "ACTIVE_SUCCESSOR"
                expanded.append(succ_note)
                seen_ids.add(succ_id)
                curr = succ_note
                hops += 1

            # Traverse backward (supersedes -> historical predecessor)
            curr = c
            hops = 0
            while curr.get("supersedes") and hops < max_hops:
                pred_id = str(curr.get("supersedes"))
                if pred_id in seen_ids or pred_id not in all_notes_by_id:
                    break
                pred_note = dict(all_notes_by_id[pred_id])
                pred_note["_supersession_relation"] = "HISTORICAL_PREDECESSOR"
                expanded.append(pred_note)
                seen_ids.add(pred_id)
                curr = pred_note
                hops += 1

        return expanded

    @classmethod
    def apply_t3_valid_time_and_supersession(
        cls,
        candidates: List[Dict[str, Any]],
        all_notes_by_id: Dict[str, Dict[str, Any]],
        query: str,
    ) -> List[Dict[str, Any]]:
        """T3: Combines supersession traversal with valid-time filtering."""
        t2_expanded = cls.apply_t2_supersession_traversal(candidates, all_notes_by_id)
        return cls.apply_t1_valid_time_filter(t2_expanded, query)

    @classmethod
    def apply_t4_bitemporal_traversal(
        cls,
        candidates: List[Dict[str, Any]],
        all_notes_by_id: Dict[str, Dict[str, Any]],
        query: str,
    ) -> List[Dict[str, Any]]:
        """T4: Bi-Temporal traversal adding explicit valid-time vs observation-time framing."""
        t3_notes = cls.apply_t3_valid_time_and_supersession(candidates, all_notes_by_id, query)

        bitemporal_notes = []
        for n in t3_notes:
            item = dict(n)
            vf = item.get("valid_from", "UNKNOWN")
            vu = item.get("valid_until", "CURRENT")
            obs_time = item.get("created") or item.get("updated") or "UNKNOWN"
            rel = item.get("_supersession_relation", "DIRECT")

            # Attach explicit bi-temporal envelope
            header_prefix = (
                f"[Temporal Meta: Valid={vf} to {vu} | Observed={obs_time} | Status={item.get('lifecycle', 'ACTIVE')} | Lineage={rel}]\n"
            )
            item["content"] = header_prefix + str(item.get("content", ""))
            bitemporal_notes.append(item)

        return bitemporal_notes
