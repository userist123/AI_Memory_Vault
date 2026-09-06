"""R016 — paired graph-on / graph-off measurement through the production path.

Unlike the v1 runner, this exercises `MemoryController.search()` itself:
the same controller, corpus, filters and principal that production uses. v1
built its own retriever out of `VaultIndex` plus lexical components and never
touched the controller, so its numbers described a parallel system.

The graph arm runs with `strict_graph_expansion=True`. A degraded expansion
raises instead of falling back to the unexpanded ranking, because a silent
fallback makes the graph arm identical to the baseline and turns "no
significant difference" into a statement about nothing.

Both sets are verified against their frozen canonical-LF hashes before any
case runs, and every gold id is checked to resolve in the corpus first — the
v1 failure was 48 cases whose gold resolved to nothing, giving a structural
zero that no configuration could move.
"""
from __future__ import annotations

import json
import os
import sys
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

from freeze import digest, hash_path  # noqa: E402

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller.controller import MemoryController  # noqa: E402
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
from retrieval.vault_index import VaultIndex  # noqa: E402


def verify_frozen() -> None:
    for name in ("heldout.json", "dev.json"):
        p = HERE / name
        recorded = hash_path(p).read_text(encoding="utf-8").strip()
        if digest(p) != recorded:
            raise SystemExit(f"FROZEN_SET_HASH_MISMATCH:{name}")


def load_cases(name: str) -> list[dict]:
    return json.loads((HERE / name).read_text(encoding="utf-8"))["cases"]


def build(index, storage, graph_on: bool) -> MemoryController:
    return MemoryController(
        storage=storage,
        index=index,
        enable_graph_expansion=graph_on,
        strict_graph_expansion=graph_on,
        # The shipped budget is unreachable in production: r004's 200-candidate
        # limit drives min(2*seeds, 20) - seeds negative, so expansion adds
        # nothing while reporting "ok". Measuring the graph at all requires an
        # explicit budget; the default stays untouched.
        graph_expansion_budget=10 if graph_on else None,
    )


def run_case(controller: MemoryController, case: dict, index) -> dict:
    pack = controller.search(Principal.HUMAN, case["query"], page_size=10)
    trace = pack.get("candidate_trace", {}) or {}
    # The controller records candidates as `fused_ranking`, a list of
    # {rank, id, fused_score, signals}. A first pass read a `candidate_ids`
    # key that does not exist, producing candidate_recall=0 alongside
    # context_recall=0.4 — impossible, since nothing reaches the context pack
    # without having been a candidate. The contradiction is what exposed it.
    candidates = {
        e.get("id") for e in (trace.get("fused_ranking") or []) if isinstance(e, dict)
    }
    candidates |= set(trace.get("graph_expanded_ids") or [])
    context = {r.get("id") for r in pack.get("results", []) if r.get("id")}
    gold = set(case["gold_relevant_notes"])

    blob = " ".join(
        index.by_id[n].text for n in context if n in index.by_id
    ).lower()
    facts_ok = all(f.lower() in blob for f in case["required_facts"]) if case["required_facts"] else False

    if case["abstain"]:
        correct = not (gold & context)
    else:
        correct = bool(gold & context) and facts_ok

    return {
        "id": case["id"],
        "class": case["class"],
        "candidate_recall": int(bool(gold & candidates)) if gold else 1,
        "context_recall": int(bool(gold & context)) if gold else 1,
        "answer_correctness": int(correct),
        "graph_status": trace.get("graph_expansion_status"),
        "expanded": len(trace.get("graph_expanded_ids") or []),
    }


def summarise(rows: list[dict]) -> dict:
    out = {}
    for klass in sorted({r["class"] for r in rows}):
        sub = [r for r in rows if r["class"] == klass]
        out[klass] = {
            "n": len(sub),
            "candidate_recall": sum(r["candidate_recall"] for r in sub) / len(sub),
            "context_recall": sum(r["context_recall"] for r in sub) / len(sub),
            "answer_correctness": sum(r["answer_correctness"] for r in sub) / len(sub),
        }
    out["ALL"] = {
        "n": len(rows),
        "candidate_recall": sum(r["candidate_recall"] for r in rows) / len(rows),
        "context_recall": sum(r["context_recall"] for r in rows) / len(rows),
        "answer_correctness": sum(r["answer_correctness"] for r in rows) / len(rows),
    }
    return out


def mcnemar(off: list[dict], on: list[dict], field: str) -> dict:
    """Discordant pairs only — the paired test the design calls for."""
    by_id = {r["id"]: r for r in off}
    b = c = 0
    for r in on:
        o = by_id.get(r["id"])
        if not o:
            continue
        if o[field] == 1 and r[field] == 0:
            b += 1
        elif o[field] == 0 and r[field] == 1:
            c += 1
    return {"off_only": b, "on_only": c, "discordant": b + c}


def main() -> int:
    verify_frozen()
    index = VaultIndex.load(REPO, include_raw=True, include_archived=True)
    storage = FileStorageEngine(str(REPO))

    cases = load_cases("heldout.json")
    unresolved = [
        (c["id"], g) for c in cases for g in c["gold_relevant_notes"] if g not in index.by_id
    ]
    if unresolved:
        raise SystemExit(f"GOLD_UNRESOLVABLE:{unresolved}")

    report = {"corpus_notes": len(index), "storage_notes": len(storage.id_to_path), "arms": {}}
    arms = {}
    for label, graph_on in (("graph_off", False), ("graph_on", True)):
        controller = build(index, storage, graph_on)
        rows, errors = [], []
        for case in cases:
            try:
                rows.append(run_case(controller, case, index))
            except Exception as exc:  # strict arm raises rather than degrading
                errors.append({"id": case["id"], "error": f"{type(exc).__name__}: {exc}"})
        arms[label] = rows
        report["arms"][label] = {
            "summary": summarise(rows) if rows else None,
            "errors": errors,
            "graph_status": dict(Counter(r["graph_status"] for r in rows)),
        }

    if arms["graph_off"] and arms["graph_on"]:
        report["mcnemar"] = {
            f: mcnemar(arms["graph_off"], arms["graph_on"], f)
            for f in ("candidate_recall", "context_recall", "answer_correctness")
        }

    (HERE / "r016_production_arms_report.json").write_text(
        json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n"
    )
    print(json.dumps(report, indent=2, ensure_ascii=False)[:3000])
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
