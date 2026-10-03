"""Security metadata must survive context-budget degradation."""

import sys
from pathlib import Path

PACKAGES = Path(__file__).resolve().parents[2] / "03_IMPLEMENTATION" / "packages"
if str(PACKAGES) not in sys.path:
    sys.path.insert(0, str(PACKAGES))

from retrieval.context.pack_builder import ContextPackBuilder


def test_budget_degradation_cannot_erase_trust_evidence():
    item = {
        "content": "Verified decision " * 200,
        "relevance": 1.0,
        "verification": {
            "status": "TRUSTED",
            "scanner_verdict": "ALLOW",
            "artifact_sha256": "abc123",
        },
        "security": {
            "trust_state": "TRUSTED",
        },
        "provenance": {
            "verified": True,
            "source_ref": "agent-a",
        },
        "requirements": ["preserve the security boundary"],
        "acceptance": ["trust evidence remains attached"],
    }

    pack = ContextPackBuilder().build(
        request_id="test-reduction-security-metadata",
        agent_id="default",
        budget={
            "max_notes": 1,
            "max_full_documents": 0,
            "soft": 128,
            "hard": 2048,
            "soft_tokens": 64,
            "hard_tokens": 512,
        },
        results=[item],
        disclosure_level="metadata_only",
    )

    assert len(pack["results"]) == 1
    result = pack["results"][0]

    assert result["verification"]["status"] == "TRUSTED"
    assert result["verification"]["scanner_verdict"] == "ALLOW"
    assert result["security"]["trust_state"] == "TRUSTED"
    assert result["provenance"]["verified"] is True
    assert result["requirements"] == item["requirements"]
    assert result["acceptance"] == item["acceptance"]


def test_controller_style_trusted_verification_string_is_normalized():
    item = {
        "id": "trusted-string",
        "content": "Controller-produced trusted memory",
        "verification": "verified",
        "provenance": {"source_type": "test", "source_ref": "controller"},
    }

    pack = ContextPackBuilder().build(
        request_id="test-trusted-string",
        agent_id="default",
        budget={"hard": 2048, "hard_tokens": 512},
        results=[item],
        disclosure_level="metadata_only",
    )

    assert pack["results"][0]["verification"] == {"status": "VERIFIED"}


def test_real_vault_trusted_provenance_states_reach_agent_context():
    import re

    repo_root = Path(__file__).resolve().parents[2]
    knowledge_root = repo_root / "01_ARCHITECTURE" / "knowledge"
    found = {}

    for note in knowledge_root.rglob("*.md"):
        text = note.read_text(encoding="utf-8")
        match = re.search(r"^verification:\s*([A-Za-z_]+)\s*$", text, re.MULTILINE)
        if match:
            status = match.group(1).upper()
            if status in {"VERIFIED_SOURCE", "DERIVED_FROM_VERIFIED_SOURCE"}:
                found.setdefault(status, (note, text))

    assert "VERIFIED_SOURCE" in found

    for status, (note, text) in found.items():
        pack = ContextPackBuilder().build(
            request_id=f"real-vault-{status.lower()}",
            agent_id="default",
            budget={"hard": 8192, "hard_tokens": 2048},
            results=[{
                "id": note.stem,
                "content": text[:512],
                "verification": {"status": status},
                "provenance": {"source_ref": str(note.relative_to(repo_root))},
            }],
            disclosure_level="snippet",
        )

        assert len(pack["results"]) == 1
        assert pack["results"][0]["verification"]["status"] == status
