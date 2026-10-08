"""B05: build a blind multi-rater packet from answers, and aggregate the completed ratings.

Build (items shuffled, condition labels hidden, unblinding key written OUTSIDE the packet):
    python 30_SCRIPTS/evaluation/b2m_blind_rating_packet.py build \
        --b03-packet 08_RESEARCH/BOOK_TO_MEMORY/b03_task_packet --out RATING_PACKET_DIR --key KEY.json \
        [--answers-dir DIR] [--raters 2] [--seed 20261008]
    python 30_SCRIPTS/evaluation/b2m_blind_rating_packet.py build --items-json ITEMS.json --out DIR --key KEY.json
        (ITEMS.json: a list of {trial_id, condition, question, answer, [task_id], [reference]})

Score (completed rating files in, agreement statistics and per-trial scores out):
    python 30_SCRIPTS/evaluation/b2m_blind_rating_packet.py score \
        --ratings RATER-1.jsonl RATER-2.jsonl --key KEY.json --out SCORES.json

The scores file is the input of `analyze_b03_packet.py`. With fewer than 2 independent rater sources the score
command writes status INSUFFICIENT_RATERS and exits 2: nothing is aggregated from one rater.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import List

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

from lifecycle.validation.book_to_memory_blind_packet import (  # noqa: E402
    build_blind_packet,
    items_from_b03_packet,
    scores_by_trial,
)
from lifecycle.validation.book_to_memory_raters import aggregate_ratings, load_ratings  # noqa: E402

DEFAULT_SEED = 20261008


def cmd_build(args: argparse.Namespace) -> int:
    if bool(args.b03_packet) == bool(args.items_json):
        raise SystemExit("give exactly one of --b03-packet and --items-json")
    if args.b03_packet:
        trials = items_from_b03_packet(Path(args.b03_packet), Path(args.answers_dir) if args.answers_dir else None)
    else:
        trials = json.loads(Path(args.items_json).read_text(encoding="utf-8"))
    manifest = build_blind_packet(trials, Path(args.out), Path(args.key), n_raters=args.raters, seed=args.seed)
    print(json.dumps(manifest, indent=2))
    if manifest["missing_answers"]:
        print(f"WARNING: {len(manifest['missing_answers'])} trial(s) had no answer and were not packaged", file=sys.stderr)
    return 0


def cmd_score(args: argparse.Namespace) -> int:
    records = []
    for f in args.ratings:
        records.extend(load_ratings(Path(f).read_text(encoding="utf-8").splitlines()))
    agg = aggregate_ratings(records, min_sources=2, require_blind=True, numeric=True)
    out = {"aggregate": agg}
    status = agg["status"]
    if status == "OK":
        out["scores_by_trial"] = scores_by_trial(agg, Path(args.key))
    Path(args.out).write_text(json.dumps(out, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"status": status, "raters": agg["raters"], "agreement": agg.get("agreement")}, indent=2))
    return 0 if status == "OK" else 2


def main(argv: List[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    b = sub.add_parser("build", help="build the blind packet and the unblinding key")
    b.add_argument("--b03-packet")
    b.add_argument("--answers-dir")
    b.add_argument("--items-json")
    b.add_argument("--out", required=True)
    b.add_argument("--key", required=True)
    b.add_argument("--raters", type=int, default=2)
    b.add_argument("--seed", type=int, default=DEFAULT_SEED)
    b.set_defaults(func=cmd_build)
    s = sub.add_parser("score", help="aggregate completed ratings")
    s.add_argument("--ratings", nargs="+", required=True)
    s.add_argument("--key", required=True)
    s.add_argument("--out", required=True)
    s.set_defaults(func=cmd_score)
    args = ap.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
