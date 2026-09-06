"""Procedural-memory notes must be complete, or the suite fails.

The vault records what is known. These notes record what was *done* — how a
problem was diagnosed and solved — so the next agent applies the method rather
than rediscovering it. Four reviewers spent an evening re-deriving the same
findings because no such record existed.

An unenforced convention decays. This vault has the proof: `synapse_store.py`
claimed for months to be "NOT wired into MemoryController.search()" while the
controller imported it in its constructor, and an external audit believed the
docstring over the code. So the contract is checked rather than trusted.

The section that matters most is "Still open". A method note without it is a
claim of completeness nobody verified, which is precisely the "task started,
task reported finished" failure this exists to prevent.
"""
import re
from pathlib import Path

import pytest
import yaml

from lifecycle.validation.schema import validate_frontmatter

REPO = Path(__file__).resolve().parents[1]
PROCEDURE = REPO / "10_DOCUMENTATION" / "procedures" / "Recording_A_Solved_Problem.md"

REQUIRED_SECTIONS = (
    "## Problem",
    "## How it was found",
    "## What fixed it",
    "## How it was verified",
    "## Reuse this when",
    "## Still open",
)

SCHEMA_KEYS = {
    "id", "type", "lifecycle", "category", "tags", "created", "updated",
    "provenance", "confidence", "verification", "relations",
}


def _frontmatter(path: Path) -> dict:
    text = path.read_text(encoding="utf-8")
    m = re.match(r"^---\n(.*?)\n---", text, re.S)
    assert m, f"{path.name} has no frontmatter"
    return yaml.safe_load(m.group(1)) or {}


def _frontmatter_or_none(path: Path):
    """Tolerant read for the discovery scan.

    Templates carry placeholders like `created: {{date}}`, which YAML rejects.
    A template is not a note, and one unparseable file must not abort
    collection for the entire suite.
    """
    try:
        meta = _frontmatter(path)
    except (AssertionError, yaml.YAMLError, UnicodeDecodeError, OSError):
        return None
    return meta if isinstance(meta, dict) else None


def _method_notes():
    """Notes tagged `method` — the procedural-memory set."""
    found = []
    for root in ("01_ARCHITECTURE", "10_DOCUMENTATION"):
        for path in (REPO / root).rglob("*.md"):
            meta = _frontmatter_or_none(path)
            if meta and "method" in (meta.get("tags") or []):
                found.append(path)
    return sorted(found)


def test_the_procedure_defining_the_contract_exists():
    assert PROCEDURE.exists(), (
        "the procedure that defines what 'finished' means is itself the contract; "
        "without it the notes below have nothing to conform to"
    )


def test_method_notes_exist():
    notes = _method_notes()
    assert notes, "no procedural-memory notes found; the method is not being recorded"


@pytest.mark.parametrize("path", _method_notes(), ids=lambda p: p.stem)
def test_frontmatter_validates_against_the_canonical_schema(path):
    """Validated on READ, not on write. An earlier draft passed at write time
    and failed here: unquoted dates come back from YAML as `datetime.date`
    while the schema requires a string."""
    meta = _frontmatter(path)
    validate_frontmatter({k: v for k, v in meta.items() if k in SCHEMA_KEYS})


@pytest.mark.parametrize("path", _method_notes(), ids=lambda p: p.stem)
def test_every_required_section_is_present(path):
    text = path.read_text(encoding="utf-8")
    if _frontmatter(path).get("type") == "procedure":
        pytest.skip("the defining procedure sets the contract rather than following it")
    missing = [s for s in REQUIRED_SECTIONS if s not in text]
    assert not missing, f"{path.name} is missing {missing}"


@pytest.mark.parametrize("path", _method_notes(), ids=lambda p: p.stem)
def test_still_open_is_answered_not_merely_present(path):
    """A heading with nothing under it is the same as no heading."""
    text = path.read_text(encoding="utf-8")
    if "## Still open" not in text:
        pytest.skip("not a solved-problem note")
    body = text.split("## Still open", 1)[1].strip()
    assert len(body) > 20, (
        f"{path.name}: 'Still open' is empty. State the remainder explicitly, "
        "including that there is none — an unanswered section is an unverified "
        "claim of completeness"
    )


@pytest.mark.parametrize("path", _method_notes(), ids=lambda p: p.stem)
def test_no_note_promotes_itself(path):
    """A session grading its own work is the failure mode the lifecycle policy
    exists to prevent. Method notes enter at REVIEW and are promoted by the
    policy, never by their author."""
    lifecycle = _frontmatter(path).get("lifecycle")
    assert lifecycle not in ("ACTIVE", "VERIFIED"), (
        f"{path.name} was written straight to {lifecycle}; method notes enter at REVIEW"
    )


@pytest.mark.parametrize("path", _method_notes(), ids=lambda p: p.stem)
def test_provenance_distinguishes_measurement_from_opinion(path):
    """`execution` means it came from running something; `inference` means it
    did not. Collapsing the two is how an opinion becomes evidence."""
    prov = _frontmatter(path).get("provenance") or {}
    assert prov.get("source_type") in {"execution", "inference", "experience"}, (
        f"{path.name}: provenance.source_type is {prov.get('source_type')!r}; "
        "a method note must say whether it was measured or reasoned"
    )
    assert prov.get("source_ref"), f"{path.name}: provenance.source_ref is empty"


def test_the_contract_names_what_finished_means():
    """If the definition of done disappears from the procedure, every note
    below it is conforming to nothing."""
    text = PROCEDURE.read_text(encoding="utf-8")
    for phrase in ('What "finished" means', "Still open", "Required sections", "Enforcement"):
        assert phrase in text, f"the procedure no longer defines: {phrase}"
