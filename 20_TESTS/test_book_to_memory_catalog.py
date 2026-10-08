"""Tests for Book-to-Memory Corpus Catalog & Reversible Consolidation (Phase 8).

Validates all contracts under:
- 00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md (Section 2 & 11)
- 03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_catalog.py
"""
import pytest
from memory_controller.authorizer import Principal
from lifecycle.validation.book_to_memory_schema import (
    BookToMemoryType,
    BookToMemoryValidationError,
    SecurityInjectionError,
    ProvenanceGateError,
)
from lifecycle.validation.book_to_memory_catalog import (
    BookToMemoryCatalog,
    BookMapNotFoundError,
    DuplicateBookMapError,
    UnlinkedNoteError,
)


@pytest.fixture
def clean_catalog():
    return BookToMemoryCatalog()


@pytest.fixture
def sample_book_map_data():
    return {
        "source_identity": "kahneman-tfs-2011",
        "title": "Thinking, Fast and Slow",
        "authors": ["Daniel Kahneman"],
        "chapter_coverage": {
            "Part 1: Two Systems": ["System 1 & 2", "Attention & Effort"],
            "Part 2: Heuristics & Biases": ["Anchoring", "Availability"],
            "Part 3: Overconfidence": [],
            "Part 4: Choices": ["Prospect Theory"],
            "Part 5: Two Selves": [],
        },
        "processing_status": "in_progress",
        "edition": "1st Edition, Farrar, Straus and Giroux, 2011",
        "linked_problems": ["cognitive_load_budget", "heuristic_routing"],
        "conflicts_and_limitations": ["Ecological rationality debate with Gerd Gigerenzer"],
    }


@pytest.fixture
def sample_atomic_note():
    return {
        "id": "NOTE-kahneman-anchoring-001",
        "type": BookToMemoryType.CONCEPT.value,
        "title": "Anchoring Effect in Numerical Estimation",
        "atomic_concept": "Initial exposure to arbitrary numerical values disproportionately influences subsequent quantitative judgments.",
        "evidence": "Subjects shown wheel of fortune spin numbers gave higher estimates when spin value was high.",
        "source_title": "Thinking, Fast and Slow",
        "chapter": "Part 2: Heuristics & Biases",
        "page_range": "119-128",
        "exact_page": 121,
        "lifecycle": "VERIFIED",
        "tags": ["heuristics", "anchoring", "behavioral_economics"],
    }


# =============================================================================
# 1. Registration & Retrieval Tests
# =============================================================================

def test_catalog_register_book_map_success(clean_catalog, sample_book_map_data):
    bmap = clean_catalog.register_book_map(**sample_book_map_data)
    assert bmap["id"] == "map-kahneman-tfs-2011"
    assert bmap["type"] == BookToMemoryType.BOOK_MAP.value
    assert bmap["lifecycle"] == "VERIFIED"

    retrieved = clean_catalog.get_book_map("kahneman-tfs-2011")
    assert retrieved is not None
    assert retrieved["title"] == "Thinking, Fast and Slow"


def test_catalog_rejects_duplicate_registration(clean_catalog, sample_book_map_data):
    clean_catalog.register_book_map(**sample_book_map_data)
    with pytest.raises(DuplicateBookMapError, match="already registered"):
        clean_catalog.register_book_map(**sample_book_map_data)


def test_catalog_rejects_invalid_schema(clean_catalog):
    with pytest.raises(BookToMemoryValidationError):
        clean_catalog.register_book_map(
            source_identity="invalid-map",
            title="Book With No Authors",
            authors=[],  # empty authors list violates schema
            chapter_coverage={},
        )


def test_catalog_list_book_maps(clean_catalog, sample_book_map_data):
    clean_catalog.register_book_map(**sample_book_map_data)
    clean_catalog.register_book_map(
        source_identity="ashby-dfb-1952",
        title="Design for a Brain",
        authors=["W. Ross Ashby"],
        chapter_coverage={"Ch 1": ["Homeostasis"]},
    )
    maps = clean_catalog.list_book_maps()
    assert len(maps) == 2
    assert maps[0]["source_identity"] == "ashby-dfb-1952"
    assert maps[1]["source_identity"] == "kahneman-tfs-2011"


# =============================================================================
# 2. Atomic Note Linkage Tests
# =============================================================================

def test_catalog_link_atomic_note_success(clean_catalog, sample_book_map_data, sample_atomic_note):
    clean_catalog.register_book_map(**sample_book_map_data)
    clean_catalog.link_atomic_note("kahneman-tfs-2011", sample_atomic_note)

    linked = clean_catalog.get_linked_notes("kahneman-tfs-2011")
    assert len(linked) == 1
    assert linked[0]["id"] == "NOTE-kahneman-anchoring-001"


