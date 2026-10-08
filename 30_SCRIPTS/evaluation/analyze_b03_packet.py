"""B03: analyse an executed task packet with the pre-registered paired test.

Usage:
    python 30_SCRIPTS/evaluation/analyze_b03_packet.py --packet DIR --scores scores.json [--out result.json]
    python 30_SCRIPTS/evaluation/analyze_b03_packet.py --packet DIR --auto-score      # exploratory coverage metric

`scores.json` is the output of `b2m_blind_rating_packet.py score` (blind multi-rater scores plus agreement).
The run must carry its `run_config.json` (the packet's template with `model_id` filled in): without it the run is
refused as not comparable. With fewer complete pairs than the pre-registered minimum the analysis prints
INSUFFICIENT_DATA and reports no effect; the exit status is then 2.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import List

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

from lifecycle.validation.book_to_memory_real_ablation import (  # noqa: E402
    MIN_PAIRS,
    AblationHarnessError,
    analyze,
    coverage_score,
    read_external_run,
)


def main(argv: List[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--packet", required=True)
    ap.add_argument("--answers-dir")
    ap.add_argument("--scores", help="scores.json from the blind-rating `score` command")
    ap.add_argument("--auto-score", action="store_true", help="use the automatic coverage metric (exploratory only)")
    ap.add_argument("--min-pairs", type=int, default=MIN_PAIRS)
    ap.add_argument("--out")
    args = ap.parse_args(argv)
    if bool(args.scores) == bool(args.auto_score):
        raise SystemExit("give exactly one of --scores and --auto-score")

    packet = Path(args.packet)
    try:
        cfg, answers = read_external_run(packet, Path(args.answers_dir) if args.answers_dir else None)
    except (AblationHarnessError, FileNotFoundError) as exc:
        print(json.dumps({"status": "INSUFFICIENT_DATA", "reason": str(exc)}, indent=2))
        return 2
    trials = json.loads((packet / "trials.json").read_text(encoding="utf-8"))["trials"]
    tasks = {t["task_id"]: t for t in json.loads((packet / "scoring" / "tasks.json").read_text(encoding="utf-8"))["tasks"]}
    alpha = None
    if args.scores:
        doc = json.loads(Path(args.scores).read_text(encoding="utf-8"))
        scores = doc.get("scores_by_trial") or {}
        alpha = (doc.get("aggregate", {}).get("agreement") or {}).get("krippendorff_alpha_interval")
    else:
        scores = {tid: 10.0 * coverage_score(ans, tasks[next(t["task_id"] for t in trials if t["trial_id"] == tid)]["source_passage"])
                  for tid, ans in answers.items()}
    manifest = json.loads((packet / "manifest.json").read_text(encoding="utf-8"))
    result = analyze(scores, trials, cfg, min_pairs=args.min_pairs, reliability_alpha=alpha,
                     preregistration_sha256=manifest["preregistration_sha256"])
    result["metric"] = "blind multi-rater rubric total (0-10)" if args.scores else "automatic coverage x10 (EXPLORATORY, not the primary metric)"
    text = json.dumps(result, ensure_ascii=False, indent=2)
    if args.out:
        Path(args.out).write_text(text + "\n", encoding="utf-8")
    print(text)
    return 0 if result["status"] == "COMPLETE" else 2


if __name__ == "__main__":
    raise SystemExit(main())
