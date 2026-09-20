"""30_SCRIPTS/evaluation/build_remaining_typed_edge_audit.py — Package Remaining 65 Typed Edges for Audit.

Extracts all remaining unjudged typed relations from the live graph:
- Initial population: 114 declared typed relations.
- Judged in audit_verdicts_declared.json: 49 relations (20 accepted, 29 rejected & purged).
  (Note: Index 50 from the previous 50-sample had no verdict, so it is included).
- Remaining unjudged: 65 relations.

Outputs:
1. 07_EVALUATION/edge_audit_v2_remaining/audit_sample_declared_remaining_65.json
2. 07_EVALUATION/edge_audit_v2_remaining/audit_sample_declared_remaining_65.json.sha256
3. 07_EVALUATION/edge_audit_v2_remaining/batches/batch_01.json .. batch_07.json (<= 20 KB each)
4. 07_EVALUATION/edge_audit_v2_remaining/LABELING_PROMPT.md (copy-paste ready for LLM chat)
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import sys
from pathlib import Path
from typing import Any, Dict, List, Set, Tuple

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 40)

from graph.synapse_store import SynapseStore
from retrieval.vault_index import VaultIndex

EXCERPT_CHARS = 700
BATCH_SIZE = 10
DEFAULT_OUT = REPO / "07_EVALUATION" / "edge_audit_v2_remaining"
AUDIT_V2_SAMPLE = REPO / "07_EVALUATION" / "edge_audit_v2" / "audit_sample_declared_50.json"
AUDIT_V2_VERDICTS = REPO / "07_EVALUATION" / "edge_audit_v2" / "audit_verdicts_declared.json"


def get_judged_edge_keys(sample_path: Path, verdicts_path: Path) -> Set[Tuple[str, str, str]]:
    """Returns the set of (source_id, target_id, relation) for all rows with an existing verdict."""
    sample_data = json.loads(sample_path.read_text(encoding="utf-8"))
    samples = {s["index"]: s for s in sample_data.get("samples", [])}

    verdicts_data = json.loads(verdicts_path.read_text(encoding="utf-8"))
    verdicts = verdicts_data.get("verdicts", [])

    judged_keys = set()
    for v in verdicts:
        idx = v.get("index")
        if idx in samples:
            s = samples[idx]
            judged_keys.add((str(s["source_id"]), str(s["target_id"]), str(s["relation"]).lower()))
    return judged_keys


def extract_remaining_edges(index: VaultIndex, store: SynapseStore, judged_keys: Set[Tuple[str, str, str]]) -> List[Dict[str, Any]]:
    """Extracts all live declared typed edges not present in judged_keys."""
    notes_by_id = {n.id: n for n in index.notes}

    # Gather live typed declared edges
    candidates = []
    seen = set()
    for syn in store.all():
        if syn.origin != "declared" or syn.relation == "related_to":
            continue
        key = (syn.source_id, syn.target_id, syn.relation)
        if key in seen or key in judged_keys:
            continue
        seen.add(key)
        candidates.append(syn)

    # Sort deterministically
    candidates.sort(key=lambda s: (s.relation, s.source_id, s.target_id))

    remaining = []
    for idx, syn in enumerate(candidates, 1):
        source_note = notes_by_id.get(syn.source_id)
        target_note = notes_by_id.get(syn.target_id)

        source_excerpt = (source_note.text.strip()[:EXCERPT_CHARS] if source_note else "")
        target_excerpt = (target_note.text.strip()[:EXCERPT_CHARS] if target_note else "")

        item = {
            "index": idx,
            "source_id": syn.source_id,
            "target_id": syn.target_id,
            "relation": syn.relation,
            "origin": syn.origin,
            "weight": syn.weight,
            "source_title": getattr(source_note, "title", None) or syn.source_id,
            "source_path": str(getattr(source_note, "path", "")) if source_note else "",
            "source_lifecycle": getattr(source_note, "lifecycle", None),
            "source_excerpt": source_excerpt,
            "target_title": getattr(target_note, "title", None) or syn.target_id,
            "target_path": str(getattr(target_note, "path", "")) if target_note else "",
            "target_lifecycle": getattr(target_note, "lifecycle", None),
            "target_excerpt": target_excerpt,
        }
        remaining.append(item)

    return remaining


def generate_labeling_prompt(first_batch: List[Dict[str, Any]], total_remaining: int, total_batches: int) -> str:
    """Generates a structured, self-contained evaluation prompt ready for copy-pasting to an LLM."""
    batch_json = json.dumps(first_batch, ensure_ascii=False, indent=2)

    prompt = f"""# Prompt de Audit și Etichetare: Relații Tipizate de Graf (Wave C — Tranșa 1/{total_batches})

