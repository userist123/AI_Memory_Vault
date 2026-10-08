"""B06: generate the human labelling packet (items that need human-attested ground truth).

Writes, under 08_RESEARCH/BOOK_TO_MEMORY/b06_labelling_packet/:
    labels_to_fill.csv        one row per decision; the owner fills `label` (and optionally the rest)
    items_readable.md         the same items as a read-only document
    OWNER_INSTRUCTIONS.md     what to do and how the labels are used
    manifest.json             counts and hashes

Items: (1) every gold note of every H1 case ("does this note answer the query?") and (2) every candidate note of the B03
packet ("is the definition faithful to its quoted passage?"). No label is ever pre-filled.

    python 30_SCRIPTS/evaluation/generate_b06_labelling_packet.py [--check]
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Dict, List

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

from lifecycle.validation.book_to_memory_human_labels import (  # noqa: E402
    ALLOWED,
    MIN_LABELS,
    TYPE_H1,
    TYPE_NOTE,
    build_rows,
    rows_to_csv,
    rows_to_markdown,
)

TRACK = REPO / "08_RESEARCH" / "BOOK_TO_MEMORY"
OUT = TRACK / "b06_labelling_packet"


def note_confidence(tasks: List[dict]) -> Dict[str, str]:
    out = {}
    for t in tasks:
        m = re.search(r"^confidence:\s*(\S+)", (REPO / t["note_path"]).read_text(encoding="utf-8"), re.M)
        out[t["note_id"]] = m.group(1) if m else "unknown"
    return out


def build() -> Dict[str, str]:
    cases = json.loads((TRACK / "h1_cases.json").read_text(encoding="utf-8"))
    corpus = json.loads((TRACK / "current_labeling_corpus.json").read_text(encoding="utf-8"))
    tasks = json.loads((TRACK / "b03_task_packet" / "scoring" / "tasks.json").read_text(encoding="utf-8"))["tasks"]
    rows = build_rows(cases, corpus, tasks, note_confidence(tasks))
    csv_text = rows_to_csv(rows)
    n_h1 = sum(r["item_type"] == TYPE_H1 for r in rows)
    n_note = sum(r["item_type"] == TYPE_NOTE for r in rows)
    p1 = sum(r["priority"] == "1" for r in rows)
    manifest = {
        "rows": len(rows), TYPE_H1: n_h1, TYPE_NOTE: n_note, "priority_1_rows": p1,
        "min_labels_per_metric": MIN_LABELS, "allowed": {k: list(v) for k, v in ALLOWED.items()},
        "csv_sha256": hashlib.sha256(csv_text.encode("utf-8")).hexdigest(),
        "h1_cases_sha256": hashlib.sha256((TRACK / "h1_cases.json").read_bytes()).hexdigest(),
    }
    instructions = f"""# B06 — instructions for the owner

**Status of B06: waiting on owner labels.** The metrics of the Book-to-Memory track were calibrated on a machine-made
corpus; nothing here has been checked by a person. This packet lists what only a person can decide. No label is filled in.

## What to do

1. Open `labels_to_fill.csv` (UTF-8, comma-separated) in a spreadsheet. `items_readable.md` shows the same items as text.
2. For each row you can judge, fill:
   * `label`: one of the values in `allowed_values` of that row (lower case).
   * `labeler_id`: your short id, the same on every row you fill (required for a labelled row).
   * `other_relevant_ids` (H1 rows only, optional): ids of other vault notes that answer the query better or equally.
   * `comment` (optional).
   Leave `label` blank when you cannot judge; blank rows are counted as unlabelled and nothing is assumed for them.
3. Do not edit any other column and do not reorder or delete rows.
4. Save as `labels_<your id>.csv` and run:

   `python 30_SCRIPTS/evaluation/b2m_ingest_labels.py --labels labels_<your id>.csv`

   Several labelers each fill their own copy; pass all files and the report adds the agreement between them.

## What is asked

| Rows | Count | Question | Allowed labels |
|---|---:|---|---|
| H1 gold relevance (`{TYPE_H1}`) | {n_h1} | Does the gold note answer the query? | `yes`, `partial`, `no` |
| Candidate-note faithfulness (`{TYPE_NOTE}`) | {n_note} | Is the definition faithful to its quoted source passage? | `faithful`, `partial`, `unfaithful`, `not_a_term` |

`priority` 1 ({p1} rows) is the held-out H1 cases and every candidate note: if you only have time for part of the packet, do those.
A rate is reported only for a type with at least {MIN_LABELS} labelled items; below that the report says `INSUFFICIENT_LABELS` and gives no rate.

## What the labels are used for

* The share of H1 gold notes a person judges relevant (with a Wilson 95% interval), and the held-out baseline recall@10
  recomputed over the cases whose gold a person confirmed.
* The share of candidate notes whose definition is faithful, overall and by the confidence the note claims about itself
  (all the notes claim `high`), which tests whether that claim is calibrated.
* With two or more labelers: Cohen's kappa and Krippendorff's alpha between them. Items on which labelers disagree are
  listed as disputed and left out of the rates, never settled by a vote.

Nothing in this packet promotes a note or changes a lifecycle state; a label is evidence for the calibration report only.
"""
    return {"labels_to_fill.csv": csv_text, "items_readable.md": rows_to_markdown(rows) + "\n",
            "OWNER_INSTRUCTIONS.md": instructions, "manifest.json": json.dumps(manifest, indent=2) + "\n"}


def main(argv: List[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--check", action="store_true", help="exit 1 if the committed packet differs from a fresh build")
    args = ap.parse_args(argv)
    files = build()
    if args.check:
        for name, text in files.items():
            if not (OUT / name).exists() or (OUT / name).read_text(encoding="utf-8") != text:
                print(f"DIFFERS: {name}")
                return 1
        print("packet is up to date")
        return 0
    OUT.mkdir(parents=True, exist_ok=True)
    for name, text in files.items():
        (OUT / name).write_text(text, encoding="utf-8", newline="\n")
    m = json.loads(files["manifest.json"])
    print(f"wrote {OUT}: {m['rows']} rows ({m[TYPE_H1]} H1 gold, {m[TYPE_NOTE]} candidate notes), {m['priority_1_rows']} priority 1")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
