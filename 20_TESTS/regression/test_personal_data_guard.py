"""The personal-data guard: what it must refuse, and what it must leave alone.

Every false-positive case below is one the guard actually produced against this
repository before its rules were tightened — spreadsheet floats, digits inside
hex hashes, Stripe decline numbers, an Amazon order number, a support-article
id in a URL, a truncated token id. They are pinned so a looser rule cannot come
back unnoticed.

All identifiers are synthetic. The CNP is built here with its own checksum
formula rather than by calling the guard, so the test does not certify the
code with itself.
"""
from __future__ import annotations

import json
import pathlib
import subprocess
import sys

import pytest

_REPO = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(_REPO / "30_SCRIPTS" / "verification"))

import personal_data_guard as guard  # noqa: E402


def synthetic_cnp(prefix: str = "190010112345") -> str:
    weights = "279146358279"
    control = sum(int(a) * int(b) for a, b in zip(prefix, weights)) % 11
    return prefix + str(1 if control == 10 else control)


VALID_IBAN = "RO49AAAA1B31007593840000"      # the published format example
VALID_CARD = "4539148803436467"               # Luhn-valid, not a processor test number


# --- identifiers that must be found ------------------------------------------

def test_a_valid_cnp_is_found():
    assert guard.scan_text(f"CNP: {synthetic_cnp()}") == {"cnp": 1}


def test_a_valid_romanian_iban_is_found():
    assert guard.scan_text(f"IBAN {VALID_IBAN}") == {"iban_ro": 1}


@pytest.mark.parametrize("layout", [
    VALID_CARD,
    "4539 1488 0343 6467",
    "4539-1488-0343-6467",
])
def test_a_card_number_is_found_in_the_layouts_cards_are_printed_in(layout):
    assert guard.scan_text(layout) == {"card": 1}


# --- validation, not pattern matching ----------------------------------------

def test_a_cnp_with_the_wrong_control_digit_is_not_a_cnp():
    cnp = synthetic_cnp()
    wrong = cnp[:-1] + str((int(cnp[-1]) + 1) % 10)
    assert guard.scan_text(wrong) == {}


def test_a_cnp_with_an_impossible_birth_date_is_not_a_cnp():
    assert guard.scan_text(synthetic_cnp("199133012345")) == {}


def test_an_iban_failing_mod_97_is_not_an_iban():
    assert guard.scan_text(VALID_IBAN[:-1] + "1") == {}


def test_a_luhn_invalid_number_is_not_a_card():
    assert guard.scan_text(VALID_CARD[:-1] + "8") == {}


# --- false positives this repository actually produced ------------------------

@pytest.mark.parametrize("text", [
    pytest.param(f"123.{synthetic_cnp()}", id="spreadsheet float fraction"),
    pytest.param(f'"context_hash": "3a9f{synthetic_cnp()}bcd4"', id="digits inside a hex hash"),
])
def test_thirteen_digits_that_are_not_a_cnp(text):
    assert guard.scan_text(text) == {}


@pytest.mark.parametrize("text", [
    pytest.param("'insufficient_funds': '4000000000009995'", id="stripe decline number"),
    pytest.param("4242424242424242", id="published processor test number"),
    pytest.param("2026-01-03,Amazon,123-4539148-8034364,Office", id="order number"),
    pytest.param("support.example.com/hc/articles/45391488034364-NCCL", id="url article id"),
    pytest.param(f"{VALID_CARD}...,7688,0,0", id="truncated token id"),
    pytest.param(f"12.{VALID_CARD}", id="float fraction"),
])
def test_digit_runs_that_are_not_cards(text):
    assert guard.scan_text(text) == {}


# --- document names ------------------------------------------------------------

@pytest.mark.parametrize("name", [
    "Factura-791390337.pdf",
    "account-statement_2026-01-01_2026-06-14_ro-ro.pdf",
    "repayment_plan.docx",
    "Buget_Personal_2026.xlsx",
    "scan_buletin.jpg",
])
def test_a_document_named_for_what_it_is_is_refused(name):
    assert "PERSONAL_DOCUMENT_NAME" in guard.inspect(name, b"")


@pytest.mark.parametrize("name", [
    ".agents/skills/invoice-organizer/SKILL.md",
    "30_SCRIPTS/billing/invoice_parser.py",
])
def test_code_and_notes_about_invoices_are_not_invoices(name):
    assert guard.inspect(name, b"") == []


# --- the output never republishes what it found --------------------------------

def _git_repo(tmp_path: pathlib.Path, files: dict[str, str]) -> pathlib.Path:
    subprocess.run(["git", "init", "-q", str(tmp_path)], check=True)
    for rel, content in files.items():
        target = tmp_path / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")
    subprocess.run(["git", "-C", str(tmp_path), "add", "-A"], check=True)
    return tmp_path


def _run(root: pathlib.Path) -> subprocess.CompletedProcess:
    return subprocess.run(
        [sys.executable, str(_REPO / "30_SCRIPTS" / "verification" / "personal_data_guard.py"),
         "--root", str(root)],
        capture_output=True, text=True,
    )


def test_a_finding_names_the_path_and_never_the_value(tmp_path):
    cnp = synthetic_cnp()
    root = _git_repo(tmp_path, {"notes/client.md": f"Client CNP {cnp}, IBAN {VALID_IBAN}"})
    result = _run(root)
    assert result.returncode == 1
    assert "CNPx1:notes/client.md" in result.stdout
    assert "IBAN_ROx1:notes/client.md" in result.stdout
    assert cnp not in result.stdout + result.stderr
    assert VALID_IBAN not in result.stdout + result.stderr


def test_a_clean_repository_passes(tmp_path):
    root = _git_repo(tmp_path, {"README.md": "nothing personal here"})
    result = _run(root)
    assert result.returncode == 0
    assert "PERSONAL_DATA_STATUS=PASS" in result.stdout


# --- the allowlist cannot become a silent exemption ----------------------------

def test_an_allowlisted_file_with_a_reason_is_skipped(tmp_path):
    root = _git_repo(tmp_path, {
        "fixtures/sample.md": f"{synthetic_cnp()}",
        "09_SECURITY/personal_data_allowlist.json": json.dumps([
            {"path": "fixtures/sample.md", "reason": "synthetic value for a parser test"}
        ]),
    })
    assert _run(root).returncode == 0


def test_an_allowlist_entry_without_a_reason_fails(tmp_path):
    root = _git_repo(tmp_path, {
        "fixtures/sample.md": "clean",
        "09_SECURITY/personal_data_allowlist.json": json.dumps([{"path": "fixtures/sample.md"}]),
    })
    result = _run(root)
    assert result.returncode == 1
    assert "ALLOWLIST_WITHOUT_REASON:fixtures/sample.md" in result.stdout


def test_an_allowlist_entry_for_a_missing_file_fails(tmp_path):
    root = _git_repo(tmp_path, {
        "09_SECURITY/personal_data_allowlist.json": json.dumps([
            {"path": "gone/long_ago.pdf", "reason": "was reviewed once"}
        ]),
    })
    result = _run(root)
    assert result.returncode == 1
    assert "ALLOWLIST_STALE:gone/long_ago.pdf" in result.stdout


# --- the repository itself --------------------------------------------------------

def test_this_repository_carries_no_personal_documents():
    """Runs against every tracked file. This is the check that would have
    stopped the account statement, the budgets, the invoices and the repayment
    plan — each of which spent over a week on a public `main`."""
    result = _run(_REPO)
    assert result.returncode == 0, result.stdout
