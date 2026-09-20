"""An AI agent that asks for nothing in particular does not get the vault's attic.

Every agent-facing entry point — the MCP server, `recall_cli`, `tool_router` —
passed no lifecycle at all, so an agent's search covered 569 ARCHIVED notes and
148 notes carrying no lifecycle. Some of those are archived precisely because
their provenance did not hold up. `search()` now applies a floor for
`Principal.AI_AGENT` when the caller names no lifecycle; the owner and any
explicit request are untouched.
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
from memory_controller.controller import (  # noqa: E402
    AGENT_LIFECYCLE_FLOOR,
    Lifecycle,
    MemoryController,
)
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
from retrieval.vault_index import VaultIndex  # noqa: E402

QUERY = "ashby ultrastable homeostat"


@pytest.fixture(scope="module")
def controller():
    cwd = os.getcwd()
    os.chdir(REPO)
    try:
        index = VaultIndex.load(Path("."), include_raw=True, include_archived=True)
        storage = FileStorageEngine(".")
    finally:
        os.chdir(cwd)
    return MemoryController(storage=storage, index=index)


def lifecycles_of(pack) -> set[str]:
    return {str(r.get("lifecycle")) for r in pack.get("results", [])}


def test_the_floor_is_active_and_review():
    assert set(AGENT_LIFECYCLE_FLOOR) == {Lifecycle.ACTIVE, Lifecycle.REVIEW}


def test_an_agent_asking_for_nothing_gets_no_archived_note(controller):
    pack = controller.search(Principal.AI_AGENT, QUERY, page_size=10)
    assert pack["results"], "the query must return something, or the test proves nothing"
    assert "ARCHIVED" not in lifecycles_of(pack)
    assert pack["candidate_trace"]["agent_lifecycle_floor_applied"] is True


def test_the_owner_still_sees_everything(controller):
    pack = controller.search(Principal.HUMAN, QUERY, page_size=10)
    assert "ARCHIVED" in lifecycles_of(pack), (
        "the floor must not apply to the owner: this query's best matches are archived"
    )
    assert pack["candidate_trace"]["agent_lifecycle_floor_applied"] is False


def test_an_explicit_request_overrides_the_floor(controller):
    pack = controller.search(
        Principal.AI_AGENT, QUERY, page_size=10, lifecycles=[Lifecycle.ARCHIVED]
    )
    assert lifecycles_of(pack) <= {"ARCHIVED"}
    assert pack["candidate_trace"]["agent_lifecycle_floor_applied"] is False


def test_the_trace_records_what_was_filtered_on(controller):
    pack = controller.search(Principal.AI_AGENT, QUERY, page_size=10)
    assert set(pack["candidate_trace"]["lifecycles_requested"]) == {"ACTIVE", "REVIEW"}


def test_the_floor_does_not_empty_the_result(controller):
    """The measured cost is one case in 130; a floor that returns nothing is not a floor."""
    for query in (QUERY, "ontology slot write gate", "retrieval benchmark"):
        pack = controller.search(Principal.AI_AGENT, query, page_size=10)
        assert pack["results"], f"agent search returned nothing for {query!r}"
