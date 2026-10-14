"""The personal-data guard catches the formats that actually leaked.

The documents that reached public history carried an IBAN in its printed,
space-grouped form, CNPs after `=`/`-`, and names such as a CV, an identity
card scan or an employment certificate. Each form is pinned here with synthetic
values: the CNP and IBAN checksums are computed in this file, the bank code is
the obviously fake `AAAA`, and nothing is copied from a real document.
"""
from __future__ import annotations

import pathlib
import random
import subprocess
import sys

import pytest

_REPO = pathlib.Path(__file__).resolve().parents[1]
_GUARD = _REPO / "30_SCRIPTS" / "verification" / "personal_data_guard.py"
sys.path.insert(0, str(_GUARD.parent))

import personal_data_guard as guard  # noqa: E402


def synthetic_cnp(prefix: str = "190010112345") -> str:
    weights = "279146358279"
    control = sum(int(a) * int(b) for a, b in zip(prefix, weights)) % 11
    return prefix + str(1 if control == 10 else control)


def synthetic_iban(seed: int = 7) -> str:
    rng = random.Random(seed)
    bban = "AAAA" + "".join(rng.choice("0123456789") for _ in range(16))
    numeric = "".join(str(int(c, 36)) for c in bban + "RO00")
    return f"RO{98 - int(numeric) % 97:02d}{bban}"


IBAN = synthetic_iban()
SPACED_IBAN = " ".join(IBAN[i:i + 4] for i in range(0, len(IBAN), 4))


# --- IBAN -----------------------------------------------------------------------

@pytest.mark.parametrize("form", [
    pytest.param(IBAN, id="compact"),
    pytest.param(SPACED_IBAN, id="printed in groups of four"),
    pytest.param(IBAN.lower(), id="lowercase"),
    pytest.param(SPACED_IBAN.lower(), id="lowercase grouped"),
])
def test_an_iban_is_found_in_every_printed_form(form):
    assert guard.scan_text(f"Cont: {form} la banca") == {"iban_ro": 1}


def test_a_grouped_iban_failing_mod_97_is_not_an_iban():
    broken = SPACED_IBAN[:-1] + str((int(SPACED_IBAN[-1]) + 1) % 10)
    assert guard.scan_text(broken) == {}


# --- CNP ------------------------------------------------------------------------

@pytest.mark.parametrize("sep", ["=", ":", "-", ": ", "= "])
def test_a_cnp_after_an_assignment_or_dash_is_found(sep):
    assert guard.scan_text(f"cnp{sep}{synthetic_cnp()}") == {"cnp": 1}


@pytest.mark.parametrize("text", [
    pytest.param(f"3.{synthetic_cnp()}", id="decimal fraction"),
    pytest.param(f"9{synthetic_cnp()}", id="longer digit run"),
    pytest.param(f"{synthetic_cnp()}7", id="longer digit run after"),
    pytest.param(f"12-{synthetic_cnp()}", id="digit-dash-digits order number"),
])
def test_a_cnp_adjacent_to_digits_is_still_not_a_cnp(text):
    assert guard.scan_text(text) == {}


# --- document names ---------------------------------------------------------------

@pytest.mark.parametrize("name", [
    "scans/CI_fata.jpg",
    "CI-verso.pdf",
    "carte_identitate.png",
    "CarteDeIdentitate.pdf",
    "contract_de_munca_2025.pdf",
    "contract-munca.docx",
    "adeverinta_salariat.pdf",
    "fisa_postului.docx",
    "fișa_medicala.pdf",
    "diploma_licenta.pdf",
    "certificat_nastere.jpg",
    "CV_2026.pdf",
    "invoice-2026-01.pdf",
    "Factura_123.pdf",
    "repayment-schedule.pdf",
    "rambursare.xlsx",
    "extras de cont ianuarie.pdf",
    "Extras-De-Cont.pdf",
])
def test_a_personal_document_name_is_refused(name):
    assert "PERSONAL_DOCUMENT_NAME" in guard.inspect(name, b"")


