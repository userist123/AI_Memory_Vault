"""Tests for the blind-rating packet generator and its CLI (PR #209 B05).

Everything here is a synthetic fixture exercising the mechanics; none of it is rating evidence.
"""
import importlib.util
import json
from pathlib import Path

import pytest

from lifecycle.validation.book_to_memory_blind_packet import (
    BlindPacketError,
    assert_blind,
    build_blind_packet,
    scores_by_trial,
    unblinding_risk,
)
from lifecycle.validation.book_to_memory_raters import aggregate_ratings, load_ratings

REPO = Path(__file__).resolve().parents[1]
_SPEC = importlib.util.spec_from_file_location("b2m_blind_rating_packet", REPO / "30_SCRIPTS" / "evaluation" / "b2m_blind_rating_packet.py")
CLI = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(CLI)


def trials(n=12):
    out = []
    for i in range(1, n + 1):
        for cond in ("WITH_NOTE", "WITHOUT_NOTE"):
            out.append({"trial_id": f"B03-T{i:02d}-{cond}", "task_id": f"T{i:02d}", "condition": cond,
                        "question": f"Explain term number {i}.", "answer": f"Synthetic answer {i} for {cond.lower()}.",
                        "reference": f"Reference {i}"})
    return out


@pytest.fixture
def built(tmp_path):
    out, key = tmp_path / "packet", tmp_path / "key" / "key.json"
    manifest = build_blind_packet(trials(), out, key, n_raters=3, seed=11)
    return out, key, manifest


def test_packet_shows_no_condition_or_trial_id(built):
    out, _, _ = built
    items = json.loads((out / "items.json").read_text(encoding="utf-8"))["items"]
    assert len(items) == 24
    blob = (out / "items.json").read_text(encoding="utf-8") + (out / "RATER_INSTRUCTIONS.md").read_text(encoding="utf-8")
    for token in ("WITH_NOTE", "WITHOUT_NOTE", "B03-T", "trial_id", "condition"):
        assert token not in blob
    for f in (out / "rating_templates").iterdir():
        assert "B03-T" not in f.read_text(encoding="utf-8") and "WITH" not in f.read_text(encoding="utf-8")
    assert all(set(i) <= {"item_id", "question", "answer", "reference"} for i in items)


def test_items_are_shuffled_and_the_order_is_seeded(tmp_path):
    t = trials()
    a = build_blind_packet(t, tmp_path / "a", tmp_path / "ka.json", seed=1)
    b = build_blind_packet(t, tmp_path / "b", tmp_path / "kb.json", seed=1)
    c = build_blind_packet(t, tmp_path / "c", tmp_path / "kc.json", seed=2)
    ids = lambda d: [i["item_id"] for i in json.loads((tmp_path / d / "items.json").read_text(encoding="utf-8"))["items"]]
    assert ids("a") == ids("b") and a["items_sha256"] == b["items_sha256"]
    assert ids("a") != ids("c")
    # Shuffled: the packet order is not the input order and does not keep pairs adjacent in input order.
    key = json.loads((tmp_path / "ka.json").read_text(encoding="utf-8"))["key"]
    order = [key[i]["trial_id"] for i in ids("a")]
    assert order != [x["trial_id"] for x in t]
    assert order != sorted(order)


def test_key_maps_every_item_back_and_lives_outside_the_packet(built):
    out, key_path, manifest = built
    key = json.loads(key_path.read_text(encoding="utf-8"))["key"]
    items = json.loads((out / "items.json").read_text(encoding="utf-8"))["items"]
    assert {i["item_id"] for i in items} == set(key)
    assert {v["condition"] for v in key.values()} == {"WITH_NOTE", "WITHOUT_NOTE"}
    assert not (out / "key.json").exists()
    assert manifest["items"] == 24 and manifest["raters"] == 3
    assert len(list((out / "rating_templates").iterdir())) == 3


def test_key_inside_the_packet_is_refused(tmp_path):
    with pytest.raises(BlindPacketError, match="outside"):
        build_blind_packet(trials(), tmp_path / "p", tmp_path / "p" / "key.json")


def test_one_rater_packet_is_refused(tmp_path):
    with pytest.raises(BlindPacketError):
        build_blind_packet(trials(), tmp_path / "p", tmp_path / "k.json", n_raters=1)


def test_empty_answers_are_not_packaged_and_are_listed(tmp_path):
    t = trials(3)
    t[0]["answer"] = "   "
    m = build_blind_packet(t, tmp_path / "p", tmp_path / "k.json")
    assert m["items"] == 5 and m["missing_answers"] == [t[0]["trial_id"]]


def test_duplicate_trial_ids_are_refused(tmp_path):
    t = trials(2)
    t.append(dict(t[0]))
    with pytest.raises(BlindPacketError, match="duplicate"):
        build_blind_packet(t, tmp_path / "p", tmp_path / "k.json")


