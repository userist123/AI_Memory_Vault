"""Reranker harm check on the 20 real work questions (07_EVALUATION/memory_usage/).

Step ``collect``: run every question through the production controller with
the reranker OFF and ON (``page_size=5``; the first three are what an agent
reads and what is judged), then write a BLIND judging packet: every distinct
(question, note) pair, shuffled, with no arm label, plus a private key file
mapping pairs to arms.

Step ``score``: read the judgements (one per pair id, ``relevant`` true/false)
and report, per arm, relevant results in the first three and questions with at
least one relevant result; wins / losses per question.

    python 30_SCRIPTS/evaluation/reranker_worklist_harm.py collect --url http://127.0.0.1:8765
    python 30_SCRIPTS/evaluation/reranker_worklist_harm.py score --judgements <file>
"""
from __future__ import annotations

import argparse
import json
import os
import random
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
for p in (REPO_ROOT, REPO_ROOT / "03_IMPLEMENTATION" / "packages", REPO_ROOT / "03_IMPLEMENTATION"):
    if str(p) not in sys.path:
        sys.path.insert(0, str(p))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)
os.environ["MEMORY_VAULT_ROOT"] = str(REPO_ROOT)
OUT = REPO_ROOT / "07_EVALUATION" / "reranker_experiment" / "worklist"
QUERIES = REPO_ROOT / "07_EVALUATION" / "memory_usage" / "work_queries.json"
JUDGED = 3


def collect(url: str) -> None:
    from memory_controller.authorizer import Principal
    from memory_controller.controller import MemoryController
    from memory_controller.controller_factory import get_memory_controller
    from retrieval.reranker import HttpReranker, rerank_text

    os.environ.pop("AI_MEMORY_VAULT_RERANKER_URL", None)
    off = get_memory_controller()
    assert off.reranker is None
    on = MemoryController(off.storage, reranker=HttpReranker(url))
    questions = json.loads(QUERIES.read_text(encoding="utf-8"))["queries"]
    arms, statuses = {"off": {}, "on": {}}, {}
    for q in questions:
        for name, ctl in (("off", off), ("on", on)):
            pack = ctl.search(Principal.AI_AGENT, q["question"], page_size=5)
            arms[name][str(q["id"])] = [r.get("id") for r in (pack.get("results") or [])[:JUDGED]]
            st = ((pack.get("candidate_trace") or {}).get("reranker") or {}).get("status")
            statuses[f"{name}:{st}"] = statuses.get(f"{name}:{st}", 0) + 1
    pairs, key = [], {}
    for q in questions:
        qid = str(q["id"])
        for nid in dict.fromkeys(arms["off"][qid] + arms["on"][qid]):
            note = off.storage.get(nid) or {}
            pairs.append({"question_id": q["id"], "question": q["question"], "task_source_quote": q["source_quote"],
                          "note_id": nid, "note_text": rerank_text(note)})
    random.Random(20261119).shuffle(pairs)
    for i, p in enumerate(pairs, 1):
        p["pair_id"] = f"P{i:03d}"
        key[p["pair_id"]] = {"question_id": p["question_id"], "note_id": p["note_id"]}
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "blind_packet.json").write_text(json.dumps({
        "rule": "A note is relevant only if it would help do the task the question was taken from "
                "(the task_source_quote). Judge each pair independently; you are not told which system returned it.",
        "pairs": [{k: v for k, v in p.items()} for p in pairs]}, ensure_ascii=False, indent=1), encoding="utf-8")
    (OUT / "arms.json").write_text(json.dumps({"arms": arms, "key": key, "statuses": statuses,
                                               "judged_depth": JUDGED}, indent=1), encoding="utf-8")
    print(json.dumps({"questions": len(questions), "pairs": len(pairs), "statuses": statuses}))


def score(judgements: Path) -> None:
    data = json.loads((OUT / "arms.json").read_text(encoding="utf-8"))
    j = json.loads(judgements.read_text(encoding="utf-8"))
    rel = {}
    for item in j["judgements"]:
        k = data["key"][item["pair_id"]]
        rel[(str(k["question_id"]), k["note_id"])] = bool(item["relevant"])
    missing = [(q, n) for q, ids in data["arms"]["off"].items() for n in ids if (q, n) not in rel] + \
              [(q, n) for q, ids in data["arms"]["on"].items() for n in ids if (q, n) not in rel]
    if missing:
        raise SystemExit(f"UNJUDGED_PAIRS {missing[:5]} (+{max(0, len(missing) - 5)})")
    res = {}
    for arm in ("off", "on"):
        ids = data["arms"][arm]
        res[arm] = {"relevant_in_first3": sum(rel[(q, n)] for q, ns in ids.items() for n in ns),
                    "results": sum(len(ns) for ns in ids.values()),
                    "questions_with_relevant": sum(any(rel[(q, n)] for n in ns) for q, ns in ids.items())}
    per_q = {}
    for q in data["arms"]["off"]:
        a = any(rel[(q, n)] for n in data["arms"]["off"][q])
        b = any(rel[(q, n)] for n in data["arms"]["on"][q])
        per_q[q] = "win" if b and not a else "loss" if a and not b else "same"
    res["per_question"] = per_q
    res["wins"] = sum(v == "win" for v in per_q.values())
    res["losses"] = sum(v == "loss" for v in per_q.values())
    res["evaluator"] = j.get("evaluator")
    (OUT / "harm_result.json").write_text(json.dumps(res, indent=1), encoding="utf-8")
    print(json.dumps(res, indent=1))


def main() -> int:
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("collect")
    c.add_argument("--url", required=True)
    s = sub.add_parser("score")
    s.add_argument("--judgements", type=Path, required=True)
    args = ap.parse_args()
    collect(args.url) if args.cmd == "collect" else score(args.judgements)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
