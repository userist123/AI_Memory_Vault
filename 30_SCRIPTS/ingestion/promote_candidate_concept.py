#!/usr/bin/env python3
"""
promote_candidate_concept.py — Gated promotion of proposed candidate concepts to REVIEW memory notes.

Reads an approved 'proposed' candidate concept from an ontology slot file, applies
recorded human/agent judgment criteria, creates a canonical memory note conforming
strictly to schema.py (lifecycle: REVIEW), and updates the originating slot file row
in place with status=promoted and promoted_note_id.
"""

import os
import re
import glob
import json
import uuid
import argparse
import importlib
from datetime import datetime
from typing import Dict, Any, Tuple

import importlib.util

schema_path = os.path.abspath("03_IMPLEMENTATION/packages/lifecycle/validation/schema.py")
spec = importlib.util.spec_from_file_location("schema_mod", schema_path)
schema_mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(schema_mod)
validate_frontmatter = schema_mod.validate_frontmatter

SLOT_DIRECTORY = "01_ARCHITECTURE/ontology/slots"
KNOWLEDGE_DIRECTORY = "01_ARCHITECTURE/knowledge"


def find_slot_file(slot_name: str, slots_dir: str = SLOT_DIRECTORY) -> str:
    """
    Locates the markdown file corresponding to slot_name.
    """
    pattern = os.path.join(slots_dir, f"*_{slot_name}.md")
    matches = glob.glob(pattern)
    if not matches:
        raise FileNotFoundError(f"No slot file found for slot '{slot_name}' in {slots_dir}")
    return matches[0]


def slugify(text: str) -> str:
    cleaned = re.sub(r'[^\w\s\-]', '', text.lower())
    return re.sub(r'[\s\-]+', '_', cleaned).strip('_')


