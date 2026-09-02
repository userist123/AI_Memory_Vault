"""evaluation/context_packing/packer_adapters.py — P1 Context Packing Strategies.

Provides isolated packing strategies without modifying production ContextPackBuilder:
  - P0: Current Production Baseline (ContextPackBuilder degradation & truncation)
  - P1: Full Context Candidate Oracle (untrimmed candidate note contents)
  - P2: Section-Aware Extractive Packer (header-based chunk scoring)
  - P3: Section-Aware + Invariant & Negation Clause Protection
  - P4: Section-Aware + Invariant Protection + Provenance-Preserving Deduplication
"""
from __future__ import annotations

import re
from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional, Set, Tuple

from memory_controller.context.pack_builder import ContextPackBuilder


@dataclass
class SectionChunk:
    note_id: str
    header: str
    content: str
    score: float = 0.0
    has_invariant: bool = False
    has_negation: bool = False
    entities_matched: List[str] = field(default_factory=list)
    facts_matched: List[str] = field(default_factory=list)

    @property
    def full_text(self) -> str:
        return f"[{self.note_id} - {self.header}]\n{self.content.strip()}"


class PackerAdapters:
    """Adapters for evaluating various context packing strategies."""

    NEGATION_KEYWORDS = {
        "not", "never", "cannot", "gated", "forbidden", "must not",
        "only", "except", "unless", "immutable", "block", "blochează",
        "prohibits", "strictly", "no"
    }

    INVARIANT_KEYWORDS = {
        "p0", "p1", "p2", "p3", "p4", "p5", "p6", "p7", "p8", "p9",
        "p10", "p11", "p12", "p13", "p14", "p15", "p16", "p17", "p18",
        "pragma", "busy_timeout", "immediate", "max_council_agents",
        "max_primary_agents", "max_specialist_output", "max_synthesis_input",
        "tasks/todo.md", "tasks/lessons.md", "attest", "attestation",
        "source_type", "lifecycle", "review", "active"
    }

    @staticmethod
    def pack_p0_current(
        candidates: List[Dict[str, Any]],
        budget: Dict[str, Any],
        request_id: str = "req_p0",
        agent_id: str = "ai_agent",
    ) -> Dict[str, Any]:
        """P0: Production ContextPackBuilder with default degradation."""
        pack_builder = ContextPackBuilder()
        pack = pack_builder.build(
            request_id=request_id,
            agent_id=agent_id,
            budget=budget,
            results=candidates,
            disclosure_level="full",
        )
        packed_text = "\n---\n".join([
            f"[{r.get('id')}]: {r.get('content', '')}"
            for r in pack.get("results", [])
        ])
        return {
            "strategy": "P0",
            "packed_text": packed_text,
            "results": pack.get("results", []),
            "sections_kept": len(pack.get("results", [])),
            "sections_dropped": max(0, len(candidates) - len(pack.get("results", []))),
        }

    @staticmethod
    def pack_p1_full_context(candidates: List[Dict[str, Any]]) -> Dict[str, Any]:
        """P1: Full Context Candidate Oracle (untrimmed)."""
        packed_text = "\n---\n".join([
            f"[{c.get('id')}]: {c.get('content', '')}"
            for c in candidates
        ])
        return {
            "strategy": "P1",
            "packed_text": packed_text,
            "results": candidates,
            "sections_kept": len(candidates),
            "sections_dropped": 0,
        }

    @classmethod
    def _extract_sections(cls, note: Dict[str, Any]) -> List[SectionChunk]:
        """Parses a Markdown note into structural section chunks."""
        nid = str(note.get("id", "unknown_note"))
        raw_content = str(note.get("content", ""))
        lines = raw_content.split("\n")

        sections: List[SectionChunk] = []
        current_header = "Overview"
        current_lines: List[str] = []

        for line in lines:
            if line.strip().startswith(("# ", "## ", "### ", "#### ")):
                if current_lines:
                    text_block = "\n".join(current_lines).strip()
                    if text_block:
                        sections.append(SectionChunk(note_id=nid, header=current_header, content=text_block))
                    current_lines = []
                current_header = line.strip().lstrip("#").strip()
            else:
                current_lines.append(line)

        if current_lines:
            text_block = "\n".join(current_lines).strip()
            if text_block:
                sections.append(SectionChunk(note_id=nid, header=current_header, content=text_block))

        if not sections and raw_content.strip():
            sections.append(SectionChunk(note_id=nid, header="Full", content=raw_content.strip()))

        return sections

    @classmethod
    def _score_section(
        cls,
        section: SectionChunk,
        query_tokens: Set[str],
        required_facts: List[str],
        entities: List[str],
    ) -> float:
        """Computes deterministic utility score for a section."""
        content_lower = section.content.lower()
        header_lower = section.header.lower()
        full_lower = f"{header_lower} {content_lower}"

        # 1. Query token overlap
        sec_tokens = set(re.findall(r"[a-zA-Z0-9_\-]+", full_lower))
        q_overlap = len(query_tokens.intersection(sec_tokens))

        # 2. Required fact matches
        fact_matches = [f for f in required_facts if f.lower() in full_lower]
        section.facts_matched = fact_matches

        # 3. Entity matches
        ent_matches = [e for e in entities if e.lower() in full_lower]
        section.entities_matched = ent_matches

        # 4. Invariant keywords
        has_inv = any(inv in full_lower for inv in cls.INVARIANT_KEYWORDS)
        section.has_invariant = has_inv

        # 5. Negation keywords
        has_neg = any(re.search(rf"\b{neg}\b", full_lower) for neg in cls.NEGATION_KEYWORDS)
        section.has_negation = has_neg

        score = (
            (q_overlap * 1.0) +
            (len(fact_matches) * 5.0) +
            (len(ent_matches) * 3.0) +
            (2.5 if has_inv else 0.0) +
            (1.5 if has_neg else 0.0)
        )
        section.score = score
        return score

    @classmethod
    def pack_p2_section_aware(
        cls,
        candidates: List[Dict[str, Any]],
        query: str,
        required_facts: List[str],
        entities: List[str],
        max_tokens: int = 1800,
    ) -> Dict[str, Any]:
        """P2: Section-Aware Extractive Packing."""
        q_tokens = set(re.findall(r"[a-zA-Z0-9_\-]+", query.lower()))
        all_sections: List[SectionChunk] = []

        for c in candidates:
            secs = cls._extract_sections(c)
            for s in secs:
                cls._score_section(s, q_tokens, required_facts, entities)
                all_sections.append(s)

        # Sort sections by score descending
        sorted_sections = sorted(all_sections, key=lambda s: s.score, reverse=True)

        kept_sections: List[SectionChunk] = []
        cur_chars = 0
        max_chars = max_tokens * 3  # approx 3 chars per token

        for sec in sorted_sections:
            sec_len = len(sec.full_text)
            if cur_chars + sec_len <= max_chars or len(kept_sections) == 0:
                kept_sections.append(sec)
                cur_chars += sec_len

        packed_text = "\n\n".join([s.full_text for s in kept_sections])
        return {
            "strategy": "P2",
            "packed_text": packed_text,
            "sections_kept": len(kept_sections),
            "sections_dropped": max(0, len(all_sections) - len(kept_sections)),
        }

    @classmethod
    def pack_p3_fact_invariant_protected(
        cls,
        candidates: List[Dict[str, Any]],
        query: str,
        required_facts: List[str],
        entities: List[str],
        max_tokens: int = 1800,
    ) -> Dict[str, Any]:
        """P3: Section-Aware + Critical Invariant & Negation Clause Protection."""
        q_tokens = set(re.findall(r"[a-zA-Z0-9_\-]+", query.lower()))
        all_sections: List[SectionChunk] = []

        for c in candidates:
            secs = cls._extract_sections(c)
            for s in secs:
                cls._score_section(s, q_tokens, required_facts, entities)
                # Heavy boost for sections containing verified required facts or guardrail negations
                if s.facts_matched:
                    s.score += len(s.facts_matched) * 10.0
                if s.has_invariant and s.has_negation:
                    s.score += 15.0
                all_sections.append(s)

        # Sort sections by augmented score
        sorted_sections = sorted(all_sections, key=lambda s: s.score, reverse=True)

        kept_sections: List[SectionChunk] = []
        cur_chars = 0
        max_chars = max_tokens * 3

        # First pass: Mandatory invariant and fact-bearing sections
        for sec in sorted_sections:
            if sec.facts_matched or (sec.has_invariant and sec.has_negation):
                sec_len = len(sec.full_text)
                if cur_chars + sec_len <= max_chars or len(kept_sections) == 0:
                    kept_sections.append(sec)
                    cur_chars += sec_len

        # Second pass: Fill remaining budget with top context sections
        for sec in sorted_sections:
            if sec not in kept_sections:
                sec_len = len(sec.full_text)
                if cur_chars + sec_len <= max_chars:
                    kept_sections.append(sec)
                    cur_chars += sec_len

        packed_text = "\n\n".join([s.full_text for s in kept_sections])
        return {
            "strategy": "P3",
            "packed_text": packed_text,
            "sections_kept": len(kept_sections),
            "sections_dropped": max(0, len(all_sections) - len(kept_sections)),
        }

    @classmethod
    def pack_p4_fact_protected_dedup(
        cls,
        candidates: List[Dict[str, Any]],
        query: str,
        required_facts: List[str],
        entities: List[str],
        max_tokens: int = 1800,
    ) -> Dict[str, Any]:
        """P4: P3 + Provenance-Preserving Exact Clause Deduplication."""
        p3_pack = cls.pack_p3_fact_invariant_protected(
            candidates, query, required_facts, entities, max_tokens=max_tokens
        )
        raw_text = p3_pack["packed_text"]

        lines = raw_text.split("\n")
        dedup_lines: List[str] = []
        seen_line_sigs: Set[str] = set()

        for line in lines:
            clean_line = line.strip().lower()
            if not clean_line or clean_line.startswith("["):
                dedup_lines.append(line)
                continue

            sig = re.sub(r"\s+", " ", clean_line)
            if sig not in seen_line_sigs:
                seen_line_sigs.add(sig)
                dedup_lines.append(line)

        dedup_text = "\n".join(dedup_lines)
        return {
            "strategy": "P4",
            "packed_text": dedup_text,
            "sections_kept": p3_pack["sections_kept"],
            "sections_dropped": p3_pack["sections_dropped"],
        }
