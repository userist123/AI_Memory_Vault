"""edge_proposer.py — P1.2 synaptogenesis proposer (owner: claude-code).

Status: EXPERIMENTAL. Output is ALWAYS a proposal, never a canonical mutation.
Never writes to Markdown/frontmatter. Never wires into MemoryController,
cognitive_core/tool_router.py, or cognitive_core/activation.py.

Problem measured on the real vault (see 06_INBOX/OPUS RECOMANDATION audit):
806-869 nodes, ~9-147 declared edges depending on snapshot, density well under
1 edge/node. A graph that sparse does not propagate activation.

Two tiers:

  TIER 1 (deterministic, always available, air-gap safe)
    - rare shared entity (IDF-weighted): co-mention of rare technical
      identifiers => `related_to` (weak) by default, refined to `applies_to`
      / `supersedes` / `part_of` when the note types/dates/tags support it
    - same subject, newer date => `supersedes` (strong)
    - lesson mentioning a procedure => `applies_to` (strong)
    - shared project tag => `part_of` (weak)

  TIER 2 (optional, local Ollama only — no external API, air-gap preserved)
    - reclassifies TIER 1 candidate pairs against the fixed relation enum
    - can only ever output a member of ALLOWED_RELATIONS or the request is
      rejected outright (fail-closed, no fuzzy matching, no auto-correct)

Every accepted proposal carries: source_id, target_id, relation, weight,
confidence, origin, evidence, extraction_run_id, provider, model, timestamp,
status. Output is written to a review queue (default 06_INBOX/edge_proposals.json)
with status PROPOSED_PENDING_REVIEW. Promotion into a canonical SynapseStore or
into Markdown remains a separate, out-of-scope, human-gated step.

    python 30_SCRIPTS/knowledge/edge_proposer.py --limit 500
    python 30_SCRIPTS/knowledge/edge_proposer.py --ollama --model qwen2.5-coder:3b
"""
from __future__ import annotations

import argparse
import json
import math
import re
import sys
import uuid
from collections import Counter, defaultdict
from datetime import datetime, timezone
from itertools import combinations
from pathlib import Path
from typing import Dict, List, Optional, Tuple

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from cognitive_core.hybrid_retrieval import entities, tokenize  # noqa: E402
from cognitive_core.synapse_store import (  # noqa: E402
    ALLOWED_RELATIONS,
    STRONG_RELATIONS,
    WEAK_RELATIONS,
    MAX_WEIGHT,
    MIN_WEIGHT,
    WEAK_WEIGHT_FACTOR,
    RELATION_BASE_WEIGHT,
)
from cognitive_core.vault_index import VaultIndex  # noqa: E402

MIN_RARE_ENTITY_DF = 1
MAX_COMMON_ENTITY_DF_RATIO = 0.15   # entities present in >15% of notes don't discriminate
MIN_SCORE = 0.60
MIN_SHARED_ENTITIES = 3   # empirically calibrated on the real vault: ~3.8 edges/node

PROCEDURE_TYPES = {"procedure", "protocol", "rules"}
LESSON_TYPES = {"lesson", "error", "experience"}

# Control characters that are never legitimate in an evidence/identity field.
# Tab/newline/carriage-return are allowed in free text but stripped anyway for
# single-line JSON fields; everything else in this class is rejected/sanitized.
_CONTROL_CHAR_RE = re.compile(r"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]")
_STATUS_PENDING = "PROPOSED_PENDING_REVIEW"


def _ensure_utf8_stdout() -> None:
    """Windows console defaults to a legacy codepage (cp1252) that cannot
    encode Romanian diacritics or check-mark symbols. Reconfigure stdout/
    stderr to UTF-8 so this CLI never crashes mid-run before writing its
    output artifact (this crash was reproduced empirically: on stock Windows
    the original script raised UnicodeEncodeError BEFORE writing
    06_INBOX/edge_proposals.json, i.e. the primary deliverable was never
    produced at all)."""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass


def _has_control_chars(text: str) -> bool:
    return bool(_CONTROL_CHAR_RE.search(text or ""))


def _sanitize_untrusted(text: str, max_len: int = 300) -> str:
    """Bounds and strips control characters from untrusted free text (e.g. raw
    LLM output) before it is allowed anywhere near a persisted artifact."""
    cleaned = _CONTROL_CHAR_RE.sub("", text or "")
    return cleaned[:max_len]


