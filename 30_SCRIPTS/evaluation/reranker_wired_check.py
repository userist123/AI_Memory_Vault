"""Reranker wiring check: does the wired flag reproduce the measured experiment?

Builds the controller exactly as production does (``controller_factory``) with
``AI_MEMORY_VAULT_RERANKER_URL`` set, runs the 160 benchmark v3 queries as
``Principal.AI_AGENT`` with ``page_size=5`` and compares the served top 5 with
the confirmatory replay (``served/bge_fp16_r1_k50.json``) and with the gold.

    python 30_SCRIPTS/evaluation/reranker_wired_check.py --url http://127.0.0.1:8765
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import time
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
for p in (REPO_ROOT, REPO_ROOT / "03_IMPLEMENTATION" / "packages", REPO_ROOT / "03_IMPLEMENTATION"):
    if str(p) not in sys.path:
        sys.path.insert(0, str(p))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)
os.environ["MEMORY_VAULT_ROOT"] = str(REPO_ROOT)
EXP = REPO_ROOT / "07_EVALUATION" / "reranker_experiment"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--url", required=True)
    ap.add_argument("--reference", type=Path, default=EXP / "served" / "bge_fp16_r1_k50.json")
    ap.add_argument("--out", type=Path, default=EXP / "results" / "wired_check.json")
    args = ap.parse_args()
    os.environ["AI_MEMORY_VAULT_RERANKER_URL"] = args.url

    from memory_controller.authorizer import Principal
    from memory_controller.controller_factory import get_memory_controller

    snap = json.loads((EXP / "snapshot.json").read_text(encoding="utf-8"))
    ref = json.loads(args.reference.read_text(encoding="utf-8"))["served"]
    controller = get_memory_controller()
    assert controller.reranker is not None, "factory did not build the reranker"
    served, statuses, times, rerank_ms = {}, {}, [], []
    for row in snap["rows"]:
        t0 = time.perf_counter()
        pack = controller.search(Principal.AI_AGENT, row["query"], page_size=5)
        times.append((time.perf_counter() - t0) * 1000.0)
        info = (pack.get("candidate_trace") or {}).get("reranker") or {}
        statuses[info.get("status")] = statuses.get(info.get("status"), 0) + 1
        if info.get("ms") is not None:
            rerank_ms.append(info["ms"])
        served[row["id"]] = [r.get("id") for r in (pack.get("results") or [])[:5]]

    rows = [r for r in snap["rows"] if not r["abstain"]]
    hit = sum(bool(set(r["gold"]) & set(served[r["id"]])) for r in rows)
    ref_hit = sum(bool(set(r["gold"]) & set(ref[r["id"]]["ids"])) for r in rows)
    same = sum(served[c] == ref[c]["ids"] for c in served)
    same_set = sum(set(served[c]) == set(ref[c]["ids"]) for c in served)
    st = sorted(times)
    out = {
        "git_head": os.popen(f'git -C "{REPO_ROOT}" rev-parse HEAD').read().strip(),
        "model": getattr(controller.reranker, "model", None),
        "reranker_status": statuses, "cases": len(served),
        "wired_hit5": hit, "reference_hit5": ref_hit, "baseline_hit5": sum(r["baseline_hit5"] for r in rows),
        "identical_top5_to_reference": same, "same_top5_set_as_reference": same_set,
        "search_ms_p50": round(st[len(st) // 2], 1), "search_ms_p95": round(st[int(len(st) * 0.95) - 1], 1),
        "rerank_ms_p95": round(sorted(rerank_ms)[int(len(rerank_ms) * 0.95) - 1], 1) if rerank_ms else None,
        "served": served,
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(out, ensure_ascii=False), encoding="utf-8")
    print(json.dumps({k: v for k, v in out.items() if k != "served"}, indent=1))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
