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
   `repayment_plan`, `buget_personal`, `salariu`, `buletin`, `passport`, `cnp`,
   `carte_identitate`, `contract_de_munca`, `adeverinta`, `fisa`, `rambursare`.
   The same names are refused on `.txt`, `.md`, `.zip`, `.eml`, `.msg` and
   `.csv` exports. Short tokens that are common words in engineering notes —
   `CI`, `CV`, `diploma`, `certificat` — count only on scans and office
   documents, where `ci-policy.md` cannot be meant.
2. **Validated identifiers in content.** A Romanian CNP with a correct control
   digit and a real birth date; a Romanian IBAN that passes mod-97; a payment
   card number that passes Luhn and has a real issuer prefix. An IBAN is found
   compact or printed in groups of four, in either case. Validation is the
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

By default it does not scan history. Removing a file from `main` does not
remove it from the repository; that takes a history rewrite, recorded
separately.

## History mode

`--history` applies the same rules to every blob reachable from any ref
(`git rev-list --all --objects`, read through one `git cat-file --batch`).
Binary blobs (a NUL byte, unless the format has an extractor: PDF, docx/xlsx)
are not content-scanned, and neither are blobs over `--max-blob-bytes`
(default 2 MB); their names are still checked. Each finding is
`RULE:path@blob` — the path git first lists the blob under and a short blob id,
never the value. The exit code is 1 on any finding. CI runs it as a
non-blocking report: history findings need a rewrite, not a code change.

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
#: Text exports and archives a personal document also travels as. Only the
#: unambiguous name rule applies to them; content is scanned as for any file.
TEXT_EXPORT_SUFFIXES = {".txt", ".md", ".zip", ".eml", ".msg", ".csv"}
PERSONAL_DOCUMENT_NAME = re.compile(
    r"(factur[aăi]|invoice|account[-_ ]?statement|extras[-_ ]?(de[-_ ]?)?cont|\bextras\b|"
    r"repayment|rambursare|"
    r"buget[-_ ]?personal|personal[-_ ]?budget|salariu|fluturas|payslip|"
    r"buletin|carte[-_ ]?(de[-_ ]?)?identitate|passport|pasaport|\bcnp\b|\biban\b|"
    r"contract[-_ ]?(de[-_ ]?)?munc|adeverin|fi[sș]a)",
    re.IGNORECASE,
)
#: Short or everyday tokens: a name rule only on scans and office documents.
#: `_` counts as a separator, so `CI_fata.jpg` matches and `decimal.pdf` does not.
SCAN_ONLY_DOCUMENT_NAME = re.compile(
    r"((?<![a-z0-9])(ci|cv)(?![a-z0-9])|diplom|certificat)",
    re.IGNORECASE,
)

#: A CNP may follow `=`, `:` or `-` (`cnp=…`, `CNP-…`), but not a digit run
#: joined by a dash or a decimal point (order numbers, floats).
_CNP = re.compile(r"(?<![\w.,/#])(?<!\d-)[1-8]\d{12}(?![\w]|[.,]\d)")
_CNP_KEY = "279146358279"
#: Compact or grouped in fours (`RO49 AAAA 1B31 …`), any case; normalised
#: before mod-97.
_IBAN_RO = re.compile(
    r"(?<![A-Za-z0-9])RO\d{2} ?[A-Z]{4}(?: ?[A-Z0-9]{4}){4}(?![A-Za-z0-9])",
    re.IGNORECASE,
)
HISTORY_MAX_BLOB_BYTES = 2_000_000
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
    s = s.replace(" ", "").upper()
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


def personal_document_name(path: str) -> bool:
    name, suffix = Path(path).name, Path(path).suffix.lower()
    if suffix in DOCUMENT_SUFFIXES:
        return bool(PERSONAL_DOCUMENT_NAME.search(name) or SCAN_ONLY_DOCUMENT_NAME.search(name))
    if suffix in TEXT_EXPORT_SUFFIXES:
        return bool(PERSONAL_DOCUMENT_NAME.search(name))
    return False


def inspect(path: str, data: bytes) -> list[str]:
    findings: list[str] = []
    if personal_document_name(path):
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


def history_blobs(root: Path) -> list[tuple[str, str, int]]:
    """(blob id, path, size) for every (blob, path) pair reachable from any ref.

    A blob is listed once per distinct path it ever had: the name rule and the allowlist
    are judged per path, so a blob first seen under a benign name and later under a
    personal-document name is not missed (R-09)."""
    listing = subprocess.run(["git", "rev-list", "--all", "--objects"], cwd=root,
                             capture_output=True, check=True).stdout.decode("utf-8", "replace")
    paths: dict[str, list[str]] = {}
    for line in listing.splitlines():
        sha, _, path = line.partition(" ")
        if path and path not in paths.setdefault(sha, []):
            paths[sha].append(path)
    if not paths:
        return []
    check = subprocess.run(
        ["git", "cat-file", "--batch-check=%(objectname) %(objecttype) %(objectsize)"],
        cwd=root, input="\n".join(paths).encode(), capture_output=True, check=True,
    ).stdout.decode().splitlines()
    blobs = []
    for line in check:
        sha, kind, size = line.split()
        if kind == "blob":
            for path in paths[sha]:
                blobs.append((sha, path, int(size)))
    return blobs


def run_history(root: Path, max_blob_bytes: int = HISTORY_MAX_BLOB_BYTES) -> list[str]:
    allowed, failures = load_allowlist(root)
    blobs = [b for b in history_blobs(root) if b[1] not in allowed]
    wanted_shas: list[str] = []
    for sha, _, size in blobs:
        if size <= max_blob_bytes and sha not in wanted_shas:
            wanted_shas.append(sha)
    wanted = [(sha, None, None) for sha in wanted_shas]
    contents: dict[str, bytes] = {}
    if wanted:
        proc = subprocess.Popen(["git", "cat-file", "--batch"], cwd=root,
                                stdin=subprocess.PIPE, stdout=subprocess.PIPE)
        out, _ = proc.communicate("".join(f"{sha}\n" for sha, _, _ in wanted).encode())
        if proc.returncode:
            raise subprocess.CalledProcessError(proc.returncode, "git cat-file --batch")
        pos = 0
        for sha, _, _ in wanted:
            header_end = out.index(b"\n", pos)
            size = int(out[pos:header_end].split()[2])
            contents[sha] = out[header_end + 1:header_end + 1 + size]
            pos = header_end + 1 + size + 1
    for sha, path, _ in blobs:
        data = contents.get(sha)
        findings = inspect(path, data) if data is not None else (
            ["PERSONAL_DOCUMENT_NAME"] if personal_document_name(path) else [])
        for finding in findings:
            failures.append(f"{finding}:{path}@{sha[:12]}")
    return failures


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--root", default=".")
    parser.add_argument("--history", action="store_true",
                        help="scan every blob reachable from any ref instead of the working tree")
    parser.add_argument("--max-blob-bytes", type=int, default=HISTORY_MAX_BLOB_BYTES,
                        help="history mode: blobs larger than this are judged by name only")
    parser.add_argument("paths", nargs="*", help="files to check; default: every tracked file")
    args = parser.parse_args()
    root = Path(args.root).resolve()
    if args.history:
        failures = run_history(root, args.max_blob_bytes)
    else:
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
