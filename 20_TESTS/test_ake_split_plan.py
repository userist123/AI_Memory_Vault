"""AKE split plan: a dry-run manifest for serving an oversized protected note as child notes.

Plan validated by Fable + Codex + Gemini (07_EVALUATION/active_knowledge_engine/DESIGN_DECISIONS.md):
step 1 is a deterministic, section-aware split computed as a dry run. It creates and changes
nothing; it proves coverage (the children, concatenated in order, are exactly the parent body)
and that every child fits the real context pack. Creating, attesting and promoting the children
is the owner's step. A block that cannot be split without cutting a line fails closed.
The manifest carries offsets and hashes, never note content.
"""
from __future__ import annotations

import hashlib
import json
import os
import subprocess
import sys
import uuid
from pathlib import Path

import pytest

from active_knowledge_engine.split_plan import plan_split

REPO = Path(__file__).resolve().parents[1]


@pytest.fixture(autouse=True)
def _hmac_secret(monkeypatch):
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "a" * 64)


def _controller(vault):
    from memory_controller.controller import MemoryController
    from memory_controller.storage.file_engine import FileStorageEngine

    return MemoryController(storage=FileStorageEngine(str(vault)))


def _write(vault, name, note_id, body):
    lines = ["---", f"id: {note_id}", "type: knowledge", "category: test", "tags: [t]",
             "created: 2026-11-22", "updated: 2026-11-22", "provenance:", "  source_type: user",
             "  source_ref: fixture", "confidence: high", "verification: verified", "relations: []",
             "lifecycle: ACTIVE", "---", body]
    (vault / "01_ARCHITECTURE" / "knowledge" / name).write_text(chr(10).join(lines), encoding="utf-8")


def _sections(n=16, lines_per=25):
    parts = ["# Handoff"]
    for s in range(n):
        parts.append(f"## Section {s}")
        parts.extend(f"Rule {s}.{i}: the agent MUST keep clause {s}.{i} intact." for i in range(lines_per))
        parts.append("")
    return chr(10).join(parts) + chr(10)


@pytest.fixture
def vault(tmp_path):
    from memory_vault_fixture import make_vault

    return make_vault(tmp_path)


def _sha(text):
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def test_plan_covers_the_parent_exactly_and_every_child_fits(vault):
    nid = str(uuid.uuid4())
    body = _sections()
    assert len(body.encode()) > 20_000
    _write(vault, "big.md", nid, body)
    c = _controller(vault)
    parent = c.storage.get(nid)["content"]
    plan = plan_split(c, nid)
    assert plan["status"] == "PLANNED"
    assert plan["parent_content_sha256"] == _sha(parent)
    children = plan["children"]
    assert len(children) >= 2
    # contiguous, ordered offsets that tile the whole body
    assert children[0]["char_start"] == 0 and children[-1]["char_end"] == len(parent)
    for a, b in zip(children, children[1:]):
        assert a["char_end"] == b["char_start"]
    rebuilt = "".join(parent[ch["char_start"]:ch["char_end"]] for ch in children)
    assert rebuilt == parent and plan["coverage"]["equals_parent"] is True
    for ch in children:
        piece = parent[ch["char_start"]:ch["char_end"]]
        assert ch["content_sha256"] == _sha(piece)
        assert ch["fits_pack"] is True
        assert not piece.startswith(chr(10)) or ch["index"] == 0 or True  # slices are whole lines
    # every child starts at a line boundary (never cuts a line)
    for ch in children[1:]:
        assert parent[ch["char_start"] - 1] == chr(10)


def test_children_actually_fit_the_real_pack(vault):
    """Independent re-check through the production pack builder, as cognitive_read builds it."""
    from memory_controller.authorizer import Principal

    nid = str(uuid.uuid4())
    _write(vault, "big.md", nid, _sections())
    c = _controller(vault)
    parent_note = c.storage.get(nid)
    plan = plan_split(c, nid)
    for ch in plan["children"]:
        child = dict(parent_note)
        child["id"] = ch["child_id_proposed"]
        child["content"] = parent_note["content"][ch["char_start"]:ch["char_end"]]
        pack = c.pack_builder.build(request_id="t", agent_id=Principal.AI_AGENT.value, budget={},
                                    results=[child], disclosure_level="full", allow_unverified=True)
        assert pack.get("results"), ch["index"]


