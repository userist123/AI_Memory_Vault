"""Reranker experiment, phase 3: score a reranker run against the frozen gold.

Implements the decision rule frozen in
07_EVALUATION/reranker_experiment/PREREGISTRATION.md (section 5) and the
controls of section 6. Reads ``snapshot.json`` (baseline, gold) and one or more
run files written by ``reranker_rerank.py``.

    python 30_SCRIPTS/evaluation/reranker_analyze.py --run runs/bge_k50_r1.json \
        --repeat runs/bge_k50_r2.json runs/bge_k50_r3.json --k 50 --confirmatory
"""
from __future__ import annotations

import argparse
import json
import math
import os
import random
import sys
from collections import defaultdict
from pathlib import Path
from typing import Dict, List, Optional

REPO_ROOT = Path(__file__).resolve().parents[2]
EXP = REPO_ROOT / "07_EVALUATION" / "reranker_experiment"


def mcnemar_exact(b: int, c: int) -> float:
    n = b + c
    if n == 0:
        return 1.0
    k = min(b, c)
    tail = sum(math.comb(n, i) for i in range(k + 1)) / 2 ** n
    return min(1.0, 2 * tail)


def first_hit(ids: List[str], gold: set) -> Optional[int]:
    return next((i for i, x in enumerate(ids, 1) if x in gold), None)


def auroc(pos: List[float], neg: List[float]) -> Optional[float]:
    if not pos or not neg:
        return None
    wins = sum((p > n) + 0.5 * (p == n) for p in pos for n in neg)
    return wins / (len(pos) * len(neg))


def eligible_ids(ids: List[str]) -> Dict[str, bool]:
    """True when the agent read path (memory_access.get) serves the note."""
    for p in (REPO_ROOT, REPO_ROOT / "03_IMPLEMENTATION" / "packages", REPO_ROOT / "03_IMPLEMENTATION"):
        if str(p) not in sys.path:
            sys.path.insert(0, str(p))
    os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)
    os.environ["MEMORY_VAULT_ROOT"] = str(REPO_ROOT)
    from interfaces import memory_access
    from memory_controller.controller_factory import get_memory_controller
    c = get_memory_controller()
    out = {}
    for nid in ids:
        try:
            out[nid] = bool(memory_access.get(c, nid).get("ok", True)) and memory_access.get(c, nid).get("error") is None
        except Exception:  # noqa: BLE001
            out[nid] = False
    return out


def score(snapshot: dict, run: dict, k: str) -> dict:
    rows = [r for r in snapshot["rows"] if not r["abstain"]]
    rk = run["by_k"][k]["rankings"]
    wins, losses, per = [], [], defaultdict(lambda: {"n": 0, "base": 0, "arm": 0, "wins": 0, "losses": 0})
    mrr_b = mrr_a = h1b = h1a = h3b = h3a = 0.0
    convertible = converted = 0
    for r in rows:
        gold = set(r["gold"])
        base = r["baseline_top5"]
        arm = rk[r["id"]]["ids"][:5]
        bh, ah = bool(gold & set(base)), bool(gold & set(arm))
        fused = [e["id"] for e in r["fused_top200"]]
        fb, fa = first_hit(fused[:10], gold), first_hit(rk[r["id"]]["ids"][:10], gold)
        mrr_b += 1 / fb if fb else 0
        mrr_a += 1 / fa if fa else 0
        h1b += fb == 1
        h1a += fa == 1
        h3b += bool(fb and fb <= 3)
        h3a += bool(fa and fa <= 3)
        gr = r["gold_fused_rank"]
        if gr and 5 < gr <= int(k):
            convertible += 1
            converted += ah
        for key in (f"class:{r['class']}", f"lang:{r['language']}", "all"):
            s = per[key]
            s["n"] += 1
            s["base"] += bh
            s["arm"] += ah
            s["wins"] += ah and not bh
            s["losses"] += bh and not ah
        if ah and not bh:
            wins.append(r["id"])
        if bh and not ah:
            losses.append(r["id"])
    n = len(rows)
    b, c = len(wins), len(losses)
    return {
        "n": n, "baseline_hit5": per["all"]["base"], "arm_hit5": per["all"]["arm"],
        "wins": b, "losses": c, "net": b - c, "mcnemar_exact_p": round(mcnemar_exact(b, c), 5),
        "win_ids": wins, "loss_ids": losses,
        "mrr10": {"baseline": round(mrr_b / n, 4), "arm": round(mrr_a / n, 4)},
        "hit1": {"baseline": int(h1b), "arm": int(h1a)}, "hit3": {"baseline": int(h3b), "arm": int(h3a)},
        "convertible_6_to_k": convertible, "converted": int(converted),
        "strata": {k2: dict(v) for k2, v in sorted(per.items())},
    }