@pytest.mark.parametrize("name", [
    "notes/extras_de_cont_ianuarie.txt",
    "notes/factura_martie.md",
    "export/adeverinta.zip",
    "mail/invoice.eml",
    "mail/factura.msg",
    "export/extras-de-cont.csv",
])
def test_text_and_archive_names_are_checked_too(name):
    assert "PERSONAL_DOCUMENT_NAME" in guard.inspect(name, b"")


@pytest.mark.parametrize("name", [
    "docs/references/ci-policy.md",
    ".agents/skills/vcpkg/references/ci.md",
    "docs/security/PR209_CI_FAILURE_ROOT_CAUSE.md",
    "docs/cv-notes.md",
    "30_SCRIPTS/billing/invoice_parser.py",
    "docs/decimal.md",
    "specification.pdf",
])
def test_short_tokens_do_not_flag_notes_and_code(name):
    assert guard.inspect(name, b"") == []


# --- history mode -----------------------------------------------------------------

def _git(root: pathlib.Path, *args: str) -> None:
    subprocess.run(["git", "-C", str(root), *args], check=True, capture_output=True)


def _run(root: pathlib.Path, *extra: str) -> subprocess.CompletedProcess:
    return subprocess.run(
        [sys.executable, str(_GUARD), "--root", str(root), *extra],
        capture_output=True, text=True,
    )


@pytest.fixture
def repo_with_deleted_leak(tmp_path: pathlib.Path) -> pathlib.Path:
    _git(tmp_path, "init", "-q")
    _git(tmp_path, "config", "user.email", "test@example.invalid")
    _git(tmp_path, "config", "user.name", "test")
    _git(tmp_path, "config", "commit.gpgsign", "false")
    (tmp_path / "README.md").write_text("clean\n", encoding="utf-8")
    (tmp_path / "notes").mkdir()
    (tmp_path / "notes" / "plata.md").write_text(f"Plata in contul {SPACED_IBAN}\n", encoding="utf-8")
    (tmp_path / "blob.bin").write_bytes(b"\x00\x01" + IBAN.encode() + b"\x00")
    _git(tmp_path, "add", "-A")
    _git(tmp_path, "commit", "-q", "-m", "add")
    _git(tmp_path, "rm", "-q", "notes/plata.md", "blob.bin")
    _git(tmp_path, "commit", "-q", "-m", "remove")
    return tmp_path


def test_default_mode_passes_once_the_file_is_deleted(repo_with_deleted_leak):
    result = _run(repo_with_deleted_leak)
    assert result.returncode == 0, result.stdout
    assert "PERSONAL_DATA_STATUS=PASS" in result.stdout


def test_history_mode_finds_the_deleted_iban_without_printing_it(repo_with_deleted_leak):
    result = _run(repo_with_deleted_leak, "--history")
    assert result.returncode == 1, result.stdout + result.stderr
    assert "IBAN_ROx1:notes/plata.md@" in result.stdout
    assert "blob.bin" not in result.stdout, "binary blobs are skipped"
    for value in (IBAN, SPACED_IBAN, IBAN.lower()):
        assert value not in result.stdout + result.stderr


def test_history_mode_skips_blobs_over_the_size_limit(repo_with_deleted_leak):
    result = _run(repo_with_deleted_leak, "--history", "--max-blob-bytes", "10")
    assert result.returncode == 0, result.stdout


def test_history_mode_checks_names_of_deleted_documents(tmp_path):
    _git(tmp_path, "init", "-q")
    _git(tmp_path, "config", "user.email", "test@example.invalid")
    _git(tmp_path, "config", "user.name", "test")
    _git(tmp_path, "config", "commit.gpgsign", "false")
    (tmp_path / "CV_2026.pdf").write_bytes(b"%PDF-1.4 not really")
    _git(tmp_path, "add", "-A")
    _git(tmp_path, "commit", "-q", "-m", "add")
    _git(tmp_path, "rm", "-q", "CV_2026.pdf")
    (tmp_path / "README.md").write_text("clean\n", encoding="utf-8")
    _git(tmp_path, "add", "-A")
    _git(tmp_path, "commit", "-q", "-m", "remove")
    result = _run(tmp_path, "--history")
    assert result.returncode == 1
    assert "PERSONAL_DOCUMENT_NAME:CV_2026.pdf@" in result.stdout
