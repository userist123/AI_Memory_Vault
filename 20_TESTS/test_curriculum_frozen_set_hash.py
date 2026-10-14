"""D-07: the OpenStax ch08 frozen question set has a recorded hash, like heldout v2.

The set is the evaluation instrument of the curriculum transfer benchmark. Without a
sidecar, an edit to a question or to the answer key would pass silently. The hash is
taken over LF-normalised bytes, as in test_benchmark_v2_gold_integrity.py, so a Windows
checkout (CRLF) of unchanged content still matches.
"""
from __future__ import annotations

import hashlib
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
SET = REPO / "07_EVALUATION" / "curriculum" / "openstax_ch08_frozen_test_set.json"
SIDECAR = SET.with_name(SET.name + ".sha256")
#: The value recorded when the sidecar was created (2026-10-14), equal to the file as
#: committed in 0bb6791a5. Pinned here as well so that rewriting both the set and the
#: sidecar together is still visible in review.
EXPECTED = "07b4a338619672cfd1f8ef44319c29cc61cc8c87d2770565f05499f7c087796d"


def _canonical(path: Path) -> bytes:
    return path.read_bytes().replace(b"\r\n", b"\n").replace(b"\r", b"\n")


def test_sidecar_matches_the_frozen_set():
    recorded = SIDECAR.read_text(encoding="utf-8").strip()
    assert recorded == EXPECTED
    assert hashlib.sha256(_canonical(SET)).hexdigest() == recorded


def test_crlf_checkout_gives_the_same_digest():
    as_crlf = _canonical(SET).replace(b"\n", b"\r\n")
    normalised = as_crlf.replace(b"\r\n", b"\n")
    assert hashlib.sha256(normalised).hexdigest() == EXPECTED


def test_a_content_change_breaks_the_hash():
    raw = _canonical(SET)
    tampered = raw.replace(b'"', b"'", 1)
    assert tampered != raw
    assert hashlib.sha256(tampered).hexdigest() != EXPECTED
