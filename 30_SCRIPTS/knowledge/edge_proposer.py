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

ROOT = Path(__file__).resolve().parents[2]
PACKAGES = ROOT / "03_IMPLEMENTATION" / "packages"
for p in (str(ROOT), str(PACKAGES)):
    if p not in sys.path:
        sys.path.insert(0, p)

try:
    from retrieval.hybrid_retrieval import entities, tokenize  # noqa: E402
    from graph.synapse_store import (  # noqa: E402
        ALLOWED_RELATIONS,
        STRONG_RELATIONS,
        WEAK_RELATIONS,
        MAX_WEIGHT,
        MIN_WEIGHT,
        WEAK_WEIGHT_FACTOR,
        RELATION_BASE_WEIGHT,
        HUB_IN_DEGREE_THRESHOLD,
    )
    from retrieval.vault_index import VaultIndex  # noqa: E402
except ImportError:
    from cognitive_core.hybrid_retrieval import entities, tokenize  # noqa: E402
    from cognitive_core.synapse_store import (  # noqa: E402
        ALLOWED_RELATIONS,
        STRONG_RELATIONS,
        WEAK_RELATIONS,
        MAX_WEIGHT,
        MIN_WEIGHT,
        WEAK_WEIGHT_FACTOR,
        RELATION_BASE_WEIGHT,
        HUB_IN_DEGREE_THRESHOLD,
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


# Entities that cause spurious vocabulary co-occurrence without query-time semantic value
SPURIOUS_ENTITIES = frozenset({
    # Dunders & python internals
    "__init__", "__file__", "__code__", "__name__", "__doc__",
    "__future__", "__main__", "__all__", "__dict__", "__class__",
    "__module__", "__annotations__", "__builtins__",
    # Python standard modules & testing tokens
    "sys", "os", "pathlib", "typing", "datetime", "subprocess",
    "argparse", "hashlib", "collections", "itertools", "functools",
    "unittest", "pytest",
    # Generic versions & IPs
    "1.0.0", "2.0.0", "3.0.0", "3.14.2", "9.0.2", "127.0.0",
    # Dataview query keywords
    "desc", "sort", "table", "where", "from",
    # Common English & metadata tokens that slip through entity regex
    "can", "must", "not", "for", "id", "it", "head", "net", "cause", "crud",
    "current", "task", "next", "status", "action", "agents", "api", "artifact",
    "conversation-evidence", "obsidian-sync", "review", "yaml", "json", "sha",
    "source_type", "original_path", "source_ref", "security", "unknown", "confirmed",
    "rfc", "aa", "ac", "dr", "tl", "ci", "mve", "end", "file_engine", "admin",
    "memory", "raw", "verification", "approval", "human", "claude_original", "perplexity_original",
    # Second hardening round. Each of these was observed joining two unrelated
    # notes in a hand-verified sample.
    # Generic English/code tokens the entity regex admits as acronyms:
    "and", "get", "set", "exists", "model", "real", "fail", "http", "https",
    "none", "important", "strict", "control", "true", "false", "null", "type",
    # Document boilerplate shared by every note carrying a legal header:
    "avertisment", "capitolul", "cee", "celex", "conformitate",
    "detaliidocument", "disclaimer", "instruction_trust", "juridic", "iii",
    # Framework/stack names. Two notes sharing a UI toolkit are not topically
    # related: WPF joined a military transfer register to a forensics tool.
    "wpf", "mvvm", "httpclient", "textbox", "mainwindow", "rest", "cli",
    # Third round. Romanian legal-document furniture: every act published in
    # Monitorul Oficial carries these, so they joined MiCA to a military order.
    "consiliul", "consiliului", "european", "europene", "eur", "parlamentul",
    "monitorul", "oficial", "emitent", "anexa", "lista", "regim", "finale",
    "care", "pentru", "din", "sau", "prin", "asupra", "autorizarea",
    "requires_legal_review", "verified_source", "url", "knowledge",
    # Fourth round (Punctul 5 - Curriculum OpenStax):
    # Book-level boilerplate entities shared across all 16 OpenStax sections.
    # Without this filter, all 120 note-pairs share these entities.
    "openstax", "psychology", "curriculum", "ch08",
    "provenance_manifest", "verified-source", "cc-by",
    "source_ref", "source_date", "extraction_date", "original_path",
    # Fifth round (PR #168 audit findings):
    # Meta-analysis, audit terms, and common evaluation tokens
    "delete_canonical", "high", "risk", "policy", "lesson", "audit",
    "wrong_type", "context", "system", "architecture", "procedure",
    "evaluation", "report", "evidence", "sample", "metric", "target",
    "source", "result", "results", "notes", "note", "rule", "rules",
})

#: Runs of underscores and similar rules used as visual separators in legal
#: documents are matched as entities and joined two unrelated acts.
FILLER_RE = re.compile(r"^[_=.\-\s]+$")

#: Dates and CELEX-style numerics leak in as entities and tie unrelated EU
#: regulations together purely by their citation headers.
DATE_LIKE_RE = re.compile(r"^\d{1,2}\.\d{1,2}\.\d{4}$|^\d{4}$")

#: Version numbers, semver tags, and IP address literals (e.g. 5.0.45, 127.0.0.1, v1.2.3)
VERSION_OR_IP_RE = re.compile(r"^v?\d+(\.\d+)+$")

#: Python dunders (__future__, __main__, etc.)
DUNDER_RE = re.compile(r"^__[a-zA-Z0-9_]+__$")


def is_spurious_entity(e: str) -> bool:
    """Predicate evaluating whether an entity token is spurious / boilerplate."""
    e_low = e.lower().strip()
    if not e_low or len(e_low) < 4:
        return True
    if e_low in SPURIOUS_ENTITIES:
        return True
    if DUNDER_RE.match(e_low):
        return True
    if VERSION_OR_IP_RE.match(e_low):
        return True
    if DATE_LIKE_RE.match(e_low):
        return True
    if FILLER_RE.match(e_low):
        return True
    return False

# Notes that are session dumps or transient agent scratchpads rather than
# durable knowledge. Measured on the r007 sample, these accounted for 37% of
# proposals and were judged FALSE in every case a human reviewed: takeover
# packages and implementation plans share large volumes of internal system
# vocabulary with each other, and CURRENT.md files change hourly, so any edge
# to one is stale before it is reviewed.
EPHEMERAL_PATH_MARKERS = (
    "/Artifacts/", "\\Artifacts\\",
    "CURRENT.md", "STATUS_SNAPSHOT", "Continuity_Handoff",
    "walkthrough", "implementation_plan", "prompt_draft", "phase3_readiness",
    "TAKEOVER",
)

#: A pair must share at least one entity this rare. Summed IDF alone is not
#: enough: six entities at DF=19 sum past the normalizer and saturate the
#: score at 1.0, so six mediocre matches score exactly like one precise one.
#: Requiring a rare hook is what separates "both notes mention infosec and
#: disclaimer" from "both notes are about MT5 trading bots".
RARE_ENTITY_DF_MAX = 5

#: Minimum normalised overlap between two notes' entity sets, as the geometric
#: mean of |shared|/|A| and |shared|/|B|.
MIN_OVERLAP_COVERAGE = 0.10


def _is_ephemeral(note) -> bool:
    path = str(getattr(note, "path", ""))
    return any(marker in path for marker in EPHEMERAL_PATH_MARKERS)


FORBIDDEN_HUBS = frozenset({
    "Knowledge Graph Home", "08 Memory Subsystems Map", "00 Core Map", "02 Memory Knowledge Map",
    "Skill Agent Memory MOC", "00_CORE__Knowledge_Graph_Home", "00_CORE__Core_Map",
})


def build_entity_df(index: VaultIndex) -> Tuple[Dict[str, set], Counter]:
    ent_by_note: Dict[str, set] = {}
    df: Counter = Counter()
    for note in index.notes:
        ents = {
            e.lower().strip()
            for e in (entities(note.text) | set(note.tags))
            if not is_spurious_entity(e)
        }
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


def extract_evidence_quote(text: str, target_entities: Iterable[str], max_len: int = 300) -> str:
    """Finds an exact verbatim sentence from text containing at least one target entity with word boundary.
    Returns empty string if no qualifying sentence containing a target entity is found.
    The returned quote is guaranteed to be a verbatim substring of text."""
    if not text:
        return ""
    sentences = re.split(r"(?<=[.!?\n])\s+", text)
    valid_ents = [e.strip() for e in target_entities if len(e.strip()) >= 3 and not is_spurious_entity(e)]
    if not valid_ents:
        return ""
    pattern = re.compile(r"\b(" + "|".join(re.escape(e) for e in valid_ents) + r")\b", re.IGNORECASE)
    for s in sentences:
        s_clean = s.strip()
        if not s_clean or len(s_clean) < 15:
            continue
        if pattern.search(s_clean):
            if s_clean in text:
                return s_clean if len(s_clean) <= max_len else s_clean[:max_len]
            idx = text.find(s_clean)
            if idx != -1:
                sub = text[idx:idx + len(s_clean)]
                return sub if len(sub) <= max_len else sub[:max_len]
    return ""


def _has_explicit_reference(src_note: Any, target_note: Any) -> bool:
    """Checks whether src_note explicitly references target_note via
    outgoing_ids, wikilink, markdown link, explicit ID mention, or frontmatter relation field."""
    target_id = getattr(target_note, "id", "")
    target_stem = getattr(getattr(target_note, "path", None), "stem", "")
    target_title = getattr(target_note, "title", "")
    body = getattr(src_note, "body", "") or ""

    # 1. Declared outgoing IDs from frontmatter parser
    if target_id and target_id in getattr(src_note, "outgoing_ids", lambda: [])():
        return True

    # 2. Exact ID citation (e.g. note-123 or SEC-001) as a whole word
    if target_id and len(target_id) >= 4 and target_id.lower() not in {"index", "readme", "note"}:
        if re.search(rf"\b{re.escape(target_id)}\b", body, re.IGNORECASE):
            return True

    # 3. Wikilinks via method or regex: [[target_id]], [[target_stem]], [[target_title]]
    for wl in getattr(src_note, "wikilinks", lambda: [])():
        wl_low = wl.lower()
        for identifier in (target_id, target_stem, target_title):
            if identifier and len(identifier) >= 4 and identifier.lower() not in {"index", "readme", "note", "test"}:
                if identifier.lower() in wl_low:
                    return True

    for identifier in (target_id, target_stem, target_title):
        if identifier and len(identifier) >= 4 and identifier.lower() not in {"index", "readme", "note", "test"}:
            if re.search(rf"\[\[(?:[^\]]*[/\\])?{re.escape(identifier)}(?:\|[^\]]+)?\]\]", body, re.IGNORECASE):
                return True

    # 4. Markdown links: [text](...target_stem...)
    if target_stem and len(target_stem) >= 4 and target_stem.lower() not in {"index", "readme", "note"}:
        if re.search(rf"\]\([^)]*{re.escape(target_stem)}(\.md)?\)", body, re.IGNORECASE):
            return True

    # 5. Frontmatter fields
    meta = getattr(src_note, "meta", {}) or {}
    for field in ("depends_on", "supersedes", "verified_by", "relations", "links", "related"):
        val = meta.get(field)
        if not val:
            continue
        if isinstance(val, (str, int)):
            val = [str(val)]
        if isinstance(val, list):
            for item in val:
                if isinstance(item, dict):
                    target_cand = str(item.get("target_id") or item.get("target") or "")
                else:
                    target_cand = str(item).strip()
                target_cand_low = target_cand.lower()
                if not target_cand_low:
                    continue
                for target_val in (target_id, target_stem, target_title):
                    if target_val and len(target_val) >= 4 and target_val.lower() in target_cand_low:
                        return True
    return False


#: A keyword only states a relation when it sits in the same passage as the
#: reference to the other note. "Same passage" is the line plus the two lines
#: around it: enough for a bullet, a table row or a sentence that wraps,
#: narrow enough that a word in section 2 cannot bind a link in section 9.
_PASSAGE_RADIUS = 2


def _references_near(src_note: Any, target_note: Any, keywords: List[str]) -> bool:
    """True when src_note names target_note within two lines of a keyword."""
    if not _has_explicit_reference(src_note, target_note):
        return False
    body = (getattr(src_note, "body", "") or "")
    lines = body.splitlines()
    identifiers = [
        str(x).lower()
        for x in (
            getattr(target_note, "id", ""),
            getattr(getattr(target_note, "path", None), "stem", ""),
            getattr(target_note, "title", ""),
        )
        if x and len(str(x)) >= 4
    ]
    if not identifiers:
        return False
    hit_lines = [
        i for i, line in enumerate(lines)
        if any(ident in line.lower() for ident in identifiers)
    ]
    if not hit_lines:
        # The reference came from frontmatter, which has no passage of its own;
        # a declared relation is evidence enough without a keyword nearby.
        return True
    for i in hit_lines:
        window = " ".join(lines[max(0, i - _PASSAGE_RADIUS): i + _PASSAGE_RADIUS + 1]).lower()
        if any(k in window for k in keywords):
            return True
    return False


def classify_relation(na: Any, nb: Any, shared_ents: set[str]) -> Tuple[str, str, str]:
    """Classify relation between two notes into ALLOWED_RELATIONS with direction.
    Strong relations (depends_on, supersedes, verified_by) require explicit evidence."""
    a, b = na.id, nb.id
    body_a_low = (na.body or "").lower()
    body_b_low = (nb.body or "").lower()

    # 0. Check declared frontmatter relations first
    for rel_dict in getattr(na, "relations", lambda: [])():
        target = rel_dict.get("target_id")
        rel_type = rel_dict.get("relation") or rel_dict.get("type")
        if target == b and rel_type in ALLOWED_RELATIONS:
            return rel_type, a, b
    for rel_dict in getattr(nb, "relations", lambda: [])():
        target = rel_dict.get("target_id")
        rel_type = rel_dict.get("relation") or rel_dict.get("type")
        if target == a and rel_type in ALLOWED_RELATIONS:
            return rel_type, b, a

    # 1. Supersedes (temporal/versioned replacement)
    # Two notes sharing a subject and carrying different timestamps used to be
    # enough. The 50-edge audit found 0 of 5 sampled `supersedes` edges correct:
    # a similar title and a newer date say nothing about replacement. One of the
    # notes now has to point at the other.
    if _same_subject(na.title, nb.title) and (
        _has_explicit_reference(na, nb) or _has_explicit_reference(nb, na)
    ):
        ua, ub = na.updated, nb.updated
        if ua and ub and ua != ub:
            (src, dst) = (a, b) if ua > ub else (b, a)
            return "supersedes", src, dst

    if _has_explicit_reference(na, nb) and any(k in body_a_low for k in ["supersedes", "înlocuiește"]):
        return "supersedes", a, b
    if _has_explicit_reference(nb, na) and any(k in body_b_low for k in ["supersedes", "înlocuiește"]):
        return "supersedes", b, a

    # 2. Applies_to (lessons applied to procedures/rules)
    # Being a lesson and a procedure is not a relationship: every lesson would
    # apply to every procedure. The lesson has to name the procedure.
    if na.type in LESSON_TYPES and nb.type in PROCEDURE_TYPES and _has_explicit_reference(na, nb):
        return "applies_to", a, b
    if nb.type in LESSON_TYPES and na.type in PROCEDURE_TYPES and _has_explicit_reference(nb, na):
        return "applies_to", b, a

    # 3. Verified_by (tests verifying architecture/knowledge)
    # Strong relation: requires explicit reference between test and verified target
    stem_a = getattr(getattr(na, "path", None), "stem", "")
    stem_b = getattr(getattr(nb, "path", None), "stem", "")
    if (na.type == "test" or "test_" in stem_a) and nb.type in {"knowledge", "procedure", "architecture", "lesson"}:
        if _has_explicit_reference(na, nb) or _has_explicit_reference(nb, na):
            return "verified_by", b, a
    if (nb.type == "test" or "test_" in stem_b) and na.type in {"knowledge", "procedure", "architecture", "lesson"}:
        if _has_explicit_reference(na, nb) or _has_explicit_reference(nb, na):
            return "verified_by", a, b

    # 4. Depends_on (prerequisites and dependencies)
    # Strong relation: requires explicit reference between notes
    dep_keywords = ["depends on", "prerequisite", "cerință prealabilă", "requires", "depinde de", "bazează pe"]
    # The keyword has to appear in the same passage as the reference. A note
    # that says "requires" in one section and links the other note three pages
    # down states no dependency; that produced 17 of the 20 rejected
    # `depends_on` edges in the audit.
    if _references_near(na, nb, dep_keywords):
        return "depends_on", a, b
    if _references_near(nb, na, dep_keywords):
        return "depends_on", b, a

    # 5. Caused (causal progression)
    # These fired on a keyword appearing anywhere in one note, with nothing
    # tying it to the other note. Same passage rule as `depends_on`.
    cause_keywords = ["caused by", "cauzat de", "a dus la", "consequence of", "rezultat din"]
    if _references_near(na, nb, cause_keywords):
        return "caused", a, b
    if _references_near(nb, na, cause_keywords):
        return "caused", b, a

    # 6. Contradicts (opposing findings, contrast)
    contra_keywords = ["contradicts", "în contradicție", "disputes", "contrazice", "spre deosebire de"]
    if _references_near(na, nb, contra_keywords):
        return "contradicts", a, b
    if _references_near(nb, na, contra_keywords):
        return "contradicts", b, a

    # 7. Part_of (containment, chapter/section, subproject)
    # `na.type == "project"` alone made every note that shared a word with a
    # project a part of it, in the wrong direction; that is audit case #26.
    # Containment now needs the containing note to name the contained one.
    part_keywords = ["part of", "parte din", "capitol", "chapter", "subsystem", "component"]
    if _references_near(na, nb, part_keywords):
        return "part_of", a, b
    if _references_near(nb, na, part_keywords):
        return "part_of", b, a
    if na.type == "project" and nb.type != "project" and _has_explicit_reference(na, nb):
        return "part_of", b, a
    if nb.type == "project" and na.type != "project" and _has_explicit_reference(nb, na):
        return "part_of", a, b

    return "related_to", a, b


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
    duplicate_pairs_skipped = 0
    for (a, b), raw in sorted(pair_scores.items(), key=lambda p: -p[1])[:limit * 3]:
        na, nb = index.by_id.get(a), index.by_id.get(b)
        if na is None or nb is None or a == b:
            continue
        if (na.title in FORBIDDEN_HUBS or nb.title in FORBIDDEN_HUBS or
            na.path.stem in FORBIDDEN_HUBS or nb.path.stem in FORBIDDEN_HUBS):
            continue
        if len(pair_shared[(a, b)]) < MIN_SHARED_ENTITIES:
            continue
        if _is_ephemeral(na) or _is_ephemeral(nb):
            continue

        # Duplicate detection: identical or near-identical body content is duplicate, not semantic relation
        norm_body_a = re.sub(r"\s+", " ", getattr(na, "body", "") or "").strip().lower()
        norm_body_b = re.sub(r"\s+", " ", getattr(nb, "body", "") or "").strip().lower()
        if len(norm_body_a) >= 100 and norm_body_a == norm_body_b:
            duplicate_pairs_skipped += 1
            continue
        # Byte equality missed the auto-generated policy lessons: they differ
        # only by an embedded id, and the audit rejected 6 sampled edges as
        # duplicate content. Near-identity over word multisets catches those
        # without touching notes that merely share vocabulary.
        if len(norm_body_a) >= 100 and len(norm_body_b) >= 100:
            wa, wb = Counter(norm_body_a.split()), Counter(norm_body_b.split())
            overlap = sum((wa & wb).values())
            if overlap / max(sum(wa.values()), sum(wb.values())) >= 0.98:
                duplicate_pairs_skipped += 1
                continue

        # At least one shared entity must be genuinely rare.
        if min(df[e] for e in pair_shared[(a, b)]) > RARE_ENTITY_DF_MAX:
            continue
        n_shared = len(pair_shared[(a, b)])
        size_a, size_b = len(ent_by_note.get(a, ())), len(ent_by_note.get(b, ()))
        if not size_a or not size_b:
            continue
        coverage = math.sqrt((n_shared / size_a) * (n_shared / size_b))
        if coverage < MIN_OVERLAP_COVERAGE:
            continue
        score = min(1.0, raw / norm)
        if score < MIN_SCORE:
            continue

        relation, src, dst = classify_relation(na, nb, pair_shared[(a, b)])
        shared_ents = sorted(pair_shared[(a, b)])[:6]
        src_note = index.by_id[src]
        dst_note = index.by_id[dst]
        sq = extract_evidence_quote(src_note.body, shared_ents)
        tq = extract_evidence_quote(dst_note.body, shared_ents)
        if not sq or not tq:
            continue

        origin = "proposed_weak" if relation in WEAK_RELATIONS else "proposed"
        proposals.append({
            "source_id": src, "target_id": dst, "relation": relation,
            "confidence": round(score, 4),
            "weight": _weight_for(relation, score),
            "origin": origin,
            "evidence_entities": shared_ents,
            "source_quote": sq,
            "target_quote": tq,
            "source_path": src_note.path.as_posix(),
            "target_path": dst_note.path.as_posix(),
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
        sq = p.get("source_quote")
        tq = p.get("target_quote")
        if not sq or not tq or not isinstance(sq, str) or not isinstance(tq, str):
            rejected["missing_quote_evidence"] += 1
            continue
        key = (src, dst, relation)
        if key in seen:
            rejected["duplicate_edge"] += 1
            continue
        text_fields = [p.get("source_path", ""), p.get("target_path", ""), sq, tq, *evidence]
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
