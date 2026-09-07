"""WP-2 (r024) — draw an independent random sample for hand review.

r007's contract requires >=50, hand-verified, against a 70% bar, before bulk
promotion. r013 used seeds 42 and 7 while TUNING the thresholds, and seed
2026 for its own final (author-judged) figure. This draws a sample with a
seed none of those three passes used, so this review is not just a repeat of
a sample someone has already looked at.

SEED = 8675309 (arbitrary, chosen now, stated here, not searched for a
favourable draw).

Run: python 07_EVALUATION/r024_wp2_precision/sample_for_review.py
Output: 07_EVALUATION/r024_wp2_precision/review_worksheet.json
"""
from __future__ import annotations

import json
import random
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
SEED = 8675309
SAMPLE_SIZE = 55  # >= 50 required, +5 margin


def read_snippet(rel_path: str, max_chars: int = 1200) -> str:
    p = REPO / rel_path
    try:
        text = p.read_text(encoding="utf-8", errors="replace")
    except OSError as exc:
        return f"<<UNREADABLE: {exc}>>"
    return text[:max_chars]


def main() -> int:
    data = json.loads((HERE / "fresh_proposals.json").read_text(encoding="utf-8"))
    proposals = data["proposals"]
    print(f"total proposals available: {len(proposals)}")

    rng = random.Random(SEED)
    sample_size = min(SAMPLE_SIZE, len(proposals))
    indices = rng.sample(range(len(proposals)), sample_size)

    worksheet = []
    for i, idx in enumerate(sorted(indices), start=1):
        p = proposals[idx]
        worksheet.append({
            "review_id": i,
            "proposal_index": idx,
            "source_id": p["source_id"],
            "target_id": p["target_id"],
            "relation": p["relation"],
            "origin": p["origin"],
            "confidence": p["confidence"],
            "evidence_entities": p["evidence_entities"],
            "source_path": p["source_path"],
            "target_path": p["target_path"],
            "source_snippet": read_snippet(p["source_path"]),
            "target_snippet": read_snippet(p["target_path"]),
            "judgement": None,  # to be filled in by hand: "correct" | "wrong"
            "judgement_reason": None,
        })

    out = {
        "seed": SEED,
        "sample_size": sample_size,
        "total_proposals": len(proposals),
        "prior_seeds_used": [42, 7, 2026],
        "items": worksheet,
    }
    out_path = HERE / "review_worksheet.json"
    out_path.write_text(json.dumps(out, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(f"sampled {sample_size} of {len(proposals)} proposals (seed={SEED}) -> {out_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
