from memory_controller.semantic_conflict import detect_pair


def _note(note_id, content, *, technology="Windows Server", category="support", source_ref="source"):
    return {
        "id": note_id,
        "content": content,
        "technology": technology,
        "category": category,
        "provenance": {"source_type": "official", "source_ref": source_ref},
        "valid_from": "2020-01-01",
        "valid_until": "2025-12-31",
    }


def test_detects_opposite_assertions_from_different_sources():
    left = _note("a", "Windows Server 2012 is supported.", source_ref="doc-a")
    right = _note("b", "Windows Server 2012 is not supported.", source_ref="doc-b")

    conflict = detect_pair(left, right)

    assert conflict is not None
    assert conflict.status == "potential_conflict"
    assert conflict.left_id == "a"
    assert conflict.right_id == "b"
    assert "opposing assertion polarity" in conflict.reasons


def test_different_subjects_do_not_conflict():
    left = _note("a", "Windows Server 2012 is supported.")
    right = _note("b", "Ubuntu Server 24.04 is not supported.", technology="Ubuntu Server")

    assert detect_pair(left, right) is None