def test_catalog_link_atomic_note_idempotent(clean_catalog, sample_book_map_data, sample_atomic_note):
    clean_catalog.register_book_map(**sample_book_map_data)
    clean_catalog.link_atomic_note("kahneman-tfs-2011", sample_atomic_note)
    clean_catalog.link_atomic_note("kahneman-tfs-2011", sample_atomic_note)  # duplicate call

    linked = clean_catalog.get_linked_notes("kahneman-tfs-2011")
    assert len(linked) == 1


def test_catalog_link_atomic_note_rejects_unregistered_map(clean_catalog, sample_atomic_note):
    with pytest.raises(BookMapNotFoundError, match="unregistered book map"):
        clean_catalog.link_atomic_note("non-existent-book", sample_atomic_note)


def test_catalog_link_atomic_note_rejects_mismatched_provenance(clean_catalog, sample_book_map_data):
    clean_catalog.register_book_map(**sample_book_map_data)
    foreign_note = {
        "id": "NOTE-ashby-feedback-001",
        "type": BookToMemoryType.CONCEPT.value,
        "title": "Ultrastability",
        "atomic_concept": "Feedback step-mechanisms maintain variable boundaries.",
        "evidence": "Homeostat experiment verification.",
        "source_title": "Design for a Brain",  # Does NOT match Thinking, Fast and Slow
        "chapter": "Chapter 7",
        "page_range": "80-100",
        "lifecycle": "VERIFIED",
    }
    with pytest.raises(UnlinkedNoteError, match="does not match book map"):
        clean_catalog.link_atomic_note("kahneman-tfs-2011", foreign_note)


# =============================================================================
# 3. Chapter Coverage & Gap Analysis
# =============================================================================

def test_catalog_coverage_calculation(clean_catalog, sample_book_map_data, sample_atomic_note):
    clean_catalog.register_book_map(**sample_book_map_data)
    clean_catalog.link_atomic_note("kahneman-tfs-2011", sample_atomic_note)

    coverage = clean_catalog.calculate_coverage("kahneman-tfs-2011")
    assert coverage["total_chapters"] == 5
    # Covered chapters: Part 1, Part 2, Part 4 (3 out of 5)
    assert coverage["covered_chapters_count"] == 3
    assert coverage["uncovered_chapters_count"] == 2
    assert coverage["coverage_ratio"] == 0.60
    assert "Part 3: Overconfidence" in coverage["uncovered_chapters"]
    assert "Part 5: Two Selves" in coverage["uncovered_chapters"]
    assert coverage["linked_atomic_notes_count"] == 1


# =============================================================================
# 4. Security & Untrusted Input Isolation
# =============================================================================

def test_catalog_rejects_injection_in_book_map(clean_catalog, sample_book_map_data):
    malicious_data = dict(sample_book_map_data)
    malicious_data["tool_call"] = "execute_code('malicious')"
    with pytest.raises(SecurityInjectionError, match="Prohibited executable or privileged directive"):
        clean_catalog.register_book_map(**malicious_data)


def test_catalog_rejects_injection_in_linked_note(clean_catalog, sample_book_map_data, sample_atomic_note):
    clean_catalog.register_book_map(**sample_book_map_data)
    malicious_note = dict(sample_atomic_note)
    malicious_note["exec"] = "os.system('sh')"
    with pytest.raises(SecurityInjectionError):
        clean_catalog.link_atomic_note("kahneman-tfs-2011", malicious_note)


# =============================================================================
# 5. Canonical Markdown Export & Tamper-Evident Digest
# =============================================================================

def test_catalog_export_canonical_markdown(clean_catalog, sample_book_map_data, sample_atomic_note):
    clean_catalog.register_book_map(**sample_book_map_data)
    clean_catalog.link_atomic_note("kahneman-tfs-2011", sample_atomic_note)

    md = clean_catalog.export_canonical_markdown("kahneman-tfs-2011")
    assert "id: map-kahneman-tfs-2011" in md
    assert "type: book_map" in md
    assert "Thinking, Fast and Slow" in md
    assert "Metrici de Acoperire" in md
    assert "[[NOTE-kahneman-anchoring-001]]" in md
    assert "[[cognitive_load_budget]]" in md


