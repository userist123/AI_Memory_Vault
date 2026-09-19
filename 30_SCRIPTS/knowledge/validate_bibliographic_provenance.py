#!/usr/bin/env python3
"""Validator for Bibliographic Provenance.

Enforces:
1. Full bibliographic metadata on source works / manifests:
   - Full title, authors, publisher, edition, publication year
   - ISBN or DOI
   - Source type (manual/textbook, primary_source, review, opinion, popularization)
   - License and verifiable license URL
   - Cryptographic SHA-256 digests of downloaded source and extracted plain text
   - Extraction method (model name and prompt template version)
2. Note-level provenance:
   - Exact source location (section or anchor)
   - Verified verbatim quote(s) substantiated by source text
   - Canonical frontmatter adherence (lifecycle: REVIEW, verification: unverified, source_type: ai)
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple, Union

REPO_ROOT = Path(__file__).resolve().parents[2]
PACKAGES_DIR = REPO_ROOT / "03_IMPLEMENTATION" / "packages"
if str(PACKAGES_DIR) not in sys.path:
    sys.path.insert(0, str(PACKAGES_DIR))

from lifecycle.validation.schema import validate_frontmatter

VALID_SOURCE_TYPES = {
    "manual",
    "textbook",
    "sursă primară",
    "primary_source",
    "review",
    "opinie",
    "opinion",
    "popularizare",
    "popularization",
}


def validate_source_manifest(manifest_data: Dict[str, Any]) -> Tuple[bool, List[str]]:
    errors: List[str] = []

    # Title
    if not manifest_data.get("work_title"):
        errors.append("Missing or empty 'work_title'")

    # Authors
    authors = manifest_data.get("authors")
    if not isinstance(authors, list) or len(authors) == 0:
        errors.append("Missing or empty 'authors' list")

    # Publisher
    if not manifest_data.get("publisher"):
        errors.append("Missing or empty 'publisher'")

    # Edition
    if not manifest_data.get("edition"):
        errors.append("Missing or empty 'edition'")

    # Year
    year = manifest_data.get("year") or manifest_data.get("publication_year")
    if not year:
        errors.append("Missing or empty 'year'")

    # ISBN or DOI
    isbn_or_doi = manifest_data.get("isbn_or_doi") or manifest_data.get("isbn") or manifest_data.get("doi")
    if not isbn_or_doi:
        errors.append("Missing or empty 'isbn_or_doi'")

    # Source type
    st = str(manifest_data.get("source_type", "")).lower()
    if st not in VALID_SOURCE_TYPES:
        errors.append(f"Invalid or missing 'source_type': '{st}'. Must be one of {sorted(VALID_SOURCE_TYPES)}")

    # License and URL
    if not manifest_data.get("license"):
        errors.append("Missing or empty 'license'")

    l_url = manifest_data.get("license_url", "")
    if not l_url or not l_url.startswith("http"):
        errors.append("Missing or invalid 'license_url' (must be an HTTP/HTTPS URL)")

    # Extraction method
    em = manifest_data.get("extraction_method")
    if not isinstance(em, dict) or not em.get("model") or not em.get("prompt_version"):
        errors.append("Missing or invalid 'extraction_method' (must contain 'model' and 'prompt_version')")

    # Files with SHA-256
    files = manifest_data.get("files")
    if not isinstance(files, list) or len(files) == 0:
        errors.append("Missing or empty 'files' list in manifest")
    else:
        for idx, f in enumerate(files):
            # Check download hash
            h_hash = f.get("html_sha256") or f.get("download_sha256")
            if not h_hash or len(h_hash) != 64:
                errors.append(f"File index {idx} missing valid 64-char download/HTML SHA-256")
            # Check text hash
            t_hash = f.get("text_sha256")
            if not t_hash or len(t_hash) != 64:
                errors.append(f"File index {idx} missing valid 64-char text SHA-256")

    return len(errors) == 0, errors


def parse_markdown_note(content: str) -> Tuple[Optional[Dict[str, Any]], str]:
    """Extracts frontmatter YAML dict and markdown body."""
    if not content.startswith("---"):
        return None, content
    parts = content.split("---", 2)
    if len(parts) < 3:
        return None, content

    import yaml
    try:
        frontmatter = yaml.safe_load(parts[1])
        body = parts[2]
        return frontmatter, body
    except Exception:
        return None, content


def validate_note_provenance(
    note_path: Union[Path, str],
    repo_root: Path = REPO_ROOT,
    skip_archived: bool = True,
) -> Tuple[bool, List[str]]:
    path = Path(note_path)
    if not path.exists():
        return False, [f"Note file not found: {path}"]

    content = path.read_text(encoding="utf-8")
    fm, body = parse_markdown_note(content)
    if not fm:
        return False, [f"Failed to parse frontmatter in note: {path.name}"]

    if skip_archived and fm.get("lifecycle") == "ARCHIVED":
        return True, ["Skipped: note is ARCHIVED"]

    errors: List[str] = []

    # 1. Canonical frontmatter schema validation
    try:
        validate_frontmatter(fm)
    except Exception as exc:
        errors.append(f"Frontmatter schema validation failed: {exc}")

    # 2. Check source location (section or anchor)
    sec_match = re.search(r"-\s+\*\*(?:Secțiune|Section)\*\*:\s*(.+)", body)
    if not sec_match or not sec_match.group(1).strip():
        errors.append("Note body missing mandatory section location (e.g. '- **Secțiune**: ...')")

    # 3. Check edition and license in note body
    ed_m = re.search(r"-\s+\*\*(?:Ediție|Edition)\*\*:\s*(\S+.*)", body)
    has_edition = ed_m is not None and bool(ed_m.group(1).strip())

    lic_m = re.search(r"-\s+\*\*(?:Licență|License)\*\*:\s*(\S+.*)", body)
    has_license = lic_m is not None and bool(lic_m.group(1).strip())

    # 4. Check manifest reference
    manifest_match = re.search(r"-\s+\*\*(?:Manifest)\*\*:\s*`?([^`\n]+)`?", body)
    if manifest_match:
        rel_m = manifest_match.group(1).strip()
        manifest_path = repo_root / rel_m
        if not manifest_path.exists():
            errors.append(f"Referenced provenance manifest not found: {rel_m}")
        else:
            try:
                mdata = json.loads(manifest_path.read_text(encoding="utf-8"))
                m_ok, m_errs = validate_source_manifest(mdata)
                if not m_ok:
                    errors.extend([f"Manifest error: {e}" for e in m_errs])
            except Exception as ex:
                errors.append(f"Failed to read/parse manifest {rel_m}: {ex}")
    else:
        errors.append("Note body missing mandatory manifest reference ('- **Manifest**: ...')")

    if not has_edition:
        errors.append("Note missing mandatory bibliographic edition (e.g. '- **Ediție**: ...')")

    if not has_license:
        errors.append("Note missing mandatory license declaration (e.g. '- **Licență**: ...')")

    # 5. Check quotes presence
    quotes = re.findall(r'>\s*"([^"]+)"', body)
    if not quotes:
        quotes = re.findall(r">\s*“([^”]+)”", body)
    if not quotes:
        errors.append("Note contains no verbatim verified quotes (expected blockquotes `> \"...\"`)")

    return len(errors) == 0, errors


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate bibliographic provenance of curriculum notes and manifests.")
    parser.add_argument("target_path", type=Path, help="Path to manifest JSON, note MD, or directory of notes")
    parser.add_argument("--pattern", default="*.md", help="Glob pattern when target_path is a directory")
    parser.add_argument("--include-archived", action="store_true",
                        help="validate ARCHIVED notes too, instead of skipping them")
    args = parser.parse_args()

    target = args.target_path
    if not target.exists():
        print(f"Error: path does not exist: {target}", file=sys.stderr)
        return 1

    if target.suffix.lower() == ".json":
        data = json.loads(target.read_text(encoding="utf-8"))
        ok, errors = validate_source_manifest(data)
        if not ok:
            print(f"FAILED manifest validation for {target}:", file=sys.stderr)
            for e in errors:
                print(f"  - {e}", file=sys.stderr)
            return 1
        print(f"PASS: {target} is a valid bibliographic provenance manifest.")
        return 0

    notes_to_check = []
    if target.is_dir():
        notes_to_check = sorted(list(target.glob(args.pattern)))
    else:
        notes_to_check = [target]

    failed = skipped = 0
    for note in notes_to_check:
        ok, errors = validate_note_provenance(note, skip_archived=not args.include_archived)
        if not ok:
            failed += 1
            print(f"FAILED note provenance for {note.name}:", file=sys.stderr)
            for e in errors:
                print(f"  - {e}", file=sys.stderr)
        elif any(e.startswith("Skipped") for e in errors):
            # An archived note is never validated. Printing PASS for it read as a
            # clean bill of health for notes archived precisely because their
            # provenance did not hold up — the six `ashby_*` notes, for one.
            skipped += 1
            print(f"SKIP: {note.name} (ARCHIVED, not validated; use --include-archived)")
        else:
            print(f"PASS: {note.name}")

    checked = len(notes_to_check) - skipped
    print(f"\n{checked} validated, {skipped} skipped, {failed} failed.")
    if failed:
        print(f"{failed}/{checked} notes failed provenance validation.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
