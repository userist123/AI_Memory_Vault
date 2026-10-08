"""B06: ingest completed human labels and calibrate the track's metrics against them.

    python 30_SCRIPTS/evaluation/b2m_ingest_labels.py --labels labels_owner.csv [labels_second.csv ...] [--out-dir DIR]
    python 30_SCRIPTS/evaluation/b2m_ingest_labels.py                      # no labels: writes the WAITING_ON_OWNER_LABELS report

Writes CALIBRATION_REPORT.json / .md to --out-dir (default 07_EVALUATION/b2m_calibration/). Exit status: 0 when calibrated,
2 when labels are missing or too few for a metric. Nothing is ever filled in on the owner's behalf.
"""
from __future__ import annotations

import argparse
import csv
import io
import json
import sys
from pathlib import Path
from typing import List

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

from lifecycle.validation.book_to_memory_human_labels import (  # noqa: E402
    STATUS_CALIBRATED,
    calibrate,
    read_labels,
    render_report_markdown,
)

PACKET = REPO / "08_RESEARCH" / "BOOK_TO_MEMORY" / "b06_labelling_packet" / "labels_to_fill.csv"
BASELINE = REPO / "08_RESEARCH" / "BOOK_TO_MEMORY" / "h1_baseline_results.json"
TASKS = REPO / "08_RESEARCH" / "BOOK_TO_MEMORY" / "b03_task_packet" / "scoring" / "tasks.json"
DEFAULT_OUT = REPO / "07_EVALUATION" / "b2m_calibration"


def _confidence(tasks: List[dict]) -> dict:
    import re
    out = {}
    for t in tasks:
        m = re.search(r"^confidence:\s*(\S+)", (REPO / t["note_path"]).read_text(encoding="utf-8"), re.M)
        out[t["note_id"]] = m.group(1) if m else "unknown"
    return out


def main(argv: List[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--labels", nargs="*", default=[])
    ap.add_argument("--packet", default=str(PACKET))
    ap.add_argument("--out-dir", default=str(DEFAULT_OUT))
    args = ap.parse_args(argv)

    packet_text = Path(args.packet).read_text(encoding="utf-8")
    packet_rows = list(csv.DictReader(io.StringIO(packet_text)))
    packet_ids = {r["item_id"]: r["item_type"] for r in packet_rows}
    label_sets = [read_labels(Path(f).read_text(encoding="utf-8"), packet_ids) for f in args.labels]
    baseline = json.loads(BASELINE.read_text(encoding="utf-8"))["cases"] if BASELINE.exists() else None
    tasks = json.loads(TASKS.read_text(encoding="utf-8"))["tasks"]
    report = calibrate(packet_rows, label_sets, baseline_cases=baseline, note_confidence=_confidence(tasks))
    out = Path(args.out_dir)
    out.mkdir(parents=True, exist_ok=True)
    (out / "CALIBRATION_REPORT.json").write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    (out / "CALIBRATION_REPORT.md").write_text(render_report_markdown(report), encoding="utf-8")
    print(f"status: {report['status']}; labelled {report['labelled_items']}/{report['items_in_packet']}; wrote {out}")
    return 0 if report["status"] == STATUS_CALIBRATED else 2


if __name__ == "__main__":
    raise SystemExit(main())