def build_entity_df(index: VaultIndex) -> Tuple[Dict[str, set], Counter]:
    ent_by_note: Dict[str, set] = {}
    df: Counter = Counter()
    for note in index.notes:
        ents = entities(note.text) | set(note.tags)
        ent_by_note[note.id] = ents
        df.update(ents)
    return ent_by_note, df


def _same_subject(t1: str, t2: str) -> bool:
    norm_re = re.compile(r"[^a-z0-9 ]+")
    a = set(norm_re.sub(" ", t1.lower()).split())
    b = set(norm_re.sub(" ", t2.lower()).split())
    if not a or not b:
        return False
    return len(a & b) / len(a | b) >= 0.7


def _weight_for(relation: str, confidence: float) -> float:
    """Proposal-layer weight: bounded in [MIN_WEIGHT, MAX_WEIGHT] by
    construction (confidence in [0,1] * a factor <= 1). Weak relations always
    receive a reduced initial weight relative to their confidence, per the
    strong/weak proposal-layer policy."""
    base = max(MIN_WEIGHT, min(1.0, confidence))
    if relation in WEAK_RELATIONS:
        return round(base * WEAK_WEIGHT_FACTOR, 4)
    return round(base, 4)


def deterministic_candidates(index: VaultIndex, limit: int = 2000) -> Tuple[List[dict], int]:
    """Returns (proposals, candidate_pair_count). candidate_pair_count is the
    number of (a, b) pairs that shared ANY rare entity at all, BEFORE the
    MIN_SHARED_ENTITIES/MIN_SCORE thresholds are applied — this is the
    "candidate pairs" metric the caller must report separately from
    "accepted"."""
    ent_by_note, df = build_entity_df(index)
    n_notes = max(len(index), 1)
    max_df = max(int(n_notes * MAX_COMMON_ENTITY_DF_RATIO), 2)

    # Inverted index on rare entities only, to avoid O(n^2) over the whole vault.
    inverted: Dict[str, List[str]] = defaultdict(list)
    for note_id, ents in ent_by_note.items():
        for e in ents:
            if 2 <= df[e] <= max_df:
                inverted[e].append(note_id)

    pair_scores: Dict[Tuple[str, str], float] = defaultdict(float)
    pair_shared: Dict[Tuple[str, str], set] = defaultdict(set)
    for ent, note_ids in inverted.items():
        if len(note_ids) < 2 or len(note_ids) > 40:
            continue
        idf = math.log(n_notes / df[ent])
        for a, b in combinations(sorted(note_ids), 2):
            pair_scores[(a, b)] += idf
            pair_shared[(a, b)].add(ent)

    candidate_pair_count = len(pair_scores)
    if not pair_scores:
        return [], 0
    # ABSOLUTE normalization, not relative to the sample maximum: a single
    # very dense pair must not suppress the rest of the graph.
    norm = 2 * math.log(n_notes)

    proposals = []
    for (a, b), raw in sorted(pair_scores.items(), key=lambda p: -p[1])[:limit * 3]:
        na, nb = index.by_id.get(a), index.by_id.get(b)
        if na is None or nb is None or a == b:
            continue
        if len(pair_shared[(a, b)]) < MIN_SHARED_ENTITIES:
            continue
        score = min(1.0, raw / norm)
        if score < MIN_SCORE:
            continue

        relation, src, dst = "related_to", a, b
        if na.type in LESSON_TYPES and nb.type in PROCEDURE_TYPES:
            relation, src, dst = "applies_to", a, b
        elif nb.type in LESSON_TYPES and na.type in PROCEDURE_TYPES:
            relation, src, dst = "applies_to", b, a
        elif _same_subject(na.title, nb.title):
            ua, ub = na.updated, nb.updated
            if ua and ub and ua != ub:
                (src, dst) = (a, b) if ua > ub else (b, a)
                relation = "supersedes"
        elif set(na.tags) & set(nb.tags) and (na.type == "project") != (nb.type == "project"):
            relation = "part_of"
            src, dst = (a, b) if nb.type == "project" else (b, a)

        origin = "proposed_weak" if relation in WEAK_RELATIONS else "proposed"
        proposals.append({
            "source_id": src, "target_id": dst, "relation": relation,
            "confidence": round(score, 4),
            "weight": _weight_for(relation, score),
            "origin": origin,
            "evidence_entities": sorted(pair_shared[(a, b)])[:6],
            "source_path": index.by_id[src].path.as_posix(),
            "target_path": index.by_id[dst].path.as_posix(),
        })
        if len(proposals) >= limit:
            break
    return proposals, candidate_pair_count


