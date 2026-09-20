"""The 83% of the repository gitleaks skips is scanned by something.

`.gitleaks.toml` exempts `.agents/`, `50_ARTIFACTS/`, `07_EVALUATION/`,
`06_INBOX/RAW_IMPORTS/` and the test trees — 16,675 of 20,030 tracked files.
Each exemption is defensible; together they leave most of a public repository
unscanned. A short list of high-confidence patterns now covers exactly those
paths, with an allowlist naming every known-benign hit.
"""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
ALLOWLIST = REPO / "20_TESTS" / "fixtures" / "exempt_area_secret_allowlist.json"
GITLEAKS = REPO / ".gitleaks.toml"

_spec = importlib.util.spec_from_file_location(
    "exempt_area_secret_scan", REPO / "30_SCRIPTS" / "verification" / "exempt_area_secret_scan.py")
scanner = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(scanner)


def test_nothing_unreviewed_is_found_in_the_exempt_areas():
    assert scanner.scan() == []


def test_the_exempt_areas_are_actually_most_of_the_repository():
    """If this ever drops sharply, the exemptions changed and this scan may be redundant."""
    assert len(scanner.exempt_files()) > 10_000


def test_the_patterns_here_match_the_ones_gitleaks_is_told_to_skip():
    toml = GITLEAKS.read_text(encoding="utf-8")
    for pattern in scanner.EXEMPT_PATTERNS:
        assert pattern in toml, f"{pattern} is scanned here but no longer exempt in .gitleaks.toml"


@pytest.mark.parametrize("rule, payload", [
    ("private_key", "-----BEGIN RSA PRIVATE KEY-----\nMIIEow...\n"),
    ("aws_access_key", "aws_key = AKIA1234567890ABCDEF"),
    ("github_token", "token: ghp_" + "a" * 36),
    ("slack_token", "xoxb-1234567890-abcdefghij"),
    ("google_api_key", "AIza" + "b" * 35),
    ("stripe_live_key", "sk_live_" + "c" * 24),
    ("openai_key", "sk-" + "d" * 40),
    ("anthropic_key", "sk-ant-" + "e" * 40),
])
def test_each_rule_fires_on_its_own_shape(rule, payload):
    assert rule in scanner.scan_text(payload)


@pytest.mark.parametrize("innocent", [
    "The password field must never be committed.",
    "Set OPENAI_API_KEY in your environment before running this.",
    "See the AWS docs for how access keys are formatted.",
    "sk-short",
])
def test_ordinary_prose_about_secrets_stays_quiet(innocent):
    assert scanner.scan_text(innocent) == set()


def test_every_allowlist_entry_was_read_and_justified():
    entries = json.loads(ALLOWLIST.read_text(encoding="utf-8"))["entries"]
    for entry in entries:
        assert (REPO / entry["path"]).exists(), f"allowlisted file is gone: {entry['path']}"
        assert entry["rules"], entry["path"]
        assert len(entry["reason"]) > 40, f"{entry['path']} has no real reason"


def test_an_allowlist_entry_only_silences_the_rule_it_names():
    entries = json.loads(ALLOWLIST.read_text(encoding="utf-8"))["entries"]
    narrowed = {entries[0]["path"]: {"slack_token"}}
    findings = scanner.scan(allowlist=narrowed)
    assert any(f["path"] == entries[0]["path"] for f in findings)
