"""Reranker experiment, phase 2b: replay an ordering through the real search() pipeline.

Pre-score amendment A1 (PREREGISTRATION.md section 8). A reranker ordering is
credited only with what production would actually serve: the ordering is
injected at the one place production ranks (the ``fused_score`` values the
controller reads from the candidate trace, ``controller.py`` graph-OFF branch,
``_ranking_key_fn``), and everything after that -- pack building, egress,
provenance/compression rejection, the token budget -- runs unchanged.

How: ``RetrievalEngine.retrieve`` is wrapped for the duration of one
``controller.search(Principal.AI_AGENT, query, page_size=5)`` call. After the
original ``retrieve`` returns, the top-K ids of the injected ordering get
synthetic scores ``1000 - position`` in ``trace_sink["fused_ranking"]``; every
other candidate keeps its real fused score (all < 1), so it stays below and in
production order. Nothing else is touched.

Orderings:
* ``identity`` -- the controller's own order for the top K (fused score desc,
  id desc). Must reproduce the baseline exactly (control 1).
* ``oracle``   -- gold notes first, then identity. Ceiling through the pack stage.
* ``run``      -- a ``reranker_rerank.py`` output file.

    python 30_SCRIPTS/evaluation/reranker_replay.py --ordering run \
        --run 07_EVALUATION/reranker_experiment/runs/bge_fp16_r1.json --k 50 \
        --out 07_EVALUATION/reranker_experiment/served/bge_fp16_r1_k50.json
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path
from typing import Dict, List

REPO_ROOT = Path(__file__).resolve().parents[2]
for p in (REPO_ROOT, REPO_ROOT / "03_IMPLEMENTATION" / "packages", REPO_ROOT / "03_IMPLEMENTATION"):
    if str(p) not in sys.path:
        sys.path.insert(0, str(p))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)
os.environ["MEMORY_VAULT_ROOT"] = str(REPO_ROOT)

from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller.controller_factory import get_memory_controller  # noqa: E402

EXP = REPO_ROOT / "07_EVALUATION" / "reranker_experiment"


def identity_order(row: dict, k: int) -> List[str]:
    top = row["fused_top200"][:k]
    return [e["id"] for e in sorted(top, key=lambda e: (float(e["fused_score"] or 0.0), e["id"]), reverse=True)]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--snapshot-dir", type=Path, default=EXP)
    ap.add_argument("--ordering", choices=["identity", "oracle", "run"], required=True)
    ap.add_argument("--run", type=Path)
    ap.add_argument("--k", type=int, default=50)
    ap.add_argument("--out", type=Path, required=True)
    args = ap.parse_args()

    snap = json.loads((args.snapshot_dir / "snapshot.json").read_text(encoding="utf-8"))
    run_rankings: Dict[str, dict] = {}
    meta = {"ordering": args.ordering, "k": args.k}
    if args.ordering == "run":
        run = json.loads(args.run.read_text(encoding="utf-8"))
        run_rankings = run["by_k"][str(args.k)]["rankings"]
        meta.update({kk: run.get(kk) for kk in ("model", "revision", "device", "fp16", "max_length", "cases")})
        meta["run_file"] = args.run.name

    controller = get_memory_controller()
    engine = controller.retrieval_engine
    original = engine.retrieve
    state = {"order": [], "applied": False}

    def patched(*a, **kw):
        notes = original(*a, **kw)
        sink = kw.get("trace_sink")
        if sink is not None and sink.get("source") == "generated" and state["order"]:
            pos = {nid: i for i, nid in enumerate(state["order"])}
            for e in sink.get("fused_ranking") or []:
                if isinstance(e, dict) and e.get("id") in pos:
                    e["fused_score"] = 1000.0 - pos[e["id"]]
            state["applied"] = True
        return notes

    engine.retrieve = patched
    served: Dict[str, dict] = {}
    try:
        for row in snap["rows"]:
            if args.ordering == "identity":
                order = identity_order(row, args.k)
            elif args.ordering == "oracle":
                ident = identity_order(row, args.k)
                gold = set(row["gold"])
                order = [x for x in ident if x in gold] + [x for x in ident if x not in gold]
            else:
                order = run_rankings[row["id"]]["ids"][: args.k]
            state.update(order=order, applied=False)
            pack = controller.search(Principal.AI_AGENT, row["query"], page_size=5)
            trace = pack.get("candidate_trace") or {}
            if trace.get("source") != "generated" or trace.get("ranking_arm") != "fused_score" or (order and not state["applied"]):
                raise SystemExit(f"INVALID_REPLAY {row['id']}: source={trace.get('source')} "
                                 f"arm={trace.get('ranking_arm')} applied={state['applied']}")
            served[row["id"]] = {"ids": [r.get("id") for r in (pack.get("results") or [])[:5]]}
    finally:
        engine.retrieve = original

    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps({"meta": meta, "served": served}, ensure_ascii=False), encoding="utf-8")
    base = {r["id"]: r["baseline_top5"] for r in snap["rows"]}
    diff = sum(served[c]["ids"] != base[c] for c in served)
    short = sum(len(v["ids"]) < 5 for v in served.values())
    print(json.dumps({**meta, "cases": len(served), "differs_from_baseline": diff, "served_fewer_than_5": short}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
