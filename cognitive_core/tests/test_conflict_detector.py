import pytest
from cognitive_core.conflict_detector import ConflictDetector

def test_pairwise_comparisons_halved_on_n50_fixture():
    notes = [
        {
            "id": f"note_{i}",
            "lifecycle": "ACTIVE",
            "category": "arch",
            "content": f"knowledge base architectural pattern note number {i}",
        }
        for i in range(50)
    ]
    detector = ConflictDetector(max_notes=2000)
    detector.detect_pairs(notes)

    # N=50 notes in same category:
    # Legacy ordered pairs: 50 * 49 = 2450 comparisons
    # Optimized deduplicated pairs: 50 * 49 // 2 = 1225 comparisons
    assert detector.comparisons_count == 1225


def test_max_notes_cap_raises_explicit_error():
    detector = ConflictDetector(max_notes=20)
    notes = [
        {"id": f"n_{i}", "lifecycle": "ACTIVE", "category": "cat", "content": f"test note {i}"}
        for i in range(25)
    ]

    with pytest.raises(ValueError, match="exceeds max_notes limit"):
        detector.detect_pairs(notes)

    with pytest.raises(ValueError, match="exceeds max_notes limit"):
        detector.detect(notes[0], notes[1:])


def test_detect_pairs_finds_contradictions_and_overlaps():
    notes = [
        {"id": "a", "lifecycle": "ACTIVE", "category": "db", "content": "folosim postgresql pentru storage"},
        {"id": "b", "lifecycle": "ACTIVE", "category": "db", "content": "nu folosim postgresql pentru storage"},
        {"id": "c", "lifecycle": "ACTIVE", "category": "db", "content": "folosim postgresql pentru analytics"},
    ]
    detector = ConflictDetector(overlap_threshold=0.3)
    pairs = detector.detect_pairs(notes)

    assert len(pairs) >= 1
    contradiction_pair = next(p for p in pairs if p["severity"] == "contradiction")
    assert contradiction_pair["note_a"] == "a"
    assert contradiction_pair["note_b"] == "b"