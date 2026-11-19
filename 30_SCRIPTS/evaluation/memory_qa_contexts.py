"""Memory QA ablation, step 1: collect the memory context an agent would read.

For every benchmark v3 case, builds the context of condition B (reranker OFF,
production today) and C (reranker ON) exactly from the agent tools:
``memory_access.search(controller, question, 5)`` (title + snippet per result)
followed by ``memory_access.get`` of each result (body, first BODY_CHARS
characters; a note get() refuses keeps only its snippet). Gold is never read.

Run while the reranker server is up; answers are generated afterwards with
the server stopped (the machine cannot hold both models at once).

    python 30_SCRIPTS/evaluation/memory_qa_contexts.py --url http://127.0.0.1:8765
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
for p in (REPO_ROOT, REPO_ROOT / "03_IMPLEMENTATION" / "packages", REPO_ROOT / "03_IMPLEMENTATION"):
    if str(p) not in sys.path:
        sys.path.insert(0, str(p))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)
os.environ["MEMORY_VAULT_ROOT"] = str(REPO_ROOT)
BENCH = REPO_ROOT / "07_EVALUATION" / "retrieval_benchmark_v3" / "retrieval_benchmark_v3.json"
OUT = REPO_ROOT / "07_EVALUATION" / "memory_qa_ablation" / "contexts.json"
BODY_CHARS = 1500
TOP = 5


def build_context(controller, question: str):
    from interfaces import memory_access
    found = memory_access.search(controller, question, TOP)["query_results"]
    blocks, ids = [], []
    for i, r in enumerate(found, 1):
        ids.append(r["id"])
        body = ""
        try:
            note = memory_access.get(controller, r["id"])
            body = str(note.get("content") or "")[:BODY_CHARS]
        except Exception:  # noqa: BLE001 - get() refuses: the agent keeps only the snippet
            body = ""
        blocks.append(f"[{i}] {r['title']}\n{body or r.get('snippet') or ''}".strip())
    return "\n\n".join(blocks), ids


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--url", required=True)
    args = ap.parse_args()
    from memory_controller.controller import MemoryController
    from memory_controller.controller_factory import get_memory_controller
    from retrieval.reranker import HttpReranker

    os.environ.pop("AI_MEMORY_VAULT_RERANKER_URL", None)
    off = get_memory_controller()
    assert off.reranker is None
    on = MemoryController(off.storage, reranker=HttpReranker(args.url))
    cases = json.loads(BENCH.read_text(encoding="utf-8"))["cases"]
    rows = []
    for c in cases:
        b_ctx, b_ids = build_context(off, c["query"])
        c_ctx, c_ids = build_context(on, c["query"])
        rows.append({"id": c["id"], "question": c["query"], "language": c.get("language"),
                     "B": {"context": b_ctx, "ids": b_ids}, "C": {"context": c_ctx, "ids": c_ids}})
    OUT.parent.mkdir(parents=True, exist_ok=True)
    payload = json.dumps({"git_head": os.popen(f'git -C "{REPO_ROOT}" rev-parse HEAD').read().strip(),
                          "top": TOP, "body_chars": BODY_CHARS, "rows": rows}, ensure_ascii=False)
    OUT.write_text(payload, encoding="utf-8")
    same = sum(r["B"]["ids"] == r["C"]["ids"] for r in rows)
    print(json.dumps({"cases": len(rows), "identical_B_C": same,
                      "sha256": hashlib.sha256(payload.encode("utf-8")).hexdigest()}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
