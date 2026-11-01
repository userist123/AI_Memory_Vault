"""Keeps the critical-review rule wired and its corrections ledger well-formed.

Scope of this test (construct validity, see the protocol): it checks WIRING and FORMAT only — every
runtime's instruction file points at the protocol, the skill points at it, the protocol keeps its checks,
card fields, tiers and error classes, and each ledger line follows the schema and the owner-privacy rule.
It does not and cannot show that the protocol reduces reasoning errors; the protocol marks that UNVERIFIED
until the seeded benchmark runs.
"""
import json
import re
from datetime import date
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
PROTOCOL_REL = "00_GOVERNANCE/protocols/Critical_Review_Protocol_V1.md"
PROTOCOL = REPO / PROTOCOL_REL
SKILL = REPO / ".claude" / "skills" / "critical-review" / "SKILL.md"
LEDGER_REL = "00_GOVERNANCE/review/CORRECTIONS_LEDGER.jsonl"
LEDGER = REPO / LEDGER_REL

FIELDS = {"date", "agent", "model_family", "claim", "correction", "error_class",
          "caught_by", "severity", "evidence_ref"}
ERROR_CLASSES = {"scope", "search_space", "staleness", "construct_validity", "composition",
                 "alternative", "disconfirmation", "overconfidence", "fabrication"}
CAUGHT_BY = {"owner", "self", "challenger", "test"}
SEVERITY = {"low", "medium", "high"}
# Owner privacy (AGENTS.md "Owner privacy"): no real Windows profile paths; only the placeholder `user`.
PROFILE_PATH = re.compile(r"(?i)(?:[a-z]:[\\/]+|/[a-z]/)users[\\/]+(?!user\b)[^\\/\s\"]+")


def _text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_protocol_exists_with_frontmatter_id_and_the_five_checks():
    text = _text(PROTOCOL)
    assert text.startswith("---\n"), "protocol must start with YAML frontmatter"
    front = text.split("---", 2)[1]
    assert re.search(r'^id: "[0-9a-f-]{36}"$', front, re.M), "frontmatter needs a uuid id (storage skips notes without one)"
    for check in ("**Scope**", "**Search space**", "**Alternatives**", "**Disconfirmation**",
                  "**Confidence and residual**"):
        assert check in text, check
    for label in ("CONCLUSION:", "CHECKED:", "NOT CHECKED:", "ALTERNATIVES:", "FALSIFIER:", "CONFIDENCE:"):
        assert label in text, label
    for cls in ERROR_CLASSES:
        assert f"`{cls}`" in text, f"error class {cls} must be documented in the protocol"
    for marker in ("**Tier 1 — light**", "**Tier 2 — full card and challenge**", "status UNVERIFIED"):
        assert marker in text, marker


@pytest.mark.parametrize("name", ["AGENTS.md", "CLAUDE.md", "GEMINI.md"])
def test_instruction_files_point_to_the_protocol(name):
    assert PROTOCOL_REL in _text(REPO / name), f"{name} must reference {PROTOCOL_REL}"


def test_skill_points_to_the_protocol_and_has_a_trigger_description():
    text = _text(SKILL)
    front = text.split("---", 2)[1]
    assert re.search(r"^name: critical-review$", front, re.M)
    description = re.search(r"^description: (.+)$", front, re.M)
    assert description and "verdict" in description.group(1)
    assert PROTOCOL_REL in text
    assert LEDGER_REL in text


def test_ledger_lines_follow_the_schema():
    lines = [ln for ln in _text(LEDGER).splitlines() if ln.strip()]
    assert lines, "ledger must not be empty"
    for n, line in enumerate(lines, 1):
        entry = json.loads(line)
        assert set(entry) == FIELDS, f"line {n}: fields {sorted(set(entry) ^ FIELDS)}"
        date.fromisoformat(entry["date"])
        assert entry["error_class"] in ERROR_CLASSES, f"line {n}: error_class"
        assert entry["caught_by"] in CAUGHT_BY, f"line {n}: caught_by"
        assert entry["severity"] in SEVERITY, f"line {n}: severity"
        for key in ("agent", "model_family", "claim", "correction", "evidence_ref"):
            assert isinstance(entry[key], str) and entry[key].strip(), f"line {n}: {key} empty"
        assert not PROFILE_PATH.search(line), f"line {n}: owner profile path in ledger"


def test_privacy_pattern_catches_real_profile_paths_and_allows_the_placeholder():
    assert PROFILE_PATH.search(r"C:\Users\someone\Documents")  # hygiene: intentional-absolute-path
    assert PROFILE_PATH.search("/c/Users/someone/x")  # hygiene: intentional-absolute-path
    assert not PROFILE_PATH.search(r"C:\Users\user\Documents")  # hygiene: intentional-absolute-path
