"""The edge cleaner changes frontmatter only. It never writes prose into a note body.

PR #209 finding B11: `clean_source_frontmatters.py` used to inject sentences into the bodies of
nine `Promoted_*` notes so that notes whose rejected relations were removed would not become
islands (for example "... knowledge of total length, optimizing sample preservation in
[[long-term memory]]."). That is unsupported text written into knowledge notes. A note with no
true relation is an island until a real relation is declared in its frontmatter.
"""
from __future__ import annotations

import importlib.util
import pathlib

import pytest

REPO = pathlib.Path(__file__).resolve().parents[1]
SCRIPT = REPO / "30_SCRIPTS" / "knowledge" / "clean_source_frontmatters.py"
KNOWLEDGE = REPO / "01_ARCHITECTURE" / "knowledge"


def _load_cleaner():
    spec = importlib.util.spec_from_file_location("clean_source_frontmatters_under_test", SCRIPT)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


NOTE = (
    "---\n"
    "id: 11111111-1111-4111-8111-111111111111\n"
    "type: knowledge\n"
    "lifecycle: REVIEW\n"
    "relations:\n"
    "  - type: part_of\n"
    "    target_id: \"slot-06-procedures\"\n"
    "  - type: related_to\n"
    "    target_id: \"22222222-2222-4222-8222-222222222222\"\n"
    "---\n"
    "\n"
    "# Reservoir Sampling\n"
    "\n"
    "## Canonical Definition\n"
    "\n"
    "Reservoir sampling is an algorithmic procedure without prior knowledge of total length.\n"
    "\n"
    "Trailing text with odd spacing.   \n"
    "\n\n"
)


def _body(text: str) -> str:
    return text.split("\n---\n", 1)[1]


@pytest.mark.parametrize("note_text", [NOTE, NOTE.replace("\n", "\r\n")], ids=["lf", "crlf"])
def test_cleaning_preserves_the_body_byte_for_byte(tmp_path, note_text):
    cleaner = _load_cleaner()
    path = tmp_path / "Promoted_reservoir_sampling.md"  # a name the old script injected prose into
    path.write_bytes(note_text.encode("utf-8"))
    before = path.read_bytes()

    changed, _msg = cleaner.clean_frontmatter_relations(
        path, [{"relation": "part_of", "target_id": "slot-06-procedures"}], dry_run=False
    )

    after = path.read_bytes()
    assert changed is True
    assert after != before  # the rejected relation really is gone
    assert b"slot-06-procedures" not in after
    assert b"22222222-2222-4222-8222-222222222222" in after  # the other relation survives
    # Everything from the closing fence on is untouched.
    fence = b"---\r\n" if b"\r\n" in before else b"---\n"
    assert before.rsplit(fence, 1)[1] == after.rsplit(fence, 1)[1]
    assert b"[[" not in after


def test_dry_run_writes_nothing(tmp_path):
    cleaner = _load_cleaner()
    path = tmp_path / "Promoted_buffer.md"
    path.write_text(NOTE, encoding="utf-8")
    cleaner.clean_frontmatter_relations(
        path, [{"relation": "part_of", "target_id": "slot-06-procedures"}], dry_run=True
    )
    assert path.read_text(encoding="utf-8") == NOTE


def test_script_has_no_body_rewriting_table():
    source = SCRIPT.read_text(encoding="utf-8")
    assert "ISLAND_PREVENTIONS" not in source
    assert "body_text.replace" not in source


# The nine sentences the old script wrote into note bodies. None may be present.
_INJECTED = {
    "Promoted_transformation.md": "governing state transitions in a [[state-determined system]]",
    "Promoted_buffer.md": "central production system in [[working memory]]",
    "Promoted_regulation.md": "maintaining stability via [[feedback]]",
    "Promoted_reinforcement_learning.md": "guiding selection in [[operator]]",
    "Promoted_reservoir_sampling.md": "optimizing sample preservation in [[long-term memory]]",
    "Promoted_retrieval.md": "ongoing agent actions from [[long-term memory]]",
    "Promoted_variety.md": "deviate from uniformity in a [[state-determined system]]",
    "Promoted_homeostat.md": "study [[ultrastable system]] dynamics",
    "Promoted_state_determined_system.md": "without intrinsic randomness in state [[transformation]]",
}


@pytest.mark.parametrize("name,sentence", sorted(_INJECTED.items()))
def test_committed_notes_carry_no_injected_prose(name, sentence):
    text = (KNOWLEDGE / name).read_text(encoding="utf-8")
    assert sentence not in text