def test_assert_blind_catches_leaks():
    ok = {"item_id": "ITM-1", "question": "q", "answer": "a"}
    assert_blind([ok])
    for field, value in (("answer", "the run WITH_NOTE said"), ("question", "B03-T01 term"), ("reference", "WITHOUT_NOTE")):
        with pytest.raises(BlindPacketError):
            assert_blind([{**ok, field: value}])
    with pytest.raises(BlindPacketError):
        assert_blind([{**ok, "condition": "x"}])


def test_unblinding_risk_flags_answers_that_mention_the_material(tmp_path):
    assert unblinding_risk("According to the reference material, X is Y.")
    assert unblinding_risk("The note says that X is Y.")
    assert not unblinding_risk("X is a mechanism that does Y.")
    t = trials(2)
    t[0]["answer"] = "According to the provided passage, it is a thing."
    m = build_blind_packet(t, tmp_path / "p", tmp_path / "k.json")
    assert m["items_with_unblinding_risk"] == 1
    key = json.loads((tmp_path / "k.json").read_text(encoding="utf-8"))["key"]
    assert sum(bool(v["unblinding_risk"]) for v in key.values()) == 1


def test_ratings_round_trip_to_trial_scores(built):
    out, key_path, _ = built
    items = json.loads((out / "items.json").read_text(encoding="utf-8"))["items"]
    key = json.loads(key_path.read_text(encoding="utf-8"))["key"]
    lines = []
    for rater, kind, model in (("R1", "human", None), ("R2", "model", "model-x")):
        for it in items:
            base = 8 if key[it["item_id"]]["condition"] == "WITH_NOTE" else 5
            lines.append(json.dumps({"schema_version": "b2m-rating-record/1", "rater_id": rater, "rater_kind": kind,
                                     "model": model, "blind": True, "item_id": it["item_id"],
                                     "label": base + (1 if rater == "R2" else 0), "rationale": "synthetic"}))
    agg = aggregate_ratings(load_ratings(lines))
    scores = scores_by_trial(agg, key_path)
    assert len(scores) == 24
    assert scores["B03-T01-WITH_NOTE"] == pytest.approx(8.5) and scores["B03-T01-WITHOUT_NOTE"] == pytest.approx(5.5)


def test_scores_refuse_an_unusable_aggregate(built):
    _, key_path, _ = built
    with pytest.raises(BlindPacketError):
        scores_by_trial({"status": "INSUFFICIENT_RATERS", "reason": "1 source"}, key_path)


# ---------------------------------------------------------------- the CLI
def test_cli_build_and_score(tmp_path, capsys):
    items_file = tmp_path / "items.json"
    items_file.write_text(json.dumps(trials(4)), encoding="utf-8")
    out, key = tmp_path / "packet", tmp_path / "key.json"
    assert CLI.main(["build", "--items-json", str(items_file), "--out", str(out), "--key", str(key), "--raters", "2", "--seed", "5"]) == 0
    items = json.loads((out / "items.json").read_text(encoding="utf-8"))["items"]
    files = []
    for rater, kind, model in (("R1", "human", None), ("R2", "model", "model-x")):
        f = tmp_path / f"{rater}.jsonl"
        f.write_text("\n".join(json.dumps({"schema_version": "b2m-rating-record/1", "rater_id": rater, "rater_kind": kind,
                                           "model": model, "blind": True, "item_id": it["item_id"], "label": 7,
                                           "rationale": "synthetic"}) for it in items), encoding="utf-8")
        files.append(str(f))
    scores = tmp_path / "scores.json"
    assert CLI.main(["score", "--ratings", *files, "--key", str(key), "--out", str(scores)]) == 0
    doc = json.loads(scores.read_text(encoding="utf-8"))
    assert len(doc["scores_by_trial"]) == 8


def test_cli_score_with_one_rater_exits_2_and_aggregates_nothing(tmp_path):
    items_file = tmp_path / "items.json"
    items_file.write_text(json.dumps(trials(3)), encoding="utf-8")
    out, key = tmp_path / "packet", tmp_path / "key.json"
    CLI.main(["build", "--items-json", str(items_file), "--out", str(out), "--key", str(key)])
    items = json.loads((out / "items.json").read_text(encoding="utf-8"))["items"]
    f = tmp_path / "R1.jsonl"
    f.write_text("\n".join(json.dumps({"schema_version": "b2m-rating-record/1", "rater_id": "R1", "rater_kind": "human",
                                       "model": None, "blind": True, "item_id": it["item_id"], "label": 6,
                                       "rationale": "synthetic"}) for it in items), encoding="utf-8")
    scores = tmp_path / "s.json"
    assert CLI.main(["score", "--ratings", str(f), "--key", str(key), "--out", str(scores)]) == 2
    assert "scores_by_trial" not in json.loads(scores.read_text(encoding="utf-8"))


def test_cli_build_needs_exactly_one_source(tmp_path):
    with pytest.raises(SystemExit):
        CLI.main(["build", "--out", str(tmp_path / "p"), "--key", str(tmp_path / "k.json")])
