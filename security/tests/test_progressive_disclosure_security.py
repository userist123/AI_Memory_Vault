"""Progressive disclosure must never downgrade or erase trust evidence."""

import sys
from pathlib import Path

PACKAGES = Path(__file__).resolve().parents[2] / "03_IMPLEMENTATION" / "packages"
if str(PACKAGES) not in sys.path:
    sys.path.insert(0, str(PACKAGES))

from retrieval.context.progressive_disclosure import ProgressiveDisclosure


class Budget:
    hard_token_budget = 10000

    def check_budget(self, usage):
        if usage > 10000:
            raise RuntimeError("budget")

    def estimate_tokens(self, value):
        return len(str(value))


def note():
    return {
        "id": "n1",
        "type": "lesson",
        "lifecycle": "ACTIVE",
        "confidence": "high",
        "content": "Verified architectural knowledge. " * 20,
        "verification": {"status": "TRUSTED", "scanner_verdict": "ALLOW"},
        "security": {"trust_state": "TRUSTED"},
        "provenance": {"verified": True, "source_ref": "agent-a"},
        "integrity": {"sha256": "abc"},
        "requirements": ["preserve boundary"],
        "forbidden": ["bypass verification"],
        "acceptance": ["evidence remains attached"],
    }


def test_all_disclosure_levels_preserve_security_metadata():
    disclosure = ProgressiveDisclosure(Budget())
    source = note()

    metadata = disclosure.metadata_only([source])[0]
    snippet = disclosure.snippet([source], chars=20)[0]
    sections = disclosure.sections([dict(source, content="first line\\npreserve boundary" )], "boundary")[0]
    full = disclosure.full_document([source])[0]

    for result in (metadata, snippet, sections, full):
        assert result["verification"]["status"] == "TRUSTED"
        assert result["security"]["trust_state"] == "TRUSTED"
        assert result["provenance"]["verified"] is True
        assert result["requirements"] == ["preserve boundary"]
        assert result["forbidden"] == ["bypass verification"]
        assert result["acceptance"] == ["evidence remains attached"]


def test_unverified_notes_never_enter_disclosure():
    disclosure = ProgressiveDisclosure(Budget())
    source = note()
    source["verification"] = {"status": "UNTRUSTED"}
    assert disclosure.metadata_only([source]) == []
    assert disclosure.snippet([source]) == []
    assert disclosure.sections([source], "boundary") == []
    assert disclosure.full_document([source]) == []
