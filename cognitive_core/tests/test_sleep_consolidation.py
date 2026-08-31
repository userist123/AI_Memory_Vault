from datetime import datetime, timedelta, timezone

from cognitive_core.sleep_consolidation import SleepConsolidator


class _FakeStorage:
    def __init__(self, notes):
        self.store = {n["id"]: n for n in notes}


class _FakeController:
    def __init__(self, notes):
        self.storage = _FakeStorage(notes)


def _iso_days_ago(days):
    return (datetime.now(timezone.utc) - timedelta(days=days)).date().isoformat()


def test_dormant_candidates_detected_by_age():
    notes = [
        {"id": "old", "lifecycle": "ACTIVE", "category": "x", "content": "foo", "updated": _iso_days_ago(120)},
        {"id": "fresh", "lifecycle": "ACTIVE", "category": "x", "content": "bar", "updated": _iso_days_ago(2)},
    ]
    controller = _FakeController(notes)
    consolidator = SleepConsolidator(controller, dormant_days=60)
    report = consolidator.run()
    dormant_ids = {item["id"] for item in report.dormant_candidates}
    assert "old" in dormant_ids
    assert "fresh" not in dormant_ids


def test_stale_review_candidates_detected():
    notes = [
        {"id": "review1", "lifecycle": "REVIEW", "category": "x", "content": "foo", "updated": _iso_days_ago(30)},
    ]
    controller = _FakeController(notes)
    consolidator = SleepConsolidator(controller, stale_review_days=14)
    report = consolidator.run()
    assert any(item["id"] == "review1" for item in report.stale_review_candidates)


def test_conflict_pairs_detected_between_active_notes():
    notes = [
        {"id": "a", "lifecycle": "ACTIVE", "category": "architecture",
         "content": "folosim SQLite WAL pentru index", "updated": _iso_days_ago(1)},
        {"id": "b", "lifecycle": "ACTIVE", "category": "architecture",
         "content": "nu folosim SQLite WAL pentru index", "updated": _iso_days_ago(1)},
    ]
    controller = _FakeController(notes)
    consolidator = SleepConsolidator(controller)
    report = consolidator.run()
    assert report.conflict_pairs
    assert report.conflict_pairs[0]["severity"] in {"contradiction", "overlap"}


def test_report_never_mutates_storage():
    notes = [{"id": "a", "lifecycle": "ACTIVE", "category": "x", "content": "foo", "updated": _iso_days_ago(1)}]
    controller = _FakeController(notes)
    snapshot_before = dict(controller.storage.store["a"])
    SleepConsolidator(controller).run()
    assert controller.storage.store["a"] == snapshot_before


def test_budget_cap_stops_run_and_reports_counts():
    notes = [
        {"id": f"note_{i}", "lifecycle": "ACTIVE", "category": "test", "content": f"content {i}", "updated": _iso_days_ago(i + 1)}
        for i in range(20)
    ]
    controller = _FakeController(notes)
    consolidator = SleepConsolidator(controller, max_items_per_run=6)
    report = consolidator.run()

    assert report.stats["total_notes"] == 20
    assert report.stats["eligible_notes"] == 20
    assert report.stats["processed_notes"] == 6
    assert report.stats["budget_cap"] == 6
    assert report.stats["budget_exhausted"] is True


def test_budget_prioritizes_oldest_notes():
    notes = [
        {"id": "recent", "lifecycle": "ACTIVE", "category": "c", "content": "recent content", "updated": _iso_days_ago(2)},
        {"id": "ancient", "lifecycle": "ACTIVE", "category": "c", "content": "ancient content", "updated": _iso_days_ago(200)},
        {"id": "mid", "lifecycle": "ACTIVE", "category": "c", "content": "mid content", "updated": _iso_days_ago(50)},
    ]
    controller = _FakeController(notes)
    # Budget of 1 should pick "ancient"
    consolidator = SleepConsolidator(controller, max_items_per_run=1, dormant_days=60)
    report = consolidator.run()

    assert report.stats["processed_notes"] == 1
    assert any(item["id"] == "ancient" for item in report.dormant_candidates)
    assert not any(item["id"] in {"recent", "mid"} for item in report.dormant_candidates)

