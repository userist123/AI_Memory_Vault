"""The promotion gate cannot be passed with a word.

U05 made `promote_approved()` demand a "verified" approval, but the approval was stamped when the
reviewer *string* was "human" or "admin", and the CLI defaulted `--reviewer human`: anyone, an
agent included, satisfied the gate by typing (or omitting) the right word. An approval is now an
owner attestation: a typed owner Principal (the vault's own ATTEST matrix: HUMAN and ADMIN), a
reviewer name and an explicit evidence reference, none of them defaulted.
"""
from __future__ import annotations

import inspect
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

from cognitive_core import memory_v6_cli  # noqa: E402
from cognitive_core.authorizer import Principal  # noqa: E402
from cognitive_core.extraction import AtomicMemoryExtractor  # noqa: E402
from cognitive_core.proposal_queue import MemoryProposalQueue, owner_principal_values  # noqa: E402
from cognitive_core.queue_promoter import QueuePromoter  # noqa: E402
from memory_controller.authorizer import Principal as ControllerPrincipal  # noqa: E402


class _Controller:
    """Records what the promoter proposes; the real controller is exercised in test_rest_proposal_flow."""

    def __init__(self):
        self.proposed = []

    def propose(self, principal, note):
        self.proposed.append(note)
        return note["id"]


@pytest.fixture
def queue(tmp_path):
    q = MemoryProposalQueue(tmp_path / "queue.jsonl")
    q.enqueue(AtomicMemoryExtractor().extract("Am decis: folosim SQLite WAL pentru index.", "session:test"))
    return q


def _only_id(queue):
    return queue.pending()[0]["candidate_id"]


def _record(queue, candidate_id):
    return next(r for r in queue._load() if r["candidate_id"] == candidate_id)


def test_the_owner_principals_are_the_vaults_attest_matrix():
    assert owner_principal_values() == {"human", "admin"}


@pytest.mark.parametrize("claim", ["human", "admin", "Human", "jarvis-human", ""])
def test_a_reviewer_string_alone_does_not_approve(queue, claim):
    cid = _only_id(queue)
    with pytest.raises((PermissionError, ValueError)):
        queue.mark(cid, "APPROVED", reviewer=claim, evidence_reference="ticket-1")
    assert _record(queue, cid)["queue_status"] == "PENDING_REVIEW"
    assert _record(queue, cid)["verification"] == "unverified"


@pytest.mark.parametrize("approver", ["human", "admin", None, 7, object()])
def test_a_self_declared_approver_that_is_not_a_principal_is_refused(queue, approver):
    cid = _only_id(queue)
    with pytest.raises(PermissionError):
        queue.mark(cid, "APPROVED", reviewer="alice", evidence_reference="ticket-1", approver=approver)
    assert _record(queue, cid)["queue_status"] == "PENDING_REVIEW"


def test_an_ai_agent_cannot_approve_even_when_it_is_a_real_principal(queue):
    cid = _only_id(queue)
    for agent in (Principal.AI_AGENT, ControllerPrincipal.AI_AGENT):
        with pytest.raises(PermissionError):
            queue.mark(cid, "APPROVED", reviewer="alice", evidence_reference="ticket-1", approver=agent)
    assert _record(queue, cid)["queue_status"] == "PENDING_REVIEW"


def test_there_is_no_default_reviewer_and_no_default_evidence(queue):
    cid = _only_id(queue)
    assert inspect.signature(MemoryProposalQueue.mark).parameters["reviewer"].default == ""
    with pytest.raises(ValueError, match="no default reviewer"):
        queue.mark(cid, "APPROVED", evidence_reference="ticket-1", approver=Principal.HUMAN)
    with pytest.raises(ValueError, match="no default reviewer"):
        queue.mark(cid, "REJECTED")
    with pytest.raises(ValueError, match="evidence_reference"):
        queue.mark(cid, "APPROVED", reviewer="alice", approver=Principal.HUMAN)
    assert _record(queue, cid)["queue_status"] == "PENDING_REVIEW"


@pytest.mark.parametrize("owner", [Principal.HUMAN, Principal.ADMIN, ControllerPrincipal.HUMAN, ControllerPrincipal.ADMIN])
def test_an_owner_principal_with_a_reviewer_and_evidence_approves(queue, owner):
    """Both import shims (cognitive_core, memory_controller) load their own Principal class."""
    cid = _only_id(queue)
    queue.mark(cid, "APPROVED", reviewer="alice", evidence_reference="review:ticket-17", approver=owner)
    record = _record(queue, cid)
    assert record["queue_status"] == "APPROVED" and record["verification"] == "verified"
    assert record["verification_source"] == owner.value
    assert record["reviewed_by"] == "alice" and record["evidence_reference"] == "review:ticket-17"


def test_a_rejection_withdraws_an_earlier_attestation(queue):
    cid = _only_id(queue)
    queue.mark(cid, "APPROVED", reviewer="alice", evidence_reference="ticket-1", approver=Principal.HUMAN)
    queue.mark(cid, "REJECTED", reviewer="alice")
    record = _record(queue, cid)
    assert record["queue_status"] == "REJECTED"
    assert record["verification"] == "unverified"
    assert not {"verification_source", "evidence_reference"} & set(record)


def test_promotion_refuses_the_old_self_declared_shape(queue):
    """What the previous gate accepted for reviewer 'human', and the REST 'jarvis-human' record."""
    cid = _only_id(queue)
    records = queue._load()
    records[0].update(queue_status="APPROVED", verification="verified", verification_source="jarvis-human",
                      evidence_reference="attestation:human")
    queue._write(records)
    controller = _Controller()
    promoter = QueuePromoter(queue, controller, Principal.ADMIN)
    with pytest.raises(ValueError, match="verified evidence"):
        promoter.promote_approved()
    assert controller.proposed == []
    assert promoter.skipped and promoter.skipped[0]["candidate_id"] == cid


