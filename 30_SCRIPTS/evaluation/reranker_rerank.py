"""Reranker experiment, phase 2: rerank the frozen candidate snapshot.

Runs in a separate environment that has ``sentence-transformers`` (it never
imports the vault packages and never reads gold labels). Input: the
``snapshot.json`` + ``texts.json`` written by ``reranker_snapshot.py``.
Output: one ranking per case (top K of the fused list, reordered by the
cross-encoder score), plus timing and the exact model revision.

    python reranker_rerank.py --model BAAI/bge-reranker-v2-m3 --k 50 --device cuda \
        --out 07_EVALUATION/reranker_experiment/runs/bge_k50_r1.json

``--latency-only`` measures warm per-query rerank time at each K without
writing any ranking (labels are never read either way).
"""
from __future__ import annotations

import argparse
import json
import statistics
import time
from pathlib import Path

MAX_LENGTH = 512


def load(snapshot_dir: Path):
    snap = json.loads((snapshot_dir / "snapshot.json").read_text(encoding="utf-8"))
    texts = json.loads((snapshot_dir / "texts.json").read_text(encoding="utf-8"))
    return snap, texts


def model_revision(model) -> str:
    try:
        path = Path(model.model.config._name_or_path)
        if path.exists():
            return path.name
    except Exception:  # noqa: BLE001
        pass
    return "unknown"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--snapshot-dir", type=Path, required=True)
    ap.add_argument("--model", required=True)
    ap.add_argument("--revision", default=None)
    ap.add_argument("--k", type=int, nargs="+", default=[50])
    ap.add_argument("--device", default="cuda")
    ap.add_argument("--out", type=Path)
    ap.add_argument("--latency-only", action="store_true")
    ap.add_argument("--limit", type=int, default=None, help="latency sample: first N cases only")
    ap.add_argument("--fp16", action="store_true", help="half precision weights (GPU)")
    args = ap.parse_args()

    import torch
    from sentence_transformers import CrossEncoder

    snap, texts = load(args.snapshot_dir)
    rows = snap["rows"][: args.limit] if args.limit else snap["rows"]

    if args.device == "cuda":
        torch.cuda.reset_peak_memory_stats()
    t0 = time.perf_counter()
    kwargs = {"model_kwargs": {"torch_dtype": torch.float16}} if args.fp16 else {}
    model = CrossEncoder(args.model, revision=args.revision, device=args.device, max_length=MAX_LENGTH, **kwargs)
    load_s = time.perf_counter() - t0
    tok = model.tokenizer

    # warm-up
    model.predict([(rows[0]["query"], "warm up")] * 4)

    out = {"model": args.model, "revision": args.revision, "device": args.device, "fp16": args.fp16,
           "cases": len(rows),
           "max_length": MAX_LENGTH, "load_seconds": round(load_s, 2), "by_k": {}}
    for k in args.k:
        times, rankings, truncated, pairs_total = [], {}, 0, 0
        for r in rows:
            cand = [e["id"] for e in r["fused_top200"][:k] if e["id"] in texts]
            pairs = [(r["query"], texts[c]) for c in cand]
            pairs_total += len(pairs)
            truncated += sum(1 for _, t in pairs if len(tok(t, add_special_tokens=False)["input_ids"]) > MAX_LENGTH)
            if args.device == "cuda":
                torch.cuda.synchronize()
            t1 = time.perf_counter()
            scores = model.predict(pairs, batch_size=32, show_progress_bar=False) if pairs else []
            if args.device == "cuda":
                torch.cuda.synchronize()
            times.append((time.perf_counter() - t1) * 1000.0)
            order = sorted(range(len(cand)), key=lambda i: (-float(scores[i]), i))
            rankings[r["id"]] = {"ids": [cand[i] for i in order], "scores": [round(float(scores[i]), 5) for i in order]}
        st = sorted(times)
        entry = {"rerank_ms_p50": round(st[len(st) // 2], 1), "rerank_ms_p95": round(st[int(len(st) * 0.95) - 1], 1),
                 "rerank_ms_max": round(st[-1], 1), "pairs": pairs_total, "pairs_over_max_length": truncated}
        if not args.latency_only:
            entry["rankings"] = rankings
        out["by_k"][str(k)] = entry
    if args.device == "cuda":
        out["peak_vram_mb"] = round(torch.cuda.max_memory_allocated() / 2**20)
    out["torch"] = torch.__version__
    summary = {k: {kk: vv for kk, vv in v.items() if kk != "rankings"} for k, v in out["by_k"].items()}
    print(json.dumps({**{k: v for k, v in out.items() if k != "by_k"}, "by_k": summary}, indent=1))
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(out, ensure_ascii=False), encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
