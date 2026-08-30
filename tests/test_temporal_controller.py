import os

from datetime import date

from memory_controller.authorizer import Principal
from memory_controller.temporal_controller import TemporalMemoryController, matches_temporal


def _note(note_id, *, lifecycle="ACTIVE", valid_from=None, valid_until=None, extraction=None, superseded_by=None, conflicts_with=None):
    note = {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": lifecycle,
        "category": "test",
        "tags": [],
        "created": "2020-01-01",
        "updated": "2020-01-01",
        "provenance": {"source_type": "official", "source_ref": "test", **({"extraction_date": extraction} if extraction else {})},
        "confidence": "high",
        "verification": "verified",
        "relations": [],
        "content": "temporal fact",
    }
    if valid_from:
        note["valid_from"] = valid_from
    if valid_until:
        note["valid_until"] = valid_until
    if superseded_by:
        note["superseded_by"] = superseded_by
    if conflicts_with:
        note["conflicts_with"] = conflicts_with
    return note


class FakeController:
    def __init__(self, notes):
        self.notes = {note["id"]: dict(note) for note in notes}
        self.search_calls = 0
        self.cognitive_reads = []

    def search(self, principal, query, **kwargs):
        self.search_calls += 1
        return {"results": list(self.notes.values()), "next_page_token": None}

    def cognitive_read(self, principal, note_id):
        self.cognitive_reads.append(note_id)
        note = self.notes.get(note_id)
        return {"results": [dict(note)]} if note else {"results": []}


def test_temporal_match_uses_validity_and_knowledge_time():
    note = _note("a", valid_from="2020-01-01", valid_until="2023-12-31", extraction="2021-01-01")
    assert matches_temporal(note, as_of=date(2022, 1, 1), known_as_of=date(2022, 6, 1))
    assert not matches_temporal(note, as_of=date(2024, 1, 1), known_as_of=date(2022, 6, 1))
    assert not matches_temporal(note, as_of=date(2022, 1, 1), known_as_of=date(2020, 12, 31))


def test_temporal_wrapper_preserves_legacy_search_without_dates():
    controller = FakeController([_note("a")])
    temporal = TemporalMemoryController(controller)
    result = temporal.search(Principal.AI_AGENT, "hello", page_size=5)
    assert result["results"][0]["id"] == "a"
    assert controller.search_calls == 1


def test_temporal_lineage_adds_successor_only_when_valid_at_snapshot():
    old = _note("old", lifecycle="SUPERSEDED", valid_from="2020-01-01", valid_until="2022-12-31", extraction="2020-02-01", superseded_by="new")
    new = _note("new", lifecycle="ACTIVE", valid_from="2023-01-01", extraction="2023-01-02")
    controller = FakeController([old])
    controller.notes["new"] = new
    temporal = TemporalMemoryController(controller)
    historical = temporal.search(Principal.AI_AGENT, "hello", page_size=10, as_of="2022-06-01")
    assert [item["id"] for item in historical["results"]] == ["old"]
    assert controller.cognitive_reads == ["new"]


def test_temporal_lineage_adds_successor_when_both_versions_are_valid():
    old = _note("old", lifecycle="SUPERSEDED", valid_from="2020-01-01", valid_until="2024-12-31", extraction="2020-02-01", superseded_by="new")
    new = _note("new", lifecycle="ACTIVE", valid_from="2023-01-01", extraction="2023-01-02")
    controller = FakeController([old, new])
    temporal = TemporalMemoryController(controller)
    snapshot = temporal.search(Principal.AI_AGENT, "hello", page_size=10, as_of="2023-06-01")
    ids = [item["id"] for item in snapshot["results"]]
    assert "new" in ids
    assert any(item.get("_temporal_lineage_from") == "old" for item in snapshot["results"] if item["id"] == "new")


def test_temporal_pagination_signs_and_binds_query(monkeypatch):
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "test-secret")
    notes = [_note("a", valid_from="2020-01-01"), _note("b", valid_from="2021-01-01")]
    temporal = TemporalMemoryController(FakeController(notes))
    first = temporal.search(Principal.AI_AGENT, "hello", page_size=1, as_of="2022-01-01")
    token = first["next_page_token"]
    assert token
    second = temporal.search(Principal.AI_AGENT, "hello", page_size=1, page_token=token, as_of="2022-01-01")
    assert second["results"][0]["id"] != first["results"][0]["id"]
    try:
        temporal.search(Principal.AI_AGENT, "hello", page_size=1, page_token=token, as_of="2023-01-01")
    except Exception as exc:
        assert "temporal query does not match" in str(exc)
    else:
        raise AssertionError("expected temporal cursor binding failure")


def test_temporal_conflict_is_reported_without_dropping_either_fact():
    left = _note("left", valid_from="2020-01-01", conflicts_with="right")
    right = _note("right", valid_from="2020-01-01", conflicts_with="left")
    temporal = TemporalMemoryController(FakeController([left, right]))
    result = temporal.search(Principal.AI_AGENT, "hello", page_size=10, as_of="2022-01-01")
    assert {item["id"] for item in result["results"]} == {"left", "right"}
    assert result["temporal"]["conflicts"] == [{"left_id": "left", "right_id": "right", "reason": "explicit conflicts_with"}]