def test_one_unattested_record_does_not_block_the_attested_ones(tmp_path):
    queue = MemoryProposalQueue(tmp_path / "queue.jsonl")
    queue.enqueue(AtomicMemoryExtractor().extract("Am decis: folosim SQLite WAL.\nAm decis: folosim un singur index.", "session:test"))
    legacy, attested = [r["candidate_id"] for r in queue.pending()]
    queue.mark(attested, "APPROVED", reviewer="alice", evidence_reference="ticket-2", approver=Principal.ADMIN)
    records = queue._load()
    for record in records:
        if record["candidate_id"] == legacy:
            record.update(queue_status="APPROVED", reviewed_by="human")  # a legacy approval: no attestation
    queue._write(records)

    controller = _Controller()
    promoter = QueuePromoter(queue, controller, Principal.ADMIN)
    promoted = promoter.promote_approved()

    assert promoted == [attested] and [n["id"] for n in controller.proposed] == [attested]
    assert _record(queue, attested)["queue_status"] == "PROMOTED"
    # the legacy approval is reported and stays APPROVED for the owner to re-attest
    assert [(s["candidate_id"], s["reason"]) for s in promoter.skipped] == [(legacy, "approval is not verified")]
    assert _record(queue, legacy)["queue_status"] == "APPROVED"


def test_promoted_candidates_become_schema_valid_notes():
    for kind, expected in (("fact", "knowledge"), ("task", "project"), ("decision", "decision"),
                           ("lesson", "lesson"), ("preference", "preference"), ("procedure", "procedure")):
        candidate = AtomicMemoryExtractor._candidate(kind, f"contenut {kind}", "jarvis:web:proposal").to_dict()
        note = QueuePromoter._note_from_candidate(candidate)
        assert note["type"] == expected
        assert note["id"] == candidate["candidate_id"]
        assert set(note["provenance"]) == {"source_type", "source_ref"}


# --- the CLI: no defaults, and only owner principals can be named for an approval ----------------

@pytest.fixture
def cli(tmp_path, monkeypatch):
    monkeypatch.setattr(memory_v6_cli, "root", lambda: tmp_path)

    def run(*argv):
        monkeypatch.setattr(sys, "argv", ["memory_v6_cli", *argv])
        memory_v6_cli.main()

    queue = MemoryProposalQueue(tmp_path / "06_INBOX" / "memory_proposals.jsonl")
    queue.enqueue(AtomicMemoryExtractor().extract("Am decis: folosim SQLite WAL pentru index.", "session:test"))
    return run, queue


def test_cli_approve_has_no_default_reviewer_principal_or_evidence(cli):
    run, queue = cli
    cid = _only_id(queue)
    for argv in (
        ("approve", cid),
        ("approve", cid, "--reviewer", "human"),                      # the old, defaulted form
        ("approve", cid, "--reviewer", "alice", "--evidence", "t-1"),  # no principal
        ("approve", cid, "--principal", "human", "--evidence", "t-1"),  # no reviewer
        ("approve", cid, "--principal", "human", "--reviewer", "alice"),  # no evidence
        ("approve", cid, "--principal", "ai_agent", "--reviewer", "alice", "--evidence", "t-1"),  # not an owner
    ):
        with pytest.raises(SystemExit):
            run(*argv)
    assert _record(queue, cid)["queue_status"] == "PENDING_REVIEW"


def test_cli_reject_has_no_default_reviewer(cli):
    run, queue = cli
    with pytest.raises(SystemExit):
        run("reject", _only_id(queue))
    assert _record(queue, _only_id(queue))["queue_status"] == "PENDING_REVIEW"


def test_cli_approve_with_an_explicit_owner_principal_attests(cli, capsys):
    run, queue = cli
    cid = _only_id(queue)
    run("approve", cid, "--principal", "admin", "--reviewer", "alice", "--evidence", "review:t-9")
    assert f"approved={cid}" in capsys.readouterr().out
    record = _record(queue, cid)
    assert record["verification"] == "verified" and record["verification_source"] == "admin"
    assert record["reviewed_by"] == "alice" and record["evidence_reference"] == "review:t-9"


def test_readme_documents_the_attested_approval():
    for name in ("README.md", "README.en.md"):
        text = (REPO / name).read_text(encoding="utf-8")
        approve_lines = [ln for ln in text.splitlines() if "memory_v6_cli approve" in ln]
        assert approve_lines, name
        for line in approve_lines:
            assert "--principal" in line and "--reviewer" in line and "--evidence" in line, (name, line)


def test_a_promoted_candidate_cannot_be_approved_again(queue):
    """Re-approving it would re-propose the same note id and overwrite the promoted note."""
    cid = _only_id(queue)
    queue.mark(cid, "APPROVED", reviewer="alice", evidence_reference="ticket-1", approver=Principal.HUMAN)
    controller = _Controller()
    assert QueuePromoter(queue, controller, Principal.ADMIN).promote_approved() == [cid]
    for status in ("APPROVED", "REJECTED"):
        with pytest.raises(ValueError, match="already promoted"):
            queue.mark(cid, status, reviewer="alice", evidence_reference="ticket-2", approver=Principal.HUMAN)
    assert _record(queue, cid)["queue_status"] == "PROMOTED"
    assert QueuePromoter(queue, controller, Principal.ADMIN).promote_approved() == []
    assert len(controller.proposed) == 1
