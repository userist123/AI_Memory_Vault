"""memory_access: search / get / propose on top of MemoryController, in a temporary vault.

Covers what the issue asked for at the controller boundary: search runs as AI_AGENT with the
production defaults, propose creates an unverified REVIEW candidate in the content tree, a
proposed note is found by search in the same session, and nothing can write an ontology slot.
"""
from __future__ import annotations

import importlib.util
import os
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

_spec = importlib.util.spec_from_file_location("memory_vault_fixture", REPO / "20_TESTS" / "memory_vault_fixture.py")
fx = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(fx)

from interfaces import memory_access as ma  # noqa: E402
from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller.controller import MemoryController  # noqa: E402
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402


@pytest.fixture
def world(tmp_path, monkeypatch):
    monkeypatch.setenv("ANTIGRAVITY_ARTIFACT_DIR", str(tmp_path / "artifacts"))
    monkeypatch.setenv("ANTIGRAVITY_TELEMETRY_DIR", str(tmp_path / "telemetry"))
    vault = fx.make_vault(tmp_path)
    return MemoryController(FileStorageEngine(str(vault))), vault


def test_search_finds_a_seed_note_with_id_title_path_snippet_and_score(world):
    controller, vault = world
    out = ma.search(controller, "consolidarea nocturna instaleaza dependentele", limit=3)
    assert out["count"] >= 1 and ma.NOTICE in out["notice"]
    top = out["query_results"][0]
    assert top["title"] == "consolidarea nocturna"
    assert top["path"].startswith("01_ARCHITECTURE/knowledge/") and (vault / top["path"]).exists()
    assert "dependentele" in top["snippet"]
    assert top["id"] and top["lifecycle"] == "ACTIVE"


def test_search_runs_as_ai_agent_with_production_defaults(world, monkeypatch):
    controller, _ = world
    seen = {}
    original = controller.search

    def spy(principal, query, **kwargs):
        seen.update(principal=principal, kwargs=kwargs)
        return original(principal, query, **kwargs)

    monkeypatch.setattr(controller, "search", spy)
    ma.search(controller, "bugetul grafului", limit=2)
    assert seen["principal"] is Principal.AI_AGENT
    # only the page size is passed: no graph, spreading activation, ranking arm or budget override
    assert set(seen["kwargs"]) == {"page_size"}


def test_search_limit_is_bounded_and_empty_query_refused(world):
    controller, _ = world
    assert ma.search(controller, "consolidarea", limit=10_000)["count"] <= ma.MAX_LIMIT
    with pytest.raises(ValueError):
        ma.search(controller, "   ")


def test_propose_creates_an_unverified_review_candidate_in_the_content_tree(world):
    controller, vault = world
    result = ma.propose(controller, "Cum am rezolvat o eroare de test", "Am adaugat pasul de instalare a dependentelor.",
                        client="pytest")
    assert result["lifecycle"] == "REVIEW" and result["verification"] == "unverified"
    assert result["path"].startswith("01_ARCHITECTURE/knowledge/") and (vault / result["path"]).exists()
    assert not (vault / "01_KNOWLEDGE").exists()
    stored = controller.storage.get(result["id"])
    assert stored["lifecycle"] == "REVIEW" and stored["verification"] == "unverified"
    assert stored["provenance"]["source_type"] in ma.PROPOSAL_SOURCE_TYPES
    assert "candidate" in stored["tags"]


def test_a_proposed_note_is_found_by_search_and_readable_in_the_same_session(world):
    controller, _ = world
    proposed = ma.propose(controller, "Reindexare dupa propunere", "Notele propuse apar imediat in cautare, fara copiere manuala.")
    found = ma.search(controller, "reindexare dupa propunere notele propuse apar imediat", limit=3)
    assert proposed["id"] in [r["id"] for r in found["query_results"]]
    row = next(r for r in found["query_results"] if r["id"] == proposed["id"])
    assert row["lifecycle"] == "REVIEW" and row["verification"] == "unverified"
    got = ma.get(controller, proposed["id"])
    assert got["lifecycle"] == "REVIEW" and got["unverified"] is True
    assert "Notele propuse apar imediat" in got["content"]


def test_nothing_a_proposal_can_do_verifies_it(world):
    controller, _ = world
    with pytest.raises(ma.ProposalRefused):
        ma.propose(controller, "t", "b", provenance={"source_type": "official"})
    with pytest.raises(ma.ProposalRefused):
        ma.propose(controller, "t", "b", provenance={"source_type": "user"})
    with pytest.raises(ma.ProposalRefused):
        ma.propose(controller, "t", "b", provenance={"verification": "verified"})
    result = ma.propose(controller, "titlu valid", "corp valid")
    assert controller.storage.get(result["id"])["verification"] != "verified"
    with pytest.raises(Exception):
        controller.attest(Principal.AI_AGENT, result["id"], "because", "ref")


@pytest.mark.parametrize("note_type", ["ontology_definition", "slot", "core", ""])
def test_a_proposal_cannot_be_typed_as_an_ontology_slot_or_core_note(world, note_type):
    controller, _ = world
    with pytest.raises(ma.ProposalRefused):
        ma.propose(controller, "titlu", "corp", note_type=note_type)


def test_a_proposal_is_never_written_into_the_slots_directory(world, monkeypatch):
    """Even if the path resolver were pointed at the slots, the proposal is refused before any write."""
    controller, vault = world
    slots = vault / "01_ARCHITECTURE" / "ontology" / "slots"
    before = {p.name: p.read_bytes() for p in slots.iterdir()}
    monkeypatch.setattr(controller.storage, "_target_path_for", lambda note_id, note: str(slots / "99_intruder.md"))
    with pytest.raises(ma.ProposalRefused, match="ontology slots"):
        ma.propose(controller, "titlu", "corp")
    assert {p.name: p.read_bytes() for p in slots.iterdir()} == before


def test_negative_control_without_the_guard_the_same_redirect_would_write_into_the_slots(world, monkeypatch):
    """The refusal above comes from the guard: the controller itself would have written the file."""
    controller, vault = world
    slots = vault / "01_ARCHITECTURE" / "ontology" / "slots"
    monkeypatch.setattr(controller.storage, "_target_path_for", lambda note_id, note: str(slots / "99_intruder.md"))
    monkeypatch.setattr(ma, "_assert_outside_slots", lambda *a, **k: None)
    ma.propose(controller, "titlu", "corp")
    assert (slots / "99_intruder.md").exists()


def test_input_limits(world):
    controller, _ = world
    with pytest.raises(ma.ProposalRefused):
        ma.propose(controller, "x" * (ma.MAX_TITLE + 1), "corp")
    with pytest.raises(ma.ProposalRefused):
        ma.propose(controller, "titlu", "y" * (ma.MAX_BODY + 1))
    with pytest.raises(ma.ProposalRefused):
        ma.propose(controller, "", "corp")


def test_get_of_an_unreadable_or_unknown_note_is_an_error(world):
    controller, _ = world
    with pytest.raises(Exception):
        ma.get(controller, "does-not-exist")