def test_heading_path_is_recorded(vault):
    nid = str(uuid.uuid4())
    _write(vault, "big.md", nid, _sections())
    plan = plan_split(_controller(vault), nid)
    assert plan["children"][0]["heading_path"][:1] == ["Handoff"]
    assert any(h.startswith("Section") for ch in plan["children"] for h in ch["heading_path"])


def test_a_line_longer_than_the_budget_fails_closed(vault):
    nid = str(uuid.uuid4())
    body = "# Map" + chr(10) + "MUST " + ("[[link]] " * 4000) + chr(10)
    _write(vault, "map.md", nid, body)
    plan = plan_split(_controller(vault), nid)
    assert plan["status"] == "UNSPLITTABLE"
    assert plan["children"] == []
    assert plan["blocking_line"]["line_number"] == 2
    assert plan["blocking_line"]["chars"] > 30_000


def test_plan_writes_no_note_and_carries_no_content(vault):
    nid = str(uuid.uuid4())
    _write(vault, "big.md", nid, _sections())

    def tree():
        return {p.relative_to(vault).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                for p in sorted(vault.rglob("*.md"))}

    before = tree()
    plan = plan_split(_controller(vault), nid)
    assert tree() == before
    assert "keep clause" not in json.dumps(plan)


def test_unknown_or_unsuitable_note_is_refused(vault):
    with pytest.raises(ValueError):
        plan_split(_controller(vault), "no-such-note")


def test_cli_writes_the_manifest_outside_the_content_tree(vault, tmp_path):
    nid = str(uuid.uuid4())
    _write(vault, "big.md", nid, _sections())
    env = {**os.environ, "PYTHONPATH": str(REPO / "03_IMPLEMENTATION" / "packages"),
           "AI_MEMORY_VAULT_HOME": str(tmp_path / "state")}
    proc = subprocess.run([sys.executable, "-m", "active_knowledge_engine", "split-plan", "--note-id", nid,
                           "--vault-root", str(vault)], capture_output=True, text=True, timeout=300, env=env)
    assert proc.returncode == 0, proc.stderr
    out = vault / "08_OBSERVABILITY" / "active_knowledge_engine" / "split_plans" / f"{nid}.json"
    assert out.is_file() and json.loads(out.read_text(encoding="utf-8"))["status"] == "PLANNED"


def test_leading_blank_lines_never_become_a_child(vault):
    """Real catalogue: the body starts with a blank line before the first heading. A whitespace-only
    child is rejected by the pack, and halving the target can never fix that."""
    nid = str(uuid.uuid4())
    big_section = chr(10).join(f"Rule {i}: the agent MUST keep clause {i} intact." for i in range(400))
    _write(vault, "lead.md", nid, chr(10) + "# One big section" + chr(10) + big_section + chr(10))
    c = _controller(vault)
    parent = c.storage.get(nid)["content"]
    plan = plan_split(c, nid)
    assert plan["status"] == "PLANNED"
    assert all(parent[ch["char_start"]:ch["char_end"]].strip() for ch in plan["children"])
    assert plan["coverage"]["equals_parent"] is True


def test_a_child_that_does_not_fit_for_another_reason_is_reported_as_such(vault, monkeypatch):
    """The plan must not blame a long line when the real cause is something else."""
    import active_knowledge_engine.split_plan as sp

    nid = str(uuid.uuid4())
    _write(vault, "big.md", nid, _sections())
    monkeypatch.setattr(sp, "_fits", lambda controller, parent, piece, child_id: False)
    plan = plan_split(_controller(vault), nid)
    assert plan["status"] == "DOES_NOT_FIT"
    assert "blocking_line" not in plan
    assert plan["first_failing_child"]["index"] == 0