OLLAMA_PROMPT = """You are a relation classifier for a memory graph.
Given two notes, choose EXACTLY ONE relation from the list below, or NONE.

Relations: depends_on, contradicts, supersedes, caused, verified_by, applies_to, related_to, part_of, NONE

NOTE A ({a_type}): {a_title}
{a_body}

NOTE B ({b_type}): {b_title}
{b_body}

Answer with ONLY JSON: {{"relation": "...", "direction": "A->B" or "B->A", "confidence": 0.0-1.0}}"""


def classify_with_ollama(pairs: List[dict], index: VaultIndex, model: str,
                         host: str = "http://localhost:11434",
                         max_pairs: int = 200) -> List[dict]:
    """Reclassifies TIER 1 candidates via a local model. Fail-closed: the
    model's `relation` value is checked against ALLOWED_RELATIONS with exact,
    case-sensitive matching — no fuzzy matching, no auto-correction. Any
    other value (garbage, an injected instruction, an unknown label) is
    rejected outright and the raw text is kept only as bounded, sanitized,
    explicitly-untrusted evidence, never as the relation itself."""
    import urllib.error
    import urllib.request

    out = []
    for p in pairs[:max_pairs]:
        a, b = index.by_id[p["source_id"]], index.by_id[p["target_id"]]
        prompt = OLLAMA_PROMPT.format(
            a_type=a.type, a_title=a.title, a_body=a.body[:800],
            b_type=b.type, b_title=b.title, b_body=b.body[:800],
        )
        payload = json.dumps({"model": model, "prompt": prompt, "stream": False,
                              "options": {"temperature": 0}}).encode()
        req = urllib.request.Request(f"{host}/api/generate", data=payload,
                                     headers={"Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=120) as resp:
                text = json.loads(resp.read()).get("response", "")
        except (urllib.error.URLError, OSError, ValueError, TimeoutError):
            continue  # network/provider failure -> skip, never fabricate a result

        raw_untrusted = _sanitize_untrusted(text, max_len=500)
        m = re.search(r"\{.*\}", text, re.S)
        if not m:
            continue
        try:
            verdict = json.loads(m.group(0))
        except json.JSONDecodeError:
            continue
        if not isinstance(verdict, dict):
            continue

        rel = verdict.get("relation")
        if not isinstance(rel, str) or rel not in ALLOWED_RELATIONS:
            # Includes "NONE" (not in ALLOWED_RELATIONS by construction) and
            # any adversarial/garbage string, e.g. a prompt-injection attempt
            # embedded in the "relation" field itself.
            continue
        conf_raw = verdict.get("confidence")
        if not isinstance(conf_raw, (int, float)) or isinstance(conf_raw, bool):
            continue
        confidence = float(conf_raw)
        if not (0.0 <= confidence <= 1.0) or confidence < 0.6:
            continue

        item = dict(p)
        item["relation"] = rel
        item["confidence"] = round(confidence, 4)
        item["weight"] = _weight_for(rel, confidence)
        item["origin"] = "proposed_weak" if rel in WEAK_RELATIONS else "proposed_llm"
        if verdict.get("direction") == "B->A":
            item["source_id"], item["target_id"] = p["target_id"], p["source_id"]
            item["source_path"], item["target_path"] = p["target_path"], p["source_path"]
        item["llm_raw_response"] = raw_untrusted  # bounded, sanitized, untrusted evidence only
        out.append(item)
    return out


def validate_proposals(proposals: List[dict], index: VaultIndex) -> Tuple[List[dict], Counter]:
    """Final fail-closed validation pass over ALL proposals (both tiers)
    before anything is written. Returns (accepted, reject_reason_counts).
    Checks, in order: unknown source, unknown target, self-loop, invalid
    relation, invalid confidence, invalid weight, missing evidence, duplicate
    edge, control-character abuse."""
    accepted: List[dict] = []
    rejected: Counter = Counter()
    seen: set = set()

    for p in proposals:
        src, dst = p.get("source_id"), p.get("target_id")
        if src not in index.by_id:
            rejected["unknown_source"] += 1
            continue
        if dst not in index.by_id:
            rejected["unknown_target"] += 1
            continue
        if src == dst:
            rejected["self_loop"] += 1
            continue
        relation = p.get("relation")
        if relation not in ALLOWED_RELATIONS:
            rejected["invalid_relation"] += 1
            continue
        confidence = p.get("confidence")
        if not isinstance(confidence, (int, float)) or isinstance(confidence, bool) \
                or not (0.0 <= float(confidence) <= 1.0):
            rejected["invalid_confidence"] += 1
            continue
        weight = p.get("weight")
        if not isinstance(weight, (int, float)) or isinstance(weight, bool) \
                or not (MIN_WEIGHT <= float(weight) <= MAX_WEIGHT):
            rejected["invalid_weight"] += 1
            continue
        evidence = p.get("evidence_entities") or []
        if not evidence and not p.get("llm_raw_response"):
            rejected["missing_evidence"] += 1
            continue
        key = (src, dst, relation)
        if key in seen:
            rejected["duplicate_edge"] += 1
            continue
        text_fields = [p.get("source_path", ""), p.get("target_path", ""), *evidence]
        if any(_has_control_chars(str(t)) for t in text_fields):
            rejected["control_character_abuse"] += 1
            continue
        seen.add(key)
        accepted.append(p)
    return accepted, rejected


def main() -> int:
    _ensure_utf8_stdout()
    ap = argparse.ArgumentParser()
    ap.add_argument("--vault", default=".")
    ap.add_argument("--limit", type=int, default=1000)
    ap.add_argument("--lifecycle", default="")
    ap.add_argument("--ollama", action="store_true")
    ap.add_argument("--model", default="qwen2.5-coder:3b")
    ap.add_argument("--out", default="06_INBOX/edge_proposals.json")
    ap.add_argument("--metrics-out", default="")
    args = ap.parse_args()

    vault = Path(args.vault)
    lifecycles = [l for l in args.lifecycle.split(",") if l] or None
    index = VaultIndex.load(vault, lifecycles=lifecycles)
    print(f"nodes: {len(index)}")

    run_id = f"edgeprop_{uuid.uuid4().hex[:12]}"
    timestamp = datetime.now(timezone.utc).isoformat(timespec="seconds")

    det_proposals, candidate_pairs = deterministic_candidates(index, limit=args.limit)
    print(f"candidate pairs (pre-threshold): {candidate_pairs}")
    print(f"deterministic proposals (post-threshold): {len(det_proposals)}")

    provider, model = "deterministic", "none"
    all_raw = det_proposals
    if args.ollama:
        refined = classify_with_ollama(det_proposals, index, args.model)
        print(f"LLM-classified: {len(refined)} / {min(len(det_proposals), 200)} attempted")
        if refined:
            all_raw = refined
            provider, model = "ollama", args.model

    for p in all_raw:
        p["extraction_run_id"] = run_id
        p["provider"] = provider
        p["model"] = model
        p["timestamp"] = timestamp
        p["status"] = _STATUS_PENDING

    accepted, rejected = validate_proposals(all_raw, index)
    accepted_strong = sum(1 for p in accepted if p["relation"] in STRONG_RELATIONS)
    accepted_weak = sum(1 for p in accepted if p["relation"] in WEAK_RELATIONS)
    resolvable_targets = sum(1 for p in all_raw if p.get("target_id") in index.by_id)
    valid_target_ratio = round(resolvable_targets / max(len(all_raw), 1), 4)
    edges_per_node = round(len(accepted) * 2 / max(len(index), 1), 3)

    metrics = {
        "candidate_pairs": candidate_pairs,
        "raw_proposals": len(all_raw),
        "accepted_strong": accepted_strong,
        "accepted_weak": accepted_weak,
        "accepted_total": len(accepted),
        "rejected_total": sum(rejected.values()),
        "reject_reasons": dict(rejected),
        "valid_target_ratio": valid_target_ratio,
        "edges_per_node": edges_per_node,
    }
    print("--- metrics (NOT just an edge count) ---")
    for k, v in metrics.items():
        print(f"  {k}: {v}")

    out = vault / args.out
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps({
        "generated": timestamp,
        "run_id": run_id,
        "status": _STATUS_PENDING,
        "metrics": metrics,
        "proposals": accepted,
    }, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"-> {out}")

    if args.metrics_out:
        mpath = vault / args.metrics_out
        mpath.parent.mkdir(parents=True, exist_ok=True)
        mpath.write_text(json.dumps(metrics, indent=2, ensure_ascii=False), encoding="utf-8")
        print(f"metrics -> {mpath}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