Ești un evaluator de cunoștințe și arhitect de grafuri semantice.
Sarcina ta este să auditezi o tranșă de relații tipizate declarate (`part_of`, `depends_on`, `applies_to`, `caused`, `contradicts`, `supersedes`) extrase din graful viu al depozitului `AI_Memory_Vault`.

---

## 1. Reguli Semantice de Judecată

O relație între o notă-Sursă (`source`) și o notă-Țintă (`target`) trebuie judecată strict pe baza conținutului real al notelor (vezi `source_excerpt` și `target_excerpt`):

1. **`part_of` (meronimie / incluziune)**:
   - Sursa este o sub-componentă structurală, un modul sau un sub-proces aparținând țintei.
   - REJECT dacă:
     - Este doar un termen generic sau asociat tematic (`wrong_type` sau `unrelated`).
     - Sursa este doar un document de audit sau plan și ținta e un slot ontologic (`wrong_type`).
     - Relația e inversă (ținta e parte din sursă) (`wrong_direction`).
2. **`depends_on` (dependență ontologică sau execuțională)**:
   - Sursa nu poate funcționa, nu poate fi înțeleasă sau nu se poate executa fără concepte/mecanisme definite specific în țintă.
   - REJECT dacă sunt doar descrieri paralele sau concepte complementare fără dependență strictă (`unrelated` sau `wrong_direction`).
3. **`applies_to` (aplicabilitate / domeniu)**:
   - Mecanismul, regula sau modelul din sursă se aplică activ asupra domeniului descris în țintă.
   - REJECT dacă sunt două definiții paralele sau dacă ținta nu este deloc guvernată de sursă (`wrong_type` sau `unsupported`).

---

## 2. Categorii de Respingere Permise

Dacă decizia este `REJECT`, trebuie să atribui exact una din următoarele 5 categorii:
- `wrong_type`: Relația semantică există, dar tipul ales este greșit (ex: e asociere liberă sau compoziție, nu incluziune `part_of`).
- `wrong_direction`: Direcția relației este inversată (ținta depinde/se aplică sursei, nu invers).
- `unsupported`: Textul notei sursă nu menționează și nu susține mecanismul țintei.
- `unrelated`: Conceptele sunt complet paralele sau nelegate structural.
- `shared_terms_only`: Singura legătură este că ambele note folosesc cuvinte comune (ex: cuvântul "memory").

---

## 3. Formatul de Răspuns Așteptat

Te rog să răspunzi **EXCLUSIV** cu un bloc JSON valid conform următoarei scheme:

```json
{{
  "batch_id": "batch_01",
  "evaluated_count": {len(first_batch)},
  "verdicts": [
    {{
      "index": 1,
      "relation": "tip_relatie",
      "verdict": "ACCEPT",
      "category": null,
      "rationale": "Sursa descrie o componentă structurală direct inclusă în arhitectura țintei."
    }},
    {{
      "index": 2,
      "relation": "tip_relatie",
      "verdict": "REJECT",
      "category": "wrong_type",
      "rationale": "Sursa este un concept paralel; nu există incluziune part_of."
    }}
  ]
}}
```

