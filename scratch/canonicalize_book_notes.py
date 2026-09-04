import sys
sys.path.insert(0, ".")
import uuid
import glob
import os
from memory_controller.storage.serializer import deserialize, serialize
from memory_controller.validation.schema import validate_frontmatter

files = glob.glob("01_KNOWLEDGE/BOOKS/*.md")
print(f"Processing {len(files)} files...")

for f in files:
    with open(f, "r", encoding="utf-8") as fp:
        raw_text = fp.read()
    data = deserialize(raw_text)
    base = os.path.basename(f)
    
    # Ensure canonical schema
    data["id"] = str(uuid.uuid5(uuid.NAMESPACE_DNS, f"vault.knowledge.books.{base}"))
    data["created"] = "2026-09-04"
    data["updated"] = "2026-09-04"
    
    # Canonical relations
    rel_objs = []
    for r in data.get("relations", []):
        if isinstance(r, str):
            rel_objs.append({"relation": "references", "target": r})
        elif isinstance(r, dict) and "relation" in r and "target" in r:
            rel_objs.append({"relation": r["relation"], "target": r["target"]})
    data["relations"] = rel_objs
    
    # Allowed frontmatter keys
    allowed_fm = {
        "id", "type", "lifecycle", "category", "tags", "created", "updated",
        "provenance", "confidence", "verification", "relations"
    }
    fm = {k: v for k, v in data.items() if k in allowed_fm}
    
    # Validate frontmatter dictionary against canonical JSON Schema
    validate_frontmatter(fm)
    
    # Re-attach content for serialization
    to_serialize = dict(fm)
    to_serialize["content"] = data.get("content", "")
    
    # Write back
    with open(f, "w", encoding="utf-8") as out_f:
        out_f.write(serialize(to_serialize))
    print(f"VALIDATED & SAVED: {base} -> ID: {fm['id']}")

print("\nAll 6 files validated against canonical schema and saved successfully!")
