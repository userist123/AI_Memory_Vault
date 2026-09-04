from memory_controller.evidence_bundle import build_evidence_bundle
from memory_controller.evidence_verifier import verify_evidence_bundle


def _note(note_id, content="alpha"):
    return {
        "id": note_id,
        "content": content,
        "lifecycle": "ACTIVE",
        "verification": "verified",
        "provenance": {"source_type": "official", "source_ref": "test"},
    }


def test_evidence_bundle_verifies_when_note_is_unchanged():
    note = _note("m1")
    bundle = build_evidence_bundle([note], conflict_case_id="CR-1")
    result = verify_evidence_bundle(bundle, [note])
    assert result.valid
    assert result.bundle_hash_matches
    assert result.bundle_hash == bundle["bundle_hash"]
    assert result.stale_memory_ids == ()
    assert result.missing_memory_ids == ()


def test_evidence_bundle_detects_stale_note():
    note = _note("m1")
    bundle = build_evidence_bundle([note], conflict_case_id="CR-1")
    changed = _note("m1", content="changed")
    result = verify_evidence_bundle(bundle, [changed])
    assert not result.valid
    assert result.stale_memory_ids == ("m1",)
    assert result.bundle_hash == bundle["bundle_hash"]


def test_evidence_bundle_detects_missing_note():
    note = _note("m1")
    bundle = build_evidence_bundle([note], conflict_case_id="CR-1")
    result = verify_evidence_bundle(bundle, [])
    assert not result.valid
    assert result.missing_memory_ids == ("m1",)


def test_tampered_bundle_fails_hash_verification():
    note = _note("m1")
    bundle = build_evidence_bundle([note], conflict_case_id="CR-1")
    tampered = dict(bundle)
    tampered["items"] = [dict(bundle["items"][0], verification="unverified")]
    result = verify_evidence_bundle(tampered, [note])
    assert not result.valid
    assert not result.bundle_hash_matches
