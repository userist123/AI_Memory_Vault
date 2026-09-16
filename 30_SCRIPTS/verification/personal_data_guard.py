#!/usr/bin/env python3
"""Refuse personal data in a public repository.

## Why this exists

The repository is public, and three kinds of personal document reached it
without any check noticing: a bank account statement, two personal budget
spreadsheets, then two invoices and a loan repayment plan in the name of the
owner. They sat on `main` for over a week.

Nothing was broken. `.gitleaks.toml` scans for *secrets* — API keys, private
keys, tokens — and it does that job. An invoice holds no secret. It holds a
name, an address, a client code and phone numbers, none of which any secret
scanner looks for, and several directories are allowlisted from it entirely.

So this guard looks for the other category: documents about a person.

## What it checks

1. **Document names.** A `.pdf`, `.docx`, `.xlsx`, image or similar whose name
   says what it is — `factura`, `invoice`, `account-statement`, `extras`,
   `repayment_plan`, `buget_personal`, `salariu`, `buletin`, `passport`, `cnp`.
2. **Validated identifiers in content.** A Romanian CNP with a correct control
   digit and a real birth date; a Romanian IBAN that passes mod-97; a payment
   card number that passes Luhn and has a real issuer prefix. Validation is the
   point: a 13-digit number is common in data, a 13-digit number with a valid
   CNP checksum and date is not. Digits that follow a decimal point are never
   treated as a start, because spreadsheet floats were the first false
   positive this produced.

Text is read from plain files, and from `.docx`/`.xlsx` with the standard
library. PDFs are read when `pypdf` is installed; without it a PDF is still
judged by its name.

## What it does not do

It prints the path, the rule and a count — never a value. A report that echoed
the CNP it found would publish it a second time.

It does not scan history. Removing a file from `main` does not remove it from
the repository; that takes a history rewrite, recorded separately.

## Exceptions

`09_SECURITY/personal_data_allowlist.json` lists reviewed exceptions, each with
a reason. An entry whose path no longer exists fails the run, so the list
cannot quietly accumulate permissions for files long gone.
"""
from __future__ import annotations

import argparse
import datetime as dt
import io
import json
import re
import subprocess
import sys
import zipfile
from pathlib import Path

ALLOWLIST = Path("09_SECURITY/personal_data_allowlist.json")
MAX_BYTES = 5_000_000

DOCUMENT_SUFFIXES = {
    ".pdf", ".doc", ".docx", ".odt", ".rtf", ".xls", ".xlsx", ".ods", ".csv",
    ".jpg", ".jpeg", ".png", ".heic", ".webp", ".tif", ".tiff",
}
PERSONAL_DOCUMENT_NAME = re.compile(
    r"(factur[aăi]|invoice|account[-_ ]?statement|extras[-_ ]?de[-_ ]?cont|\bextras\b|"
    r"repayment[-_ ]?plan|plan[-_ ]?(de[-_ ]?)?rambursare|grafic[-_ ]?rambursare|"
    r"buget[-_ ]?personal|personal[-_ ]?budget|salariu|fluturas|payslip|"
    r"buletin|carte[-_ ]?de[-_ ]?identitate|passport|pasaport|\bcnp\b|\biban\b)",
    re.IGNORECASE,
)

_CNP = re.compile(r"(?<![\w.,/=#-])[1-8]\d{12}(?![\w]|[.,]\d)")
_CNP_KEY = "279146358279"
_IBAN_RO = re.compile(r"(?<![A-Z0-9])RO\d{2}[A-Z]{4}[A-Z0-9]{16}(?![A-Z0-9])")
#: Plain 13-19 digit runs, or the grouped layouts cards are actually printed in
#: (4-4-4-4[-3] and Amex 4-6-5). Arbitrary hyphenation matched order numbers.
_CARD = re.compile(
    r"(?<![\w.,/=#-])"
    r"(?:\d{13,19}|\d{4}([ -])\d{4}\1\d{4}\1\d{4}(?:\1\d{3})?|\d{4}([ -])\d{6}\2\d{5})"
    r"(?![\w]|[.,]\d|\.\.|-\d)"
)

#: Published processor test numbers. They pass Luhn by design and appear in
#: vendored payment code; they identify nobody.
TEST_CARDS = frozenset({
    "378282246310005", "371449635398431", "378734493671000", "5610591081018250",
    "30569309025904", "38520000023237", "6011111111111117", "6011000990139424",
    "3530111333300000", "3566002020360505", "5555555555554444", "5105105105105100",
    "4111111111111111", "4012888888881881", "4222222222222", "4242424242424242",
    "4000056655665556", "5200828282828210", "2223003122003222", "6200000000000005",
})


