"""B09 guard: a phase report of the Book-to-Memory track must not claim more than was measured.

PR #209 B09: reports said "verified" and "empirically verified" where only unit tests had passed. This test fails
if a phase report (or the master report or the track README) uses a claim word -- verified, proven, guaranteed,
"empirical(ly)" and their Romanian equivalents -- on a line that carries no link to an evidence file (a test, an
evaluation artefact, a script) and is not a negation ("not empirically validated").

What to do when this fails: say what was measured ("unit tests pass", "checked by `20_TESTS/test_x.py`"), or put the
path of the evidence file on the same line. The `VERIFIED` lifecycle state (upper case, usually in code spans) is a
state name, not a claim, and is not matched.
"""
import re
from pathlib import Path

import pytest

TRACK = Path(__file__).resolve().parents[2] / "08_RESEARCH" / "BOOK_TO_MEMORY"
REPORTS = sorted(TRACK.glob("PHASE*_*.md")) + [TRACK / "MASTER_RESEARCH_TRACK_CLOSURE_REPORT.md", TRACK / "README.md"]

CLAIM = re.compile(
    r"\b(?:EMPIRICALLY|FULLY|CRYPTOGRAPHICALLY)\s+VERIFIED\b"
    r"|\b(?:verified|proven|proves|proved|guaranteed|guarantees?)\b"
    r"|\bempiric\w*\b"
    r"|\b(?:verificat[ăe]?|dovedit[ăe]?|demonstrat[ă]?|garantat[ăe]?|garantează)\b",
    re.I,
)
EVIDENCE = re.compile(r"(?:20_TESTS|07_EVALUATION|security/tests|30_SCRIPTS|08_RESEARCH/BOOK_TO_MEMORY)/[\w./\-]+\.(?:py|json|md)|\btest_[\w]+\.py\b")
NEGATION = re.compile(r"\b(?:not|no|never|without|nu|fără)\b[^.|]{0,60}\bempiric|NOT_EMPIRICALLY|not empirical", re.I)
#: Verbatim quotations of the governing policy; they quote a requirement, they do not claim a result.
QUOTED_POLICY = [
    ("PHASE4_USAGE_TEST_ARCHITECTURE.md", "Trebuie demonstrat că poate contribui la rezolvarea unei sarcini reale"),
    ("PHASE5_ABLATION_ARCHITECTURE.md", "Trebuie demonstrat că nota aduce valoare incrementală"),
]


def violations(name: str, text: str):
    """(line number, claim word, line) for every unsupported claim in one report."""
    out = []
    blocks, start = [], 0
    for chunk in re.split(r"(\n\s*\n)", text):
        blocks.append((start, chunk))
        start += len(chunk)
    for offset, block in blocks:
        if "Evidence caveat" in block:
            continue
        for i, line in enumerate(block.split("\n")):
            lineno = text[:offset].count("\n") + i + 1
            stripped = re.sub(r"`[^`]*`", "", line)
            if any(f == name and q in line for f, q in QUOTED_POLICY):
                continue
            for m in CLAIM.finditer(stripped):
                if m.group(0) == "VERIFIED":      # the lifecycle state name, not a claim
                    continue
                if EVIDENCE.search(line) or NEGATION.search(re.sub(r"(?i)with(?:out)?[-_ ]note", "", stripped)):
                    continue
                out.append((lineno, m.group(0), line.strip()[:160]))
    return out


@pytest.mark.parametrize("report", REPORTS, ids=lambda p: p.name)
def test_report_makes_no_unsupported_claim(report):
    found = violations(report.name, report.read_text(encoding="utf-8"))
    assert not found, (
        f"{report.name} uses claim wording without a linked evidence file; say what was measured "
        f"(\"unit tests pass\") or link the evidence:\n" + "\n".join(f"  line {n}: {w!r}: {l}" for n, w, l in found)
    )


def test_the_guard_catches_the_old_wording():
    bad = "**Status**: COMPLETED & EMPIRICALLY VERIFIED (240/240 PASS)\n"
    assert violations("X.md", bad)
    assert violations("X.md", "All gates were verified.\n")
    assert violations("X.md", "Faza 2 este validată empiric prin 304 teste.\n")
    assert violations("X.md", "Sistemul garantează că nimic nu trece.\n")


def test_the_guard_lets_supported_and_negated_statements_through():
    assert not violations("X.md", "Checked by `20_TESTS/test_book_to_memory_lifecycle_gates.py`; verified as passing in tests/test_x.py.\n")
    assert not violations("X.md", "Status: unit tests pass (240/240); not empirically validated.\n")
    assert not violations("X.md", "The note reaches the `VERIFIED` state only through attestation.\n")
    assert not violations("X.md", "> **Evidence caveat.** \"Fully verified\" below means the unit tests pass.\n\nNext paragraph.\n")
    assert not violations("X.md", "Unit tests pass (`security/tests/test_x.py`).\n")


def test_every_report_is_covered():
    names = {p.name for p in REPORTS}
    assert "MASTER_RESEARCH_TRACK_CLOSURE_REPORT.md" in names and "README.md" in names
    assert len([n for n in names if n.startswith("PHASE")]) >= 30
    assert all(p.exists() for p in REPORTS)


def test_quoted_policy_allowances_still_exist():
    for fname, quote in QUOTED_POLICY:
        assert quote in (TRACK / fname).read_text(encoding="utf-8"), f"{fname}: the quoted line moved; update QUOTED_POLICY"
