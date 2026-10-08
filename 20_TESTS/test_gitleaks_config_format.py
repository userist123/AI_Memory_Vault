"""`.gitleaks.toml` stays in the array-of-tables form every other branch extends.

Gitleaks refuses a config that mixes the deprecated single `[allowlist]` table with
`[[allowlists]]` ("[allowlist] is deprecated, it cannot be used alongside [[allowlists]]"), so
the day two branches that each extend the file with an `[[allowlists]]` block meet a branch that
rewrote it to `[allowlist]`, the secret scan and the R001 history scan turn red on whichever
lands second. This is a structural check of the file; running gitleaks itself is the CI job's.
"""
import re
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
TEXT = (REPO / ".gitleaks.toml").read_text(encoding="utf-8")

#: The block the other open branches append, verbatim. A branch that carries it byte for byte
#: merges as a no-op with each of them.
SHARED_BLOCK = '''# Python type annotations of cryptography key classes, flagged by generic-api-key as
# "private_key: Ed25519PrivateKey" in agent_bridge/crypto.py history (commit 2c9c1018ef,
# PR #211). The "value" is a class name, not key material. CI scans `--all` refs, so one
# historical false positive on any branch fails every PR's history scan.
[[allowlists]]
description = "Cryptography key type annotations (class names, not keys)"
regexTarget = "match"
regexes = [
  \'\'\'(?:private|public)_key:\\s*(?:Ed25519|X25519)(?:Private|Public)Key\\b\'\'\',
]
'''


def test_no_legacy_single_allowlist_table():
    assert not re.search(r"^\[allowlist\]\s*$", TEXT, re.M), (
        "use [[allowlists]] tables: gitleaks rejects [allowlist] next to [[allowlists]]"
    )


def test_uses_the_array_of_tables_form():
    assert len(re.findall(r"^\[\[allowlists\]\]\s*$", TEXT, re.M)) >= 5


def test_the_shared_cryptography_annotation_block_is_present_verbatim():
    assert SHARED_BLOCK in TEXT


def test_the_allowlist_does_not_exempt_a_whole_commit_or_runtime_source():
    """Narrow on purpose: no commit allowlist, and the runtime crypto module stays scanned."""
    assert not re.search(r"^\s*commits\s*=", TEXT, re.M)
    assert "agent_bridge/crypto" not in TEXT.replace(SHARED_BLOCK, "")
    assert "Ed25519PrivateKey" not in TEXT.replace(SHARED_BLOCK, "")


def test_toml_parses_when_a_parser_is_available():
    try:
        import tomllib
    except ModuleNotFoundError:  # Python 3.10: the structural checks above still run
        return
    data = tomllib.loads(TEXT)
    assert "allowlist" not in data and len(data["allowlists"]) >= 5