def promote_candidate_concept(
    concept_name: str,
    slot_name: str,
    rewritten_definition: str = None,
    slots_dir: str = SLOT_DIRECTORY,
    notes_dir: str = KNOWLEDGE_DIRECTORY,
    judgment_reasoning: str = ""
) -> Dict[str, Any]:
    """
    Promotes a 'proposed' candidate concept to a REVIEW memory note in the vault.
    """
    slot_file = find_slot_file(slot_name, slots_dir)
    with open(slot_file, "r", encoding="utf-8") as f:
        content = f.read()

    # Parse Candidate concepts table
    table_match = re.search(r'## Candidate concepts\s*\n(\|.*(?:\n\|.*)*)', content)
    if not table_match:
        raise ValueError(f"Could not locate Candidate concepts table in {slot_file}")

    lines = content.splitlines()
    target_row_idx = -1
    target_cols = []

    for i, line in enumerate(lines):
        if line.startswith("|"):
            cols = [c.strip() for c in line.split("|")]
            if len(cols) >= 6 and cols[1].lower() == concept_name.lower():
                target_row_idx = i
                target_cols = cols
                break

    if target_row_idx == -1:
        raise ValueError(f"Concept '{concept_name}' not found in slot file {slot_file}")

    current_status = target_cols[4]
    if current_status != "proposed":
        raise ValueError(f"Concept '{concept_name}' in {slot_file} has status '{current_status}', must be 'proposed' to promote.")

    source_book = target_cols[2]
    conf_val = target_cols[3]
    date_added = target_cols[5]

    note_uuid = str(uuid.uuid4())
    today_str = datetime.now().strftime("%Y-%m-%d")

    # Frontmatter matching canonical schema.py
    frontmatter_dict = {
        "id": note_uuid,
        "type": "knowledge",
        "lifecycle": "REVIEW",  # STRICT: NEVER ACTIVE
        "category": slot_name,
        "tags": ["ontology-promoted", slot_name],
        "created": today_str,
        "updated": today_str,
        "provenance": {
            "source_type": "import",
            "source_ref": source_book,
            "source_date": date_added if re.match(r'^\d{4}-\d{2}-\d{2}$', date_added) else today_str,
            "provenance_status": "complete",
            "redaction": "none"
        },
        "confidence": "high",
        "verification": "unverified",
        "relations": [
            {
                "relation": "derived_from",
                "target": os.path.relpath(slot_file, start=".").replace("\\", "/")
            }
        ]
    }

    # Validate against canonical schema
    validate_frontmatter(frontmatter_dict)

    # Prepare note content
    definition_text = rewritten_definition.strip() if rewritten_definition else f"{concept_name} is a load-bearing concept extracted from {source_book}."
    
    yaml_frontmatter = (
        "---\n"
        f"id: \"{frontmatter_dict['id']}\"\n"
        f"type: {frontmatter_dict['type']}\n"
        f"lifecycle: {frontmatter_dict['lifecycle']}\n"
        f"category: {frontmatter_dict['category']}\n"
        f"tags: [{', '.join(frontmatter_dict['tags'])}]\n"
        f"created: \"{frontmatter_dict['created']}\"\n"
        f"updated: \"{frontmatter_dict['updated']}\"\n"
        "provenance:\n"
        f"  source_type: {frontmatter_dict['provenance']['source_type']}\n"
        f"  source_ref: \"{frontmatter_dict['provenance']['source_ref']}\"\n"
        f"  source_date: \"{frontmatter_dict['provenance']['source_date']}\"\n"
        f"  provenance_status: {frontmatter_dict['provenance']['provenance_status']}\n"
        f"  redaction: {frontmatter_dict['provenance']['redaction']}\n"
        f"confidence: {frontmatter_dict['confidence']}\n"
        f"verification: {frontmatter_dict['verification']}\n"
        "relations:\n"
        f"  - relation: derived_from\n"
        f"    target: \"{frontmatter_dict['relations'][0]['target']}\"\n"
        "---\n\n"
    )

    note_body = (
        f"# {concept_name}\n\n"
        f"## Canonical Definition\n\n"
        f"{definition_text}\n\n"
        f"## Judgment & Evaluation\n\n"
        f"{judgment_reasoning or 'Evaluated as a load-bearing algorithmic procedure eligible for ontology promotion.'}\n\n"
        f"## Code Cross-References\n\n"
        f"Potentially relevant to implementation modules in `03_IMPLEMENTATION/` or procedures in `10_DOCUMENTATION/procedures/`, not independently verified this package.\n"
    )

    note_filename = f"Promoted_{slugify(concept_name)}.md"
    note_filepath = os.path.join(notes_dir, note_filename)
    os.makedirs(notes_dir, exist_ok=True)

    with open(note_filepath, "w", encoding="utf-8") as f:
        f.write(yaml_frontmatter + note_body)

    # Update slot file table row in place
    # Format: | concept | source_book | confidence | status | date_added | promoted_note_id |
    new_row = f"| {target_cols[1]} | {target_cols[2]} | {target_cols[3]} | promoted | {target_cols[5]} | {note_uuid} |"
    lines[target_row_idx] = new_row

    with open(slot_file, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")

    return {
        "concept_name": concept_name,
        "slot_name": slot_name,
        "promoted_note_id": note_uuid,
        "note_filepath": note_filepath,
        "status": "promoted",
        "judgment_reasoning": judgment_reasoning
    }


def main():
    parser = argparse.ArgumentParser(description="Promote an approved proposed candidate concept to a REVIEW memory note.")
    parser.add_argument("--concept-name", required=True, help="Concept name")
    parser.add_argument("--slot-name", required=True, help="Canonical slot name")
    parser.add_argument("--rewritten-definition", help="Optional rewritten definition string")
    parser.add_argument("--reasoning", help="Judgment reasoning for promotion")

    args = parser.parse_args()
    res = promote_candidate_concept(
        args.concept_name,
        args.slot_name,
        rewritten_definition=args.rewritten_definition,
        judgment_reasoning=args.reasoning
    )
    print(json.dumps(res, indent=2))


if __name__ == "__main__":
    main()
