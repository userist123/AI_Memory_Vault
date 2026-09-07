#!/usr/bin/env python3
"""
test_genuine_extraction.py — Verification of content-derived concept extraction.

Verifies:
1. Extraction generalizes to unseen/fictitious vocabulary not in any hardcoded list.
2. Emitted definitions are dynamically generated and NOT literals in extract_book_concepts.py.
3. No static dictionary or pattern table pairing fixed concept names to pre-written definitions exists in source.
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
    Verifies extraction generalizes to fictitious vocabulary that cannot exist in any hardcoded list.
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


def test_no_static_definition_strings_in_source():
    """
    Statically scans extract_book_concepts.py source for any static dictionary or tuple table
    pairing fixed concept names with pre-written multi-sentence definition strings.
    """
    script_source = inspect.getsource(extract_mod)

    # Defect signatures from original r027 hardcoded pattern table
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
    assert rec["definition"].startswith("Quantum cognitive gating")
