"""`reasoning` and `executive` annotate the trace; they do not change retrieval.

Both were reported as evaluated on benchmark v3 and rejected for not winning —
0 wins, 0 losses, p = 1.0. That outcome was settled before the run: neither
hook touches `results`, so neither could have won or lost a single case. These
tests pin the contract as it is, so that a future claim of "the executive
improves retrieval" has to start by making the output consumed, and so nobody
re-runs that measurement believing it tests anything.

`working_memory` is the counter-example kept here on purpose: it does change
the output, which is how a real wiring behaves.
"""
from __future__ import annotations

import os
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "x" * 40)

from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller.controller import MemoryController  # noqa: E402
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
from retrieval.vault_index import VaultIndex  # noqa: E402

QUERIES = ("ontology slot write gate", "retrieval benchmark", "graph expansion budget")


@pytest.fixture(scope="module")
def parts():
    cwd = os.getcwd()
    os.chdir(REPO)
    try:
        index = VaultIndex.load(Path("."), include_raw=True, include_archived=True)
        storage = FileStorageEngine(".")
    finally:
        os.chdir(cwd)
    return storage, index


def ids_for(parts, **flags) -> list[list[str]]:
    storage, index = parts
    controller = MemoryController(storage=storage, index=index, **flags)
    return [
        [r.get("id") for r in controller.search(Principal.HUMAN, q, page_size=5).get("results", [])]
        for q in QUERIES
    ]


@pytest.mark.parametrize("flag", ["enable_reasoning", "enable_executive"])
def test_an_annotation_only_module_returns_exactly_the_baseline(parts, flag):
    assert ids_for(parts, **{flag: True}) == ids_for(parts)


@pytest.mark.parametrize("flag, marker", [
    ("enable_reasoning", "reasoning_is_annotation_only"),
    ("enable_executive", "executive_is_annotation_only"),
])
def test_the_trace_says_so_out_loud(parts, flag, marker):
    storage, index = parts
    controller = MemoryController(storage=storage, index=index, **{flag: True})
    trace = controller.search(Principal.HUMAN, QUERIES[0], page_size=5)["candidate_trace"]
    assert trace.get(marker) is True


def test_working_memory_is_the_counter_example(parts):
    """A module that really is wired changes something; this one does, for the worse."""
    storage, index = parts
    controller = MemoryController(storage=storage, index=index, enable_working_memory=True)
    trace = controller.search(Principal.HUMAN, QUERIES[0], page_size=5)["candidate_trace"]
    assert trace.get("working_memory_active_count") is not None
    assert "working_memory_is_annotation_only" not in trace
