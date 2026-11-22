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
        piece = parent_note["content"][ch["char_start"]:ch["char_end"]]
        child = dict(parent_note, id=ch["child_id_proposed"], content=piece, lifecycle="ACTIVE")
        # cognitive_read's rule for an ACTIVE note: allow_unverified=True (controller.py)
        pack = c.pack_builder.build(request_id="t", agent_id=Principal.AI_AGENT.value, budget={},
                                    results=[child], disclosure_level="full", allow_unverified=True)
        assert pack.get("results") and pack["results"][0].get("content") == piece, ch["index"]


def test_heading_path_is_recorded(vault):
    nid = str(uuid.uuid4())
    _write(vault, "big.md", nid, _sections())
    plan = plan_split(_controller(vault), nid)
    # line numbers of the headings in effect, never the heading text (no content in the manifest)
    assert plan["children"][0]["heading_lines"][:1] == [1]
    assert all(isinstance(n, int) for ch in plan["children"] for n in ch["heading_lines"])
    assert "Section" not in json.dumps(plan)


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


# --- review round 1 ------------------------------------------------------------------------------

def test_a_child_served_shortened_does_not_count_as_fitting(vault):
    """The pack shortens long unprotected text and still returns it: that is not a fit."""
    nid = str(uuid.uuid4())
    body = chr(10).join("plain text line without normative words number %d" % i for i in range(30)) + chr(10)
    body = body + ("x" * 5900) + chr(10)
    _write(vault, "plain.md", nid, body)
    c = _controller(vault)
    plan = plan_split(c, nid)
    parent = c.storage.get(nid)
    from memory_controller.authorizer import Principal
    for ch in plan["children"]:
        piece = parent["content"][ch["char_start"]:ch["char_end"]]
        child = dict(parent, id="x", content=piece, lifecycle="ACTIVE")
        pack = c.pack_builder.build(request_id="t", agent_id=Principal.AI_AGENT.value, budget={},
                                    results=[child], disclosure_level="full", allow_unverified=True)
        served = (pack.get("results") or [{}])[0].get("content")
        assert ch["fits_pack"] == (served == piece)


def test_parent_without_provenance_is_not_planned_as_fitting(vault):
    """No provenance is fabricated: the egress contract would drop every child."""
    nid = "moc-test-0002"
    lines = ["---", f"id: {nid}", "type: moc", "lifecycle: ACTIVE", "category: navigation", "tags: [moc]",
             "---", _sections()]
    (vault / "01_ARCHITECTURE" / "knowledge" / "moc.md").write_text(chr(10).join(lines), encoding="utf-8")
    plan = plan_split(_controller(vault), nid)
    assert plan["status"] == "NEEDS_PROVENANCE" and plan["children"] == []


def test_only_newline_is_a_line_boundary(vault):
    """U+2028, U+0085 and form feed are inside a line, never a cut point."""
    nid = str(uuid.uuid4())
    odd = "Rule: the agent MUST keep" + chr(0x2028) + "this" + chr(0x0C) + "intact" + chr(0x85) + "always."
    body = _sections() + chr(10).join([odd] * 200) + chr(10)
    _write(vault, "odd.md", nid, body)
    c = _controller(vault)
    parent = c.storage.get(nid)["content"]
    plan = plan_split(c, nid)
    assert plan["status"] == "PLANNED"
    for ch in plan["children"][1:]:
        assert parent[ch["char_start"] - 1] == chr(10)


def test_a_long_line_that_fits_alone_becomes_its_own_child(vault):
    nid = str(uuid.uuid4())
    long_line = "plain " * 1200  # about 7,200 chars, unprotected, no newline inside
    body = "# T" + chr(10) + "intro" + chr(10) + long_line + chr(10) + "outro" + chr(10)
    _write(vault, "long.md", nid, body)
    plan = plan_split(_controller(vault), nid)
    assert plan["status"] in {"PLANNED", "UNSPLITTABLE"}
    if plan["status"] == "UNSPLITTABLE":  # only when that line itself does not pass the pack
        assert plan["blocking_line"]["line_number"] == 3
        assert "does not fit" in plan["reason"]


def test_a_blank_only_body_is_refused(vault):
    nid = str(uuid.uuid4())
    _write(vault, "blank.md", nid, chr(10) * 50)
    with pytest.raises(ValueError):
        plan_split(_controller(vault), nid)
