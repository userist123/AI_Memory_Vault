"""The audit sample is frozen before anyone labels it, and it describes the live graph.

The previous 50-edge audit sampled the proposer's output — rows whose origin is
`proposed` — which was never promoted into the graph. This sample is drawn from
the relations a traversal actually follows. These tests keep it honest: the
bytes cannot change without the hash failing, the sample has to come from the
declared typed population, and each note in it has to carry enough text for a
judgement to be possible.
"""
from __future__ import annotations

import collections
import hashlib
import json
from pathlib import Path

import pytest

DIR = Path(__file__).resolve().parents[1] / "07_EVALUATION" / "edge_audit_v2"
SAMPLE = DIR / "audit_sample_declared_50.json"
RECORDED = DIR / "audit_sample_declared_50.json.sha256"


@pytest.fixture(scope="module")
def payload() -> dict:
    return json.loads(SAMPLE.read_text(encoding="utf-8"))


def test_the_sample_bytes_match_the_recorded_hash():
    text = SAMPLE.read_text(encoding="utf-8").replace("\r\n", "\n")
    digest = hashlib.sha256(text.encode("utf-8")).hexdigest()
    assert digest == RECORDED.read_text(encoding="utf-8").split()[0]


def test_the_sample_is_fifty_rows_drawn_with_a_fixed_seed(payload):
    assert payload["seed"] == 42
    assert payload["sample_size"] == len(payload["samples"]) == 50


def test_no_row_is_an_untyped_relation(payload):
    """`related_to` is the untyped fallback; auditing it would answer nothing."""
    assert all(row["relation"] != "related_to" for row in payload["samples"])


def test_every_relation_type_in_the_population_is_represented(payload):
    counts = collections.Counter(row["relation"] for row in payload["samples"])
    assert set(counts) == {"part_of", "depends_on", "applies_to", "caused"}
    assert counts["part_of"] > counts["applies_to"], "stratification is proportional"


def test_every_row_carries_what_a_judgement_needs(payload):
    for row in payload["samples"]:
        for role in ("source", "target"):
            assert row[f"{role}_title"], f"row {row['index']} has no {role} title"
            assert row[f"{role}_excerpt"], f"row {row['index']} has no {role} text"
            assert row[f"{role}_lifecycle"], f"row {row['index']} has no {role} lifecycle"


def test_the_rows_are_distinct_pairs(payload):
    pairs = [(row["source_id"], row["target_id"], row["relation"]) for row in payload["samples"]]
    assert len(set(pairs)) == len(pairs)


def test_tampering_with_one_byte_would_be_caught():
    text = SAMPLE.read_text(encoding="utf-8").replace("\r\n", "\n")
    tampered = text.replace("ACCEPT", "accept", 1) if "ACCEPT" in text else text + " "
    assert hashlib.sha256(tampered.encode("utf-8")).hexdigest() != RECORDED.read_text(encoding="utf-8").split()[0]


def test_no_verdict_is_shipped_with_the_sample(payload):
    """The sample goes to the evaluator unlabelled, or the audit proves nothing."""
    assert all("verdict" not in row for row in payload["samples"])
