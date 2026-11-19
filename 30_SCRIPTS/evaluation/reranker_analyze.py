"""Reranker experiment, phase 3: score served top-5 lists against the frozen gold.

Implements PREREGISTRATION.md section 5 as amended by A1 (section 8): both the
baseline and the arm are what ``search()`` actually serves (``pack["results"]``),
the arm through ``reranker_replay.py``. Raw reranker files are used only for
configuration checks, latency and the abstain AUROC.

    python 30_SCRIPTS/evaluation/reranker_analyze.py \
        --served served/bge_fp16_r1_k50.json \
        --repeat-served served/bge_fp16_r2_k50.json served/bge_fp16_r3_k50.json \
        --raw-run runs/bge_fp16_r1.json --k 50 --confirmatory \
        --out results/confirmatory.json
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
CONFIRMATORY = {"model": "BAAI/bge-reranker-v2-m3", "revision": "953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e",
                "fp16": True, "device": "cuda", "k": 50}
P95_LIMIT_MS = 2000.0


def mcnemar_exact(b: int, c: int) -> float:
    n = b + c
    if n == 0:
        return 1.0
    tail = sum(math.comb(n, i) for i in range(min(b, c) + 1)) / 2 ** n
    return min(1.0, 2 * tail)


def first_hit(ids: List[str], gold: set) -> Optional[int]:
    return next((i for i, x in enumerate(ids, 1) if x in gold), None)


def auroc(pos: List[float], neg: List[float]) -> Optional[float]:
    if not pos or not neg:
        return None
    return sum((p > n) + 0.5 * (p == n) for p in pos for n in neg) / (len(pos) * len(neg))


def score(rows: List[dict], arm: Dict[str, List[str]], k: int) -> dict:
    per = defaultdict(lambda: {"n": 0, "base": 0, "arm": 0, "wins": 0, "losses": 0})
    wins, losses = [], []
    rr = {"baseline": 0.0, "arm": 0.0}
    h1 = {"baseline": 0, "arm": 0}
    h3 = {"baseline": 0, "arm": 0}
    convertible = converted = 0
    for r in rows:
        gold = set(r["gold"])
        b_ids, a_ids = r["baseline_top5"], arm[r["id"]]
        bh, ah = bool(gold & set(b_ids)), bool(gold & set(a_ids))
        for name, ids in (("baseline", b_ids), ("arm", a_ids)):
            f = first_hit(ids, gold)
            rr[name] += 1 / f if f else 0
            h1[name] += f == 1
            h3[name] += bool(f and f <= 3)
        gr = r["gold_fused_rank"]
        if gr and 5 < gr <= k:
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
    return {
        "n": n, "baseline_hit5": per["all"]["base"], "arm_hit5": per["all"]["arm"],
        "wins": len(wins), "losses": len(losses), "net": len(wins) - len(losses),
        "mcnemar_exact_p": round(mcnemar_exact(len(wins), len(losses)), 5),
        "win_ids": wins, "loss_ids": losses,
        "mrr5": {k2: round(v / n, 4) for k2, v in rr.items()}, "hit1": h1, "hit3": h3,
        "convertible_6_to_k": convertible, "converted": converted,
        "strata": {k2: dict(v) for k2, v in sorted(per.items())},
    }


def decide(s: dict, ro_net: int, p95_ok: bool) -> str:
    w, l = s["wins"], s["losses"]
    if s["net"] <= 2 or l > w / 2:
        return "REJECT"
    if s["net"] >= 8 and s["mcnemar_exact_p"] < 0.05 and l <= w / 3 and ro_net >= 0 and p95_ok:
        return "ADOPT"
    return "INCONCLUSIVE"


def served_ids(path: Path) -> Dict[str, List[str]]:
    return {c: v["ids"] for c, v in json.loads(path.read_text(encoding="utf-8"))["served"].items()}


def eligible(ids: List[str]) -> Dict[str, bool]:
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
            memory_access.get(c, nid)
            out[nid] = True
        except Exception:  # noqa: BLE001 - get() raises for a note the agent may not read
            out[nid] = False
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--snapshot-dir", type=Path, default=EXP)
    ap.add_argument("--served", type=Path, required=True)
    ap.add_argument("--repeat-served", type=Path, nargs="*", default=[])
    ap.add_argument("--raw-run", type=Path, required=True)
    ap.add_argument("--k", type=int, default=50)
    ap.add_argument("--confirmatory", action="store_true", help="assert the frozen confirmatory configuration")
    ap.add_argument("--out", type=Path)
    args = ap.parse_args()

    snap = json.loads((args.snapshot_dir / "snapshot.json").read_text(encoding="utf-8"))
    rows = [r for r in snap["rows"] if not r["abstain"]]
    served_doc = json.loads(args.served.read_text(encoding="utf-8"))
    meta = served_doc["meta"]
    raw = json.loads(args.raw_run.read_text(encoding="utf-8"))
    if meta.get("run_file") != args.raw_run.name or meta.get("k") != args.k:
        raise SystemExit(f"CONFIG_MISMATCH served={meta} raw={args.raw_run.name} k={args.k}")
    if args.confirmatory:
        got = {"model": raw["model"], "revision": raw["revision"], "fp16": raw.get("fp16"),
               "device": raw["device"], "k": args.k}
        if got != CONFIRMATORY or raw.get("cases") != len(snap["rows"]):
            raise SystemExit(f"NOT_THE_CONFIRMATORY_ARM {got} cases={raw.get('cases')}")
    arm = served_ids(args.served)

    s = score(rows, arm, args.k)
    search_p95 = sorted(r["search_ms"] for r in snap["rows"])[int(len(snap["rows"]) * 0.95) - 1]
    rerank_p95 = raw["by_k"][str(args.k)]["rerank_ms_p95"]
    s["latency"] = {"search_p95_ms": search_p95, "rerank_p95_ms": rerank_p95,
                    "end_to_end_p95_upper_bound_ms": round(search_p95 + rerank_p95, 1)}
    p95_ok = s["latency"]["end_to_end_p95_upper_bound_ms"] <= P95_LIMIT_MS
    ro = s["strata"].get("lang:ro", {"wins": 0, "losses": 0})
    s["decision"] = decide(s, ro["wins"] - ro["losses"], p95_ok)
    s["decision_inputs"] = {"ro_net": ro["wins"] - ro["losses"], "p95_ok": p95_ok}

    # Controls (section 6, amended by A1)
    rng = random.Random(20261119)
    shuffled = [dict(r) for r in rows]
    golds = [r["gold"] for r in shuffled]
    rng.shuffle(golds)
    for r, g in zip(shuffled, golds):
        r["gold"] = g
    ss = score(shuffled, arm, args.k)
    flips = [sum(served_ids(p)[r["id"]] != arm[r["id"]] for r in rows) for p in args.repeat_served]
    controls = {"shuffled_labels_net": ss["net"], "repeat_served_top5_flips": flips}
    for name in ("identity", "oracle"):
        p = EXP / "served" / f"{name}_k{args.k}.json"
        if p.exists():
            controls[f"{name}_hit5"] = score(rows, served_ids(p), args.k)["arm_hit5"]
    base_ids = {r["id"]: r["baseline_top5"] for r in snap["rows"]}
    ident_path = EXP / "served" / f"identity_k{args.k}.json"
    if ident_path.exists():
        ident = served_ids(ident_path)
        controls["identity_differs_from_baseline"] = sum(ident[c] != base_ids[c] for c in base_ids)
    s["controls"] = controls
    # Section 6: controls must pass before any conclusion.
    oracle_expected = sum(1 for r in rows if r["gold_fused_rank"] and r["gold_fused_rank"] <= args.k)
    failed = []
    if controls.get("identity_differs_from_baseline") != 0:
        failed.append("identity")
    if controls.get("oracle_hit5") != oracle_expected:
        failed.append("oracle")
    heads = {json.loads(p.read_text(encoding="utf-8"))["meta"].get("git_head")
             for p in [args.served, ident_path, *args.repeat_served]}
    if len(heads) != 1 or None in heads:
        failed.append(f"git_head_mismatch:{sorted(map(str, heads))}")
    if failed:
        s["decision"] = "CONTROLS_FAILED:" + ",".join(failed)

    rk = raw["by_k"][str(args.k)]["rankings"]
    top = lambda rid: (rk[rid]["scores"] or [float("-inf")])[0]  # noqa: E731
    s["abstain_auroc_top_score"] = auroc([top(r["id"]) for r in rows],
                                         [top(r["id"]) for r in snap["rows"] if r["abstain"]])
    new_ids = sorted({x for r in rows for x in arm[r["id"]] if x not in r["baseline_top5"]})
    elig = eligible(new_ids)
    s["new_top5_notes"] = len(new_ids)
    s["new_top5_not_agent_readable"] = sorted(k2 for k2, v in elig.items() if not v)
    s["served_fewer_than_5"] = {"baseline": sum(len(r["baseline_top5"]) < 5 for r in rows),
                                "arm": sum(len(arm[r["id"]]) < 5 for r in rows)}
    s["config"] = {"served_meta": meta}

    print(json.dumps({k2: v for k2, v in s.items() if k2 != "strata"}, indent=1, ensure_ascii=False))
    print(json.dumps(s["strata"], indent=1))
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(s, indent=1, ensure_ascii=False), encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
