"""Corpus packet for an independent labeller: the notes search can return, with their links.

Excludes ARCHIVED notes (the 552 templated lessons retired as noise) and notes
without an id, which the controller cannot return. No note body is sent whole:
an excerpt is enough to write a question and judge relevance.
"""
import json
import re
import sys
from pathlib import Path

from graph.synapse_store import SynapseStore
from retrieval.vault_index import VaultIndex

OUT = Path(sys.argv[1])
COMMIT = sys.argv[2]
EXCERPT = 700

idx = VaultIndex.load(Path("."), include_raw=True, include_archived=False)
store = SynapseStore.from_index(idx)


def clean(v):
    return v.value if hasattr(v, "value") else (str(v) if v is not None else "")


def excerpt(body: str) -> str:
    text = re.sub(r"```.*?```", " ", body or "", flags=re.S)
    text = re.sub(r"!\[[^\]]*\]\([^)]*\)", " ", text)
    text = re.sub(r"\[\[([^\]|]+)(\|[^\]]+)?\]\]", r"\1", text)
    text = re.sub(r"[#>*_`|]+", " ", text)
    text = re.sub(r"\s+", " ", text).strip()
    return text[:EXCERPT]


notes = []
ids = set()
for n in idx.notes:
    if not n.id or clean(n.lifecycle) == "ARCHIVED":
        continue
    ids.add(n.id)
    notes.append({
        "id": n.id,
        "title": (n.title or Path(str(n.path)).stem)[:160],
        "type": clean(n.type),
        "lifecycle": clean(n.lifecycle),
        "path": Path(str(n.path)).as_posix(),
        "excerpt": excerpt(n.body),
    })

links = [
    {"source": s.source_id, "target": s.target_id, "relation": clean(s.relation), "origin": clean(s.origin)}
    for s in store.all() if s.source_id in ids and s.target_id in ids and s.source_id != s.target_id
]

OUT.write_text(json.dumps({
    "vault_commit": COMMIT,
    "note_count": len(notes),
    "link_count": len(links),
    "notes": notes,
    "links": links,
}, ensure_ascii=False, indent=1), encoding="utf-8")
print(f"notes={len(notes)} links={len(links)} bytes={OUT.stat().st_size}")
