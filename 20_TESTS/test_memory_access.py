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
    # The order among three seed notes is not stable across platforms (ties broken by random ids),
    # so the check is on the note being returned with the right fields, not on its rank.
    top = next(r for r in out["query_results"] if r["title"] == "consolidarea nocturna")
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

    # memory_get is part of the same contract: the agent reads its own REVIEW proposal,
    # explicitly marked unverified (CLAUDE.md: "REVIEW e marcata neverificata").
    got = ma.get(controller, proposed["id"])
    assert got["lifecycle"] == "REVIEW" and got["unverified"] is True and got["verification"] == "unverified"
    assert "Notele propuse apar imediat" in got["content"]
    assert ma.NOTICE in got["notice"]


@pytest.fixture
def distribution(tmp_path, monkeypatch):
    """A vault holding one note per state of the real vault (ACTIVE/REVIEW, verified or not)."""
    monkeypatch.setenv("ANTIGRAVITY_ARTIFACT_DIR", str(tmp_path / "artifacts"))
    monkeypatch.setenv("ANTIGRAVITY_TELEMETRY_DIR", str(tmp_path / "telemetry"))
    vault = fx.make_vault(tmp_path)
    seeded = fx.seed_real_distribution(vault)
    return MemoryController(FileStorageEngine(str(vault))), seeded


def _search_row(controller, seeded, kind):
    note_id, word = seeded[kind]
    out = ma.search(controller, word, limit=10)
    rows = [r for r in out["query_results"] if r["id"] == note_id]
    return rows[0] if rows else None


READABLE_KINDS = ("active_verified", "active_partially_verified", "active_unstamped", "review_unverified", "review_unstamped")


@pytest.mark.parametrize("kind", READABLE_KINDS)
def test_read_contract_active_and_review_notes_are_searchable_and_gettable_by_the_agent(distribution, kind):
    """CLAUDE.md: memory_search returns snippets; memory_get serves ACTIVE or REVIEW notes,
    REVIEW marked unverified. Whether a note is verified decides the label, never the visibility."""
    controller, seeded = distribution
    note_id, word = seeded[kind]

    row = _search_row(controller, seeded, kind)
    assert row is not None, f"{kind} was not returned by search"
    assert word in row["snippet"], (kind, row["snippet"])

    got = ma.get(controller, note_id)
    assert word in got["content"]
    assert got["lifecycle"] == ("REVIEW" if kind.startswith("review") else "ACTIVE")
    # only a verified note is served without the unverified flag; a REVIEW one is always flagged
    assert got["unverified"] is (kind != "active_verified")
    assert ma.NOTICE in got["notice"]


@pytest.mark.parametrize("kind", ("review_quarantined", "active_quarantined"))
def test_read_contract_a_quarantined_note_is_hidden_from_the_agent(distribution, kind):
    controller, seeded = distribution
    note_id, word = seeded[kind]
    row = _search_row(controller, seeded, kind)
    # the note may be listed (id/title), but its body never reaches the agent
    assert row is None or row["snippet"] == ""
    with pytest.raises(ValueError, match="quarantined"):
        ma.get(controller, note_id)
    # the owner can still inspect it
    assert word in ma.get(controller, note_id, principal=Principal.HUMAN)["content"]


def test_read_contract_an_archived_note_is_not_served_to_the_agent(distribution):
    controller, seeded = distribution
    note_id, word = seeded["archived"]
    row = _search_row(controller, seeded, "archived")
    assert row is None or row["snippet"] == ""
    with pytest.raises(ValueError, match="not eligible for cognitive retrieval"):
        ma.get(controller, note_id)


def test_read_contract_the_review_fallback_never_synthesizes_trust(distribution):
    """Handing over an unverified REVIEW candidate adds no verification and no promotion state."""
    controller, seeded = distribution
    note_id, _ = seeded["review_unverified"]
    before = dict(controller.storage.get(note_id))
    got = ma.get(controller, note_id)
    assert got["verification"] == "unverified" and got["lifecycle"] == "REVIEW"
    after = controller.storage.get(note_id)
    assert after["verification"] == before["verification"] and after["lifecycle"] == before["lifecycle"]


def test_read_contract_distribution_matches_the_real_vault_shape(distribution):
    """The non-empty-snippet rate over the real distribution of states. On the real vault this
    was 38/48 on main, fell to 2/48 with the verified-only rule, and must be full again."""
    controller, seeded = distribution
    served = 0
    for kind in READABLE_KINDS:
        row = _search_row(controller, seeded, kind)
        served += bool(row and row["snippet"])
    assert served == len(READABLE_KINDS)


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


def test_quarantined_note_has_empty_snippet_in_search_results(world):
    import uuid
    controller, vault = world
    note_id = str(uuid.uuid4())
    quarantine_path = vault / "01_ARCHITECTURE" / "knowledge" / "quarantine_note.md"
    quarantine_path.write_text(
        "---\n"
        f"id: {note_id}\n"
        "type: knowledge\n"
        "category: quarantine-note\n"
        "tags: [quarantined]\n"
        "created: 2026-09-01\n"
        "updated: 2026-09-01\n"
        "provenance:\n  source_type: user\n  source_ref: fixture\n"
        "confidence: high\n"
        "verification: verified\n"
        "quarantined: true\n"
        "relations: []\n"
        "lifecycle: ACTIVE\n"
        "---\n"
        "# quarantine note\n\nSensitive quarantined payload that must never leak.\n",
        encoding="utf-8"
    )
    controller.storage.id_to_path.clear()
    controller.storage._cache.clear()
    controller.storage._initialize_index()
    
    out = ma.search(controller, "Sensitive quarantined payload", limit=5)
    matching = [r for r in out["query_results"] if r["id"] == note_id]
    assert len(matching) == 1
    assert matching[0]["snippet"] == ""
    
    # get() must refuse to return quarantined notes to AI_AGENT
    with pytest.raises(ValueError, match="quarantined"):
        ma.get(controller, note_id, principal=Principal.AI_AGENT)


def test_an_unverified_active_note_keeps_its_content_in_search_and_get(world):
    """Not being verified is a label, not a reason to hide an ACTIVE note (it was, once)."""
    import uuid
    controller, vault = world
    note_id = str(uuid.uuid4())
    unverified_path = vault / "01_ARCHITECTURE" / "knowledge" / "unverified_active.md"
    unverified_path.write_text(
        fx.note_text("unverified active", "Unverified but active text served as untrusted data.", note_id, verification="unverified"),
        encoding="utf-8",
    )
    controller.storage.id_to_path.clear()
    controller.storage._cache.clear()
    controller.storage._initialize_index()

    out = ma.search(controller, "Unverified but active text served", limit=5)
    matching = [r for r in out["query_results"] if r["id"] == note_id]
    assert len(matching) == 1
    assert "Unverified but active text" in matching[0]["snippet"]
    got = ma.get(controller, note_id)
    assert got["unverified"] is True and "Unverified but active text" in got["content"]
