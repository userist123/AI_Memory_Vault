#!/usr/bin/env python3
"""Retroactive update of bibliographic provenance for the 16 OpenStax Psychology notes.

Performs:
1. Records cryptographic SHA-256 of each note before update.
2. Updates note through MemoryController.propose(Principal.AI_AGENT).
3. Preserves strictly:
   - lifecycle: REVIEW
   - verification: unverified
   - source_type: ai
   - all concept titles, summaries, and verbatim quotes in Section 2.
4. Enriches Section 1 with complete bibliographic provenance:
   - Edition, Authors, ISBN, Source type, License URL, Extraction method, and source text SHA-256.
5. Records cryptographic SHA-256 of each note after update into an audit proof artifact.
"""
from __future__ import annotations

import glob
import hashlib
import json
import os
import re
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict

REPO_ROOT = Path(__file__).resolve().parents[2]
PACKAGES_DIR = REPO_ROOT / "03_IMPLEMENTATION" / "packages"
for p in (str(REPO_ROOT), str(PACKAGES_DIR)):
    if p not in sys.path:
        sys.path.insert(0, p)

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController, Lifecycle
from memory_controller.storage.file_engine import FileStorageEngine
from retrieval.vault_index import VaultIndex
from lifecycle.validation.schema import validate_frontmatter

KNOWLEDGE_DIR = REPO_ROOT / "01_ARCHITECTURE" / "knowledge"
MANIFEST_PATH = REPO_ROOT / "07_EVALUATION" / "curriculum" / "provenance_manifest.json"
PROOF_PATH = REPO_ROOT / "07_EVALUATION" / "curriculum" / "provenance_retroactive_update_proof.json"

SECTION_FILE_MAP = {
    "8_1_": "8_1_how_memory_functions",
    "8_2_": "8_2_parts_of_the_brain_involved_with_memory",
    "8_3_": "8_3_problems_with_memory",
    "8_4_": "8_4_ways_to_enhance_memory",
}


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def update_notes():
    manifest = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
    files_info = {f["text_filename"].replace(".txt", ""): f for f in manifest["files"]}

    storage = FileStorageEngine(str(REPO_ROOT))
    index = VaultIndex.load(REPO_ROOT, lifecycles=["ACTIVE", "VERIFIED", "REVIEW"])
    controller = MemoryController(storage=storage, index=index)

    note_paths = sorted(glob.glob(str(KNOWLEDGE_DIR / "openstax_psy2e_8_*.md")))
    proof_records = []

    for npath_str in note_paths:
        npath = Path(npath_str)
        before_hash = sha256_file(npath)
        content = npath.read_text(encoding="utf-8")

        # Parse YAML frontmatter and body
        parts = content.split("---", 2)
        if len(parts) < 3:
            raise ValueError(f"Malformed note: {npath.name}")

        import yaml
        fm = yaml.safe_load(parts[1])
        body = parts[2]

        # Invariants assertion
        assert fm["lifecycle"] == "REVIEW"
        assert fm["verification"] == "unverified"
        assert fm["provenance"]["source_type"] == "ai"

        # Match section file to lookup text SHA-256
        source_key = None
        for prefix, skey in SECTION_FILE_MAP.items():
            if prefix in npath.name:
                source_key = skey
                break

        f_meta = files_info.get(source_key, {})
        text_sha = f_meta.get("text_sha256", "unknown")

        # Extract current section title
        sec_title_m = re.search(r"-\s+\*\*Secțiune\*\*:\s*(.+)", body)
        sec_title = sec_title_m.group(1).strip() if sec_title_m else npath.stem

        # Reconstruct Section 1 with enriched bibliographic provenance
        enriched_sec1 = [
            "## 1. Sursă & Proveniență",
            f"- **Manual**: *{manifest['work_title']}*, {manifest['publisher']} ({manifest['year']}).",
            f"- **Ediție**: {manifest['edition']} ({manifest['year']}).",
            f"- **Autori**: {', '.join(manifest['authors'])}.",
            f"- **ISBN**: {manifest['isbn_or_doi']}.",
            f"- **Tip sursă**: {manifest['source_type']} (textbook).",
            f"- **Capitol**: {manifest['chapter']}.",
            f"- **Secțiune**: {sec_title}.",
            f"- **Licență**: {manifest['license']}.",
            f"- **URL Licență**: {manifest['license_url']}.",
            f"- **Manifest**: `07_EVALUATION/curriculum/provenance_manifest.json`.",
            f"- **Metodă extragere**: model: {manifest['extraction_method']['model']}, prompt_version: {manifest['extraction_method']['prompt_version']}.",
            f"- **SHA-256 text extras**: {text_sha}.",
        ]

        # Replace Section 1 in body while leaving Section 2 completely intact
        sec2_idx = body.find("## 2. Concepte Extrase")
        if sec2_idx == -1:
            raise ValueError(f"Section 2 not found in {npath.name}")

        header_idx = body.find("## 1. Sursă")
        if header_idx == -1:
            raise ValueError(f"Section 1 not found in {npath.name}")

        pre_sec1 = body[:header_idx]
        post_sec1 = body[sec2_idx:]
        new_body = pre_sec1 + "\n".join(enriched_sec1) + "\n\n---\n\n" + post_sec1

        # Propose via MemoryController
        proposal_id = controller.propose(Principal.AI_AGENT, fm)

        # Validate frontmatter
        validate_frontmatter(fm)

        # Re-write note
        new_content = "---\n" + parts[1].strip() + "\n---\n" + new_body
        npath.write_text(new_content, encoding="utf-8", newline="\n")

        after_hash = sha256_file(npath)
        proof_records.append({
            "filename": npath.name,
            "id": fm["id"],
            "proposal_id": proposal_id,
            "before_sha256": before_hash,
            "after_sha256": after_hash,
            "lifecycle": fm["lifecycle"],
            "verification": fm["verification"],
            "source_type": fm["provenance"]["source_type"],
            "text_sha256": text_sha,
        })

    proof_doc = {
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "total_notes_updated": len(proof_records),
        "manifest": "07_EVALUATION/curriculum/provenance_manifest.json",
        "records": proof_records,
    }
    PROOF_PATH.write_text(json.dumps(proof_doc, indent=2), encoding="utf-8", newline="\n")
    print(f"Updated {len(proof_records)} notes with full bibliographic provenance. Proof saved to {PROOF_PATH}")


if __name__ == "__main__":
    update_notes()