---

## 4. Datele Tranșei 1 (10 cazuri de evaluat)

```json
{batch_json}
```
"""
    return prompt


def main() -> int:
    parser = argparse.ArgumentParser(description="Build remaining typed edge audit packet")
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT)
    parser.add_argument("--batch-size", type=int, default=BATCH_SIZE)
    args = parser.parse_args()

    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    judged_keys = get_judged_edge_keys(AUDIT_V2_SAMPLE, AUDIT_V2_VERDICTS)
    print(f"Loaded {len(judged_keys)} judged edges from audit v2 (to exclude).")

    index = VaultIndex.load(REPO, include_raw=True, include_archived=True)
    store = SynapseStore.from_index(index)

    remaining_edges = extract_remaining_edges(index, store, judged_keys)
    total_remaining = len(remaining_edges)
    print(f"Extracted {total_remaining} remaining unjudged typed edges (expected: 65).")

    if total_remaining != 65:
        print(f"WARNING: Expected exactly 65 remaining edges, got {total_remaining}")

    # Output directory setup
    args.out.mkdir(parents=True, exist_ok=True)
    batches_dir = args.out / "batches"
    batches_dir.mkdir(parents=True, exist_ok=True)

    # 1. Full aggregate file
    aggregate_payload = {
        "schema": "declared-edge-audit-sample-remaining.v1",
        "population": "remaining unjudged declared typed relations (relation != related_to)",
        "population_size": total_remaining,
        "sample_size": total_remaining,
        "excerpt_chars": EXCERPT_CHARS,
        "samples": remaining_edges,
    }
    agg_file = args.out / "audit_sample_declared_remaining_65.json"
    agg_text = json.dumps(aggregate_payload, ensure_ascii=False, indent=2) + "\n"
    # Normalize LF for hashing
    agg_normalized_bytes = agg_text.replace("\r\n", "\n").encode("utf-8")
    agg_file.write_bytes(agg_normalized_bytes)
    digest = hashlib.sha256(agg_normalized_bytes).hexdigest()

    sha_file = args.out / "audit_sample_declared_remaining_65.json.sha256"
    sha_file.write_text(f"{digest}  audit_sample_declared_remaining_65.json\n", encoding="utf-8", newline="\n")
    print(f"Full aggregate saved to: {agg_file} ({len(agg_normalized_bytes) / 1024:.1f} KB)")
    print(f"SHA-256: {digest}")

    # 2. Split into batches
    num_batches = math.ceil(total_remaining / args.batch_size)
    batches = []
    for b_idx in range(num_batches):
        start_i = b_idx * args.batch_size
        end_i = min(start_i + args.batch_size, total_remaining)
        batch_items = remaining_edges[start_i:end_i]
        batches.append(batch_items)

        batch_name = f"batch_{b_idx + 1:02d}.json"
        batch_path = batches_dir / batch_name
        batch_payload = {
            "batch_id": f"batch_{b_idx + 1:02d}",
            "batch_index": b_idx + 1,
            "total_batches": num_batches,
            "batch_size": len(batch_items),
            "aggregate_sha256": digest,
            "samples": batch_items,
        }
        b_text = json.dumps(batch_payload, ensure_ascii=False, indent=2) + "\n"
        b_bytes = b_text.replace("\r\n", "\n").encode("utf-8")
        batch_path.write_bytes(b_bytes)
        print(f"- Batch {b_idx + 1:02d}: {len(batch_items)} items, {len(b_bytes) / 1024:.1f} KB -> {batch_path.name}")

    # 3. Labeling Prompt
    labeling_prompt = generate_labeling_prompt(batches[0], total_remaining, num_batches)
    prompt_file = args.out / "LABELING_PROMPT.md"
    prompt_file.write_text(labeling_prompt, encoding="utf-8", newline="\n")
    print(f"Labeling prompt saved to: {prompt_file}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