def valid_cnp(s: str) -> bool:
    if len(s) != 13 or not s.isdigit() or s[0] not in "12345678":
        return False
    century = {"1": 1900, "2": 1900, "3": 1800, "4": 1800,
               "5": 2000, "6": 2000, "7": 1900, "8": 1900}[s[0]]
    try:
        dt.date(century + int(s[1:3]), int(s[3:5]), int(s[5:7]))
    except ValueError:
        return False
    if not 1 <= int(s[7:9]) <= 52:
        return False
    control = sum(int(a) * int(b) for a, b in zip(s[:12], _CNP_KEY)) % 11
    return (1 if control == 10 else control) == int(s[12])


def valid_iban(s: str) -> bool:
    rearranged = s[4:] + s[:4]
    return int("".join(str(int(c, 36)) for c in rearranged)) % 97 == 1


def valid_card(raw: str) -> bool:
    digits = re.sub(r"\D", "", raw)
    if not 13 <= len(digits) <= 19 or len(set(digits)) < 4 or digits in TEST_CARDS:
        return False
    #: Processor test and decline numbers are padded with zeros (4000000000000002…).
    if "00000" in digits:
        return False
    if not re.match(r"^(4|5[1-5]|2[2-7]|3[47]|6011|65)", digits):
        return False
    total = 0
    for i, ch in enumerate(reversed(digits)):
        n = int(ch)
        if i % 2:
            n = n * 2 - 9 if n * 2 > 9 else n * 2
        total += n
    return total % 10 == 0


def scan_text(text: str) -> dict[str, int]:
    found = {
        "cnp": sum(valid_cnp(m.group(0)) for m in _CNP.finditer(text)),
        "iban_ro": sum(valid_iban(m.group(0)) for m in _IBAN_RO.finditer(text)),
        "card": sum(valid_card(m.group(0)) for m in _CARD.finditer(text)),
    }
    return {k: v for k, v in found.items() if v}


def extract_text(path: str, data: bytes) -> str | None:
    """Readable text, or None when the format cannot be read here."""
    suffix = Path(path).suffix.lower()
    if suffix in {".docx", ".xlsx", ".pptx", ".odt", ".ods"}:
        try:
            with zipfile.ZipFile(io.BytesIO(data)) as z:
                xml = " ".join(z.read(n).decode("utf-8", "ignore")
                               for n in z.namelist() if n.endswith(".xml"))
        except zipfile.BadZipFile:
            return None
        return re.sub(r"<[^>]+>", " ", xml)
    if suffix == ".pdf":
        try:
            import pypdf
        except ImportError:
            return None
        try:
            reader = pypdf.PdfReader(io.BytesIO(data))
            return "\n".join((page.extract_text() or "") for page in reader.pages)
        except Exception:
            return None
    if b"\x00" in data[:8000]:
        return None
    return data.decode("utf-8", "replace")


def inspect(path: str, data: bytes) -> list[str]:
    findings: list[str] = []
    name = Path(path).name
    if Path(path).suffix.lower() in DOCUMENT_SUFFIXES and PERSONAL_DOCUMENT_NAME.search(name):
        findings.append("PERSONAL_DOCUMENT_NAME")
    if len(data) <= MAX_BYTES:
        text = extract_text(path, data)
        if text:
            for rule, count in scan_text(text).items():
                findings.append(f"{rule.upper()}x{count}")
    return findings


def load_allowlist(root: Path) -> tuple[dict[str, str], list[str]]:
    target = root / ALLOWLIST
    if not target.exists():
        return {}, []
    entries = json.loads(target.read_text(encoding="utf-8"))
    allowed: dict[str, str] = {}
    problems: list[str] = []
    for entry in entries:
        path, reason = entry.get("path", ""), entry.get("reason", "").strip()
        if not reason:
            problems.append(f"ALLOWLIST_WITHOUT_REASON:{path}")
        elif not (root / path).exists():
            problems.append(f"ALLOWLIST_STALE:{path}")
        else:
            allowed[path] = reason
    return allowed, problems


def tracked_files(root: Path) -> list[str]:
    out = subprocess.run(["git", "ls-files", "-z"], cwd=root, capture_output=True, check=True)
    return [p for p in out.stdout.decode("utf-8").split("\0") if p]


def run(root: Path, paths: list[str]) -> list[str]:
    allowed, failures = load_allowlist(root)
    for path in paths:
        if path in allowed:
            continue
        full = root / path
        if not full.is_file():
            continue
        for finding in inspect(path, full.read_bytes()):
            failures.append(f"{finding}:{path}")
    return failures


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--root", default=".")
    parser.add_argument("paths", nargs="*", help="files to check; default: every tracked file")
    args = parser.parse_args()
    root = Path(args.root).resolve()
    failures = run(root, args.paths or tracked_files(root))
    if failures:
        print("PERSONAL_DATA_STATUS=FAIL")
        print(f"FAILURE_COUNT={len(failures)}")
        for failure in sorted(failures):
            print(failure)
        return 1
    print("PERSONAL_DATA_STATUS=PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