def test_catalog_deterministic_digest_and_drift_detection(clean_catalog, sample_book_map_data, sample_atomic_note):
    clean_catalog.register_book_map(**sample_book_map_data)
    digest_1 = clean_catalog.compute_catalog_digest()
    assert len(digest_1) == 64

    # Repeated digest computation without change must be identical
    digest_2 = clean_catalog.compute_catalog_digest()
    assert digest_1 == digest_2

    # Linking a new note mutates digest
    clean_catalog.link_atomic_note("kahneman-tfs-2011", sample_atomic_note)
    digest_3 = clean_catalog.compute_catalog_digest()
    assert digest_3 != digest_1


# =============================================================================
# 6. Corpus Registration Suite: Core Monograph Cataloging
# =============================================================================

def test_catalog_registers_all_seven_core_monographs(clean_catalog):
    # 1. Kahneman 2011
    clean_catalog.register_book_map(
        source_identity="kahneman-tfs-2011",
        title="Thinking, Fast and Slow",
        authors=["Daniel Kahneman"],
        chapter_coverage={
            "Part 1: Two Systems": ["System 1 & 2"],
            "Part 2: Heuristics & Biases": ["Anchoring", "Availability"],
            "Part 4: Choices": ["Prospect Theory", "Loss Aversion"],
        },
        edition="1st Edition, Farrar, Straus and Giroux, 2011",
    )
    # 2. Ashby 1952
    clean_catalog.register_book_map(
        source_identity="ashby-dfb-1952",
        title="Design for a Brain",
        authors=["W. Ross Ashby"],
        chapter_coverage={
            "Chapter 1: The Problem": ["Homeostasis"],
            "Chapter 7: The Ultrastable System": ["Step-mechanisms"],
        },
        edition="Chapman & Hall, 1952",
    )
    # 3. Ashby 1956
    clean_catalog.register_book_map(
        source_identity="ashby-itc-1956",
        title="An Introduction to Cybernetics",
        authors=["W. Ross Ashby"],
        chapter_coverage={
            "Part 1: Mechanism": ["Transformation", "State-determined system"],
            "Part 2: Variety": ["Law of Requisite Variety"],
            "Part 3: Regulation & Control": ["Error-controlled regulator"],
        },
        edition="Chapman & Hall, 1956",
    )
    # 4. Laird 2012
    clean_catalog.register_book_map(
        source_identity="laird-soar-2012",
        title="The Soar Cognitive Architecture",
        authors=["John E. Laird"],
        chapter_coverage={
            "Chapter 2: Requirements": ["Cognitive Architectures"],
            "Chapter 3: PSCM": ["Problem-Space Computational Model"],
            "Chapter 6: Chunking": ["Impasses and Substates"],
            "Chapter 8: Semantic Memory": ["SMem Storage"],
            "Chapter 9: Episodic Memory": ["EpMem Temporal Traces"],
        },
        edition="MIT Press, 2012",
    )
    # 5. Schacter & Tulving 1994
    clean_catalog.register_book_map(
        source_identity="schacter-tulving-1994",
        title="Memory Systems 1994",
        authors=["Daniel L. Schacter", "Endel Tulving"],
        chapter_coverage={
            "Chapter 1: What Are the Memory Systems?": ["Five Major Systems: PRS, Episodic, Semantic, Procedural, Working Memory"],
            "Chapter 2: Neuropsychological Dissociations": ["Amnesia Dissociations"],
        },
        edition="MIT Press, 1994",
    )
    # 6. Squire & Kandel 2000
    clean_catalog.register_book_map(
        source_identity="squire-kandel-2000",
        title="Memory: From Mind to Molecules",
        authors=["Larry R. Squire", "Eric R. Kandel"],
        chapter_coverage={
            "Chapter 4: The Hippocampus and Declarative Memory": ["Medial Temporal Lobe"],
            "Chapter 6: Synaptic Plasticity and Long-Term Storage": ["Cellular Mechanisms"],
        },
        edition="Scientific American Library, 2000",
    )
    # 7. Kandel 2001
    clean_catalog.register_book_map(
        source_identity="kandel-2001",
        title="The Molecular Biology of Memory Storage",
        authors=["Eric R. Kandel"],
        chapter_coverage={
            "Section: Short-term vs Long-term Facilitation": ["CREB-mediated gene expression"],
            "Section: Synaptic Tagging": ["Synaptic capture hypothesis"],
        },
        edition="Nobel Lecture / Science, 2001",
    )

    maps = clean_catalog.list_book_maps()
    assert len(maps) == 7
    digest = clean_catalog.compute_catalog_digest()
    assert len(digest) == 64
