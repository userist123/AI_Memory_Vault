#!/usr/bin/env python3
"""Validator for Curriculum Module Profiles.

Validates curriculum profile JSON documents against:
07_EVALUATION/curriculum/schema/curriculum_module.schema.json
and enforces domain business rules (e.g. retroactive note requirement,
referenced file existence).
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, List, Tuple, Union

try:
    import jsonschema
except ImportError:
    jsonschema = None

REPO_ROOT = Path(__file__).resolve().parents[2]
SCHEMA_PATH = REPO_ROOT / "07_EVALUATION" / "curriculum" / "schema" / "curriculum_module.schema.json"


def load_schema() -> Dict[str, Any]:
    if not SCHEMA_PATH.exists():
        raise FileNotFoundError(f"Schema not found at {SCHEMA_PATH}")
    return json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))


def validate_profile_dict(
    profile_data: Dict[str, Any],
    check_referenced_files: bool = False,
    repo_root: Path = REPO_ROOT,
) -> Tuple[bool, List[str]]:
    errors: List[str] = []

    # 1. JSON Schema validation
    schema = load_schema()
    if jsonschema is not None:
        validator = jsonschema.Draft7Validator(schema)
        for err in validator.iter_errors(profile_data):
            path_str = ".".join(str(p) for p in err.path) if err.path else "root"
            errors.append(f"Schema error at '{path_str}': {err.message}")
    else:
        # Fallback basic key check if jsonschema is not installed
        for req in schema.get("required", []):
            if req not in profile_data:
                errors.append(f"Missing required field: '{req}'")

    # 2. Retroactive profile business rule
    if profile_data.get("retroactive") is True:
        note = profile_data.get("retroactive_note", "").strip()
        if not note:
            errors.append("Profile marked 'retroactive: true' must include non-empty 'retroactive_note'")

    # 3. Referenced files verification (optional deep check)
    if check_referenced_files:
        src = profile_data.get("source", {})
        prov_manifest = src.get("provenance_manifest")
        if prov_manifest:
            mpath = repo_root / prov_manifest
            if not mpath.exists():
                errors.append(f"Referenced provenance manifest not found: {prov_manifest}")

        eval_cfg = profile_data.get("evaluation", {})
        frozen_test = eval_cfg.get("frozen_test_set")
        if frozen_test:
            fpath = repo_root / frozen_test
            if not fpath.exists():
                errors.append(f"Referenced frozen test set not found: {frozen_test}")

    return len(errors) == 0, errors


def validate_curriculum_profile(
    profile_path: Union[str, Path],
    check_referenced_files: bool = False,
) -> Tuple[bool, List[str]]:
    path = Path(profile_path)
    if not path.exists():
        return False, [f"Profile file not found: {path}"]
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except Exception as exc:
        return False, [f"Failed to parse profile JSON: {exc}"]

    return validate_profile_dict(data, check_referenced_files=check_referenced_files)


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate curriculum module profile JSON.")
    parser.add_argument("profile_path", type=Path, help="Path to profile JSON file")
    parser.add_argument(
        "--check-files",
        action="store_true",
        help="Check that referenced files (manifest, frozen test set) exist on disk",
    )
    args = parser.parse_args()

    ok, errors = validate_curriculum_profile(args.profile_path, check_referenced_files=args.check_files)
    if not ok:
        print(f"FAILED validation for {args.profile_path}:", file=sys.stderr)
        for err in errors:
            print(f"  - {err}", file=sys.stderr)
        return 1

    print(f"PASS: {args.profile_path} is a valid curriculum module profile.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