def decide(s: dict, ro_net: int, p95_ok: bool) -> str:
    w, l = s["wins"], s["losses"]
    if s["net"] <= 2 or l > w / 2:
        return "REJECT"
    if s["net"] >= 8 and s["mcnemar_exact_p"] < 0.05 and l <= w / 3 and ro_net >= 0 and p95_ok:
        return "ADOPT"
    return "INCONCLUSIVE"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--snapshot-dir", type=Path, default=EXP)
    ap.add_argument("--run", type=Path, required=True)
    ap.add_argument("--repeat", type=Path, nargs="*", default=[])
    ap.add_argument("--k", default="50")
    ap.add_argument("--p95-ok", choices=["yes", "no"], required=True)
    ap.add_argument("--out", type=Path)
    args = ap.parse_args()

    snap = json.loads((args.snapshot_dir / "snapshot.json").read_text(encoding="utf-8"))
    run = json.loads(args.run.read_text(encoding="utf-8"))
    s = score(snap, run, args.k)
    ro = s["strata"].get("lang:ro", {"wins": 0, "losses": 0})
    s["decision"] = decide(s, ro["wins"] - ro["losses"], args.p95_ok == "yes")

    # Controls (PREREGISTRATION section 6)
    rows = [r for r in snap["rows"] if not r["abstain"]]
    ident = {"by_k": {args.k: {"rankings": {r["id"]: {"ids": [e["id"] for e in r["fused_top200"][:int(args.k)]]}
                                             for r in snap["rows"]}}}}
    si = score(snap, ident, args.k)
    oracle = {"by_k": {args.k: {"rankings": {
        r["id"]: {"ids": sorted([e["id"] for e in r["fused_top200"][:int(args.k)]],
                                key=lambda x, g=set(r["gold"]): x not in g)} for r in snap["rows"]}}}}
    so = score(snap, oracle, args.k)
    rng = random.Random(20261119)
    shuffled = json.loads(json.dumps(snap))
    golds = [r["gold"] for r in shuffled["rows"] if not r["abstain"]]
    rng.shuffle(golds)
    for r, g in zip([r for r in shuffled["rows"] if not r["abstain"]], golds):
        r["gold"] = g
        r["baseline_hit5"] = bool(set(g) & set(r["baseline_top5"]))
    ss = score(shuffled, run, args.k)
    flips = []
    for rp in args.repeat:
        other = json.loads(rp.read_text(encoding="utf-8"))["by_k"][args.k]["rankings"]
        flips.append(sum(other[r["id"]]["ids"][:5] != run["by_k"][args.k]["rankings"][r["id"]]["ids"][:5] for r in rows))
    oracle_k = sum(1 for r in rows if r["gold_fused_rank"] and r["gold_fused_rank"] <= int(args.k))
    s["controls"] = {
        "identity_reproduces_baseline": si["wins"] == 0 and si["losses"] == 0,
        "identity_hit5": si["arm_hit5"],
        "oracle_hit5": so["arm_hit5"], "oracle_at_k_expected": oracle_k,
        "oracle_matches": so["arm_hit5"] == oracle_k,
        "shuffled_labels_net": ss["net"], "shuffled_labels_arm_hit5": ss["arm_hit5"],
        "repeat_top5_flips": flips,
    }

    # Abstain: descriptive AUROC of the top reranker score (no threshold fitted)
    rk = run["by_k"][args.k]["rankings"]
    top = lambda r: (rk[r["id"]]["scores"] or [float("-inf")])[0]  # noqa: E731
    s["abstain_auroc_top_score"] = auroc([top(r) for r in rows], [top(r) for r in snap["rows"] if r["abstain"]])

    # Eligibility of every note the arm newly put in a top 5
    new_ids = sorted({x for r in rows for x in rk[r["id"]]["ids"][:5] if x not in r["baseline_top5"]})
    elig = eligible_ids(new_ids)
    s["new_top5_notes"] = len(new_ids)
    s["new_top5_not_agent_readable"] = sorted(k2 for k2, v in elig.items() if not v)

    print(json.dumps({k2: v for k2, v in s.items() if k2 not in ("strata",)}, indent=1, ensure_ascii=False))
    print(json.dumps(s["strata"], indent=1))
    if args.out:
        args.out.write_text(json.dumps(s, indent=1, ensure_ascii=False), encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
