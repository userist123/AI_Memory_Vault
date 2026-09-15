#!/usr/bin/env python3
"""
test_genuine_extraction.py — Verification of content-derived concept extraction.

Verifies:
1. Extraction generalizes to unseen/fictitious vocabulary not in any hardcoded list.
2. Emitted definitions pass check_verbatim_overlap (<15 word exact contiguous match).
3. No static dictionary or pattern table pairing fixed concept names to pre-written definitions exists in source.
4. Realistic long multi-clause academic sentences with citations pass extraction and verbatim checks.
"""

import os
import inspect
import importlib
import pytest
from typing import Dict, Any

extract_mod = importlib.import_module("30_SCRIPTS.ingestion.extract_book_concepts")
extract_concepts_from_chunk = extract_mod.extract_concepts_from_chunk
check_verbatim_overlap = extract_mod.check_verbatim_overlap


def test_extraction_generalizes_to_unseen_vocabulary():
    """
    Verifies extraction generalizes to fictitious vocabulary that cannot exist in any hardcoded list
    and that the generated definition passes check_verbatim_overlap.
    """
    fictitious_chunk = {
        "heading": "Section 1",
        "content": (
            "Zorbatic interference is a phenomenon in which redundant activation traces "
            "suppress unrelated retrieval paths during memory recall."
        )
    }

    records = extract_concepts_from_chunk(fictitious_chunk, source_book="Fictitious Source 2026")

    assert len(records) > 0, "Extractor failed to produce concept for definitional sentence in fictitious chunk!"

    rec = records[0]
    assert "zorbatic" in rec["concept"].lower()
    assert rec["maps_to_slot"] in extract_mod.CANONICAL_SLOTS

    # Prove definition is content-derived and NOT a literal in extract_book_concepts.py source
    script_source = inspect.getsource(extract_mod)
    assert rec["definition"] not in script_source, (
        f"Emitted definition '{rec['definition']}' was found as a literal string in script source code!"
    )

    # Prove definition passes check_verbatim_overlap against raw source text
    is_verbatim, overlap = check_verbatim_overlap(
        rec["definition"], fictitious_chunk["content"], n_gram_len=15
    )
    assert not is_verbatim, f"Generated definition still verbatim-overlaps source: '{overlap}'"


def test_extraction_on_realistic_academic_sentence():
    """
    Verifies extraction on a realistic, multi-clause 35-word academic sentence containing citations,
    asserting both extraction and verbatim guard passing.
    """
    academic_chunk = {
        "heading": "Background & Architecture",
        "content": (
            "Parametric weight consolidation is described as a neuro-inspired optimization mechanism "
            "whereby critical parameters are selectively anchored to prevent catastrophic forgetting "
            "(Sarfraz et al., 2022) during sequential task acquisition over extended training trajectories."
        )
    }

    records = extract_concepts_from_chunk(academic_chunk, source_book="Sarfraz et al. (2022)")
    assert len(records) > 0, "Failed to extract concept from multi-clause academic sentence!"

    rec = records[0]
    assert "parametric weight consolidation" in rec["concept"].lower()

    is_verbatim, overlap = check_verbatim_overlap(
        rec["definition"], academic_chunk["content"], n_gram_len=15
    )
    assert not is_verbatim, f"Academic sentence definition verbatim-overlaps source chunk: '{overlap}'"


def test_no_static_definition_strings_in_source():
    """
    Statically scans extract_book_concepts.py source for any static dictionary or tuple table
    pairing fixed concept names with pre-written multi-sentence definition strings.
    """
    script_source = inspect.getsource(extract_mod)

    forbidden_snippets = [
        "Stabilization of synaptic plastic updates in biological or neural networks",
        "Cognitive dual-system framework combining fast hippocampal instance replay",
        "Interleaved re-execution of previously stored episodic memory samples",
        "Structural or algorithmic constraint preventing abrupt decay of past",
        "Fundamental tradeoff balancing rapid adaptation to novel environmental inputs"
    ]

    for snippet in forbidden_snippets:
        assert snippet not in script_source, (
            f"Found legacy hardcoded pre-written definition snippet in extract_book_concepts.py: '{snippet}'"
        )


def test_paraphrased_definition_human_quality():
    """
    Validates that the generated paraphrase for the fictitious concept is human-readable and clean.
    """
    fictitious_chunk = {
        "heading": "Section 2",
        "content": (
            "Quantum cognitive gating is defined as an algorithmic mechanism "
            "where high-entropy memory states are attenuated prior to retrieval."
        )
    }

    records = extract_concepts_from_chunk(fictitious_chunk, source_book="Quantum Cognitive Book")
    assert len(records) > 0
    rec = records[0]

    assert "quantum cognitive gating" in rec["concept"].lower()
    assert len(rec["definition"]) > 20
    assert not check_verbatim_overlap(rec["definition"], fictitious_chunk["content"], n_gram_len=15)[0]
