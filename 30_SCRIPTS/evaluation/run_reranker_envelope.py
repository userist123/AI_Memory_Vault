"""Reranker cost envelope: does dense reordering of the fusion pool move gold notes onto the page, and at what cost.

Preregistered in 07_EVALUATION/reranker_envelope/PREREGISTRATION.md, committed
before this script existed. Every number in REPORT.md is read from
results.json; none is written by hand. DEVIATIONS.md records two earlier
versions of this harness that were void, and why.

The arms run *inside* production. The controller's sort key is replaced, on the
module object the controller actually uses, by one that ranks the first K of the
fusion order by cosine similarity (nomic-embed-text, local Ollama) and leaves
the rest in fusion order; the real `search()` then paginates and packs exactly
as it does today. An arm's page is therefore the page production would return
with that reranker wired in — including the context-pack budget drops that
make the page shorter than five on many queries.

    python 30_SCRIPTS/evaluation/run_reranker_envelope.py            # v3, all arms, controls
    python 30_SCRIPTS/evaluation/run_reranker_envelope.py --render   # REPORT.md from results.json
    python 30_SCRIPTS/evaluation/run_reranker_envelope.py --heldout  # one run of the qualifying arm on v2
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
import os
import random
import statistics
import sys
import time
import urllib.request
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional, Sequence

REPO = Path(__file__).resolve().parents[2]
BENCH = REPO / "07_EVALUATION" / "retrieval_benchmark_v3" / "retrieval_benchmark_v3.json"
BENCH_SHA = BENCH.with_suffix(".json.sha256")
HELDOUT = REPO / "07_EVALUATION" / "heldout_retrieval_benchmark_v2" / "heldout.json"
HELDOUT_SHA = HELDOUT.with_suffix(".json.sha256")
OUT_DIR = REPO / "07_EVALUATION" / "reranker_envelope"
RESULTS = OUT_DIR / "results.json"
REPORT = OUT_DIR / "REPORT.md"
HELDOUT_RESULT = OUT_DIR / "heldout_confirmation.json"
CACHE = REPO / ".cache" / "reranker_envelope" / "nomic-embed-text.json"

MODEL = "nomic-embed-text"
OLLAMA = os.getenv("OLLAMA_HOST", "http://127.0.0.1:11434").rstrip("/")
PAGE = 5
TEXT_LIMIT = 2000
ARMS = {"embed_top20": 20, "embed_top50": 50, "embed_top200": 200, "rrf_top50": 50}
LATENCY_BUDGET_MS = 200.0
RRF_K = 60

sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)
from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller.controller import MemoryController  # noqa: E402
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
from retrieval.vault_index import VaultIndex  # noqa: E402

#: The module whose `_ranking_key_fn` the controller calls. The compatibility shim
#: imports the same file under two names; patching the other one changes nothing.
CTRL_MOD = sys.modules[MemoryController.__module__]

# The statistics are the ranking experiment's, loaded by path so there is one definition.
_spec = importlib.util.spec_from_file_location("ranking_arms", REPO / "30_SCRIPTS" / "evaluation" / "run_ranking_arm_experiment.py")
_rank = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_rank)
wilson, exact_mcnemar_p, holm, paired = _rank.wilson, _rank.exact_mcnemar_p, _rank.holm, _rank.paired

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")


# --- embeddings --------------------------------------------------------------

class Embedder:
    """nomic-embed-text through Ollama, with a disk cache keyed by sha256(model + text)."""

    def __init__(self) -> None:
        self.cache: Dict[str, List[float]] = {}
        if CACHE.exists():
            self.cache = json.loads(CACHE.read_text(encoding="utf-8"))
        self.calls = 0
        self.call_seconds = 0.0

    @staticmethod
    def key(text: str) -> str:
        return hashlib.sha256((MODEL + "\x00" + text).encode("utf-8")).hexdigest()

    def _call(self, texts: Sequence[str]) -> List[List[float]]:
        body = json.dumps({"model": MODEL, "input": list(texts)}).encode("utf-8")
        req = urllib.request.Request(f"{OLLAMA}/api/embed", data=body, headers={"Content-Type": "application/json"})
        t0 = time.perf_counter()
        with urllib.request.urlopen(req, timeout=300) as r:
            out = json.loads(r.read().decode("utf-8"))["embeddings"]
        self.calls += 1
        self.call_seconds += time.perf_counter() - t0
        return out

    def live(self, text: str) -> List[float]:
        """Uncached, so the cost measured is the cost a caller pays."""
        return self._call([text])[0]

    def cached(self, texts: Sequence[str], batch: int = 32) -> List[List[float]]:
        missing = [t for t in dict.fromkeys(texts) if self.key(t) not in self.cache]
        for i in range(0, len(missing), batch):
            chunk = missing[i:i + batch]
            for t, v in zip(chunk, self._call(chunk)):
                self.cache[self.key(t)] = v
        if missing:
            CACHE.parent.mkdir(parents=True, exist_ok=True)
            CACHE.write_text(json.dumps(self.cache), encoding="utf-8")
        return [self.cache[self.key(t)] for t in texts]


def cosine(a: Sequence[float], b: Sequence[float]) -> float:
    dot = sum(x * y for x, y in zip(a, b))
    na = math.sqrt(sum(x * x for x in a)) or 1.0
    nb = math.sqrt(sum(y * y for y in b)) or 1.0
    return dot / (na * nb)


def note_text(note: dict) -> str:
    title = str(note.get("title") or note.get("id") or "")
    return (title + "\n" + str(note.get("content") or ""))[:TEXT_LIMIT]


# --- the arms, as sort orders ---------------------------------------------

def reorder(pool: List[str], scores: Dict[str, float], k: int, rrf: bool) -> List[str]:
    """The arm's order: the first k ids of the pool reordered, positions after k untouched."""
    head, tail = pool[:k], pool[k:]
    if rrf:
        fusion_rank = {nid: i + 1 for i, nid in enumerate(head)}
        by_cos = sorted(head, key=lambda n: (-scores[n], n))
        cos_rank = {nid: i + 1 for i, nid in enumerate(by_cos)}
        key = lambda n: (-(1 / (RRF_K + fusion_rank[n]) + 1 / (RRF_K + cos_rank[n])), n)
    else:
        key = lambda n: (-scores[n], n)
    return sorted(head, key=key) + tail


class PatchedKey:
    """Make the controller sort by a given order, for one search call.

    `sorted(notes, key=..., reverse=True)` in the controller gets `-position`, so
    position 0 comes first. Ids not in the order (there should be none) go last.
    """

    def __init__(self, order: Optional[List[str]]):
        self.index = None if order is None else {nid: i for i, nid in enumerate(order)}

    def __enter__(self):
        self.original = CTRL_MOD._ranking_key_fn
        if self.index is not None:
            idx = self.index
            CTRL_MOD._ranking_key_fn = lambda *a, **k: (lambda n: -idx.get(n.get("id"), 10 ** 9))
        return self

    def __exit__(self, *exc):
        CTRL_MOD._ranking_key_fn = self.original


def search_page(storage, index, query: str, order: Optional[List[str]]) -> dict:
    """A fresh controller per call, so no result cache can hand back a previous order."""
    controller = MemoryController(storage=storage, index=index, enable_graph_expansion=False)
    with PatchedKey(order):
        t0 = time.perf_counter()
        pack = controller.search(Principal.AI_AGENT, query, page_size=PAGE, enable_graph_expansion=False)
        elapsed = (time.perf_counter() - t0) * 1000.0
    trace = pack.get("candidate_trace", {}) or {}
    pool = [e["id"] for e in sorted((e for e in (trace.get("fused_ranking") or []) if isinstance(e, dict) and e.get("id")),
                                    key=lambda e: int(e.get("rank", 0)))]
    return {"page": [r.get("id") for r in pack.get("results", []) if r.get("id")], "pool": pool, "search_ms": elapsed}


# --- one benchmark -----------------------------------------------------------

def load_cases(path: Path, sha_path: Path) -> List[dict]:
    expected = sha_path.read_text(encoding="utf-8").split()[0]
    actual = hashlib.sha256(path.read_bytes()).hexdigest()
    if actual != expected:
        raise SystemExit(f"FROZEN_BENCHMARK_HASH_MISMATCH {path.name}: {actual} != {expected}")
    data = json.loads(path.read_text(encoding="utf-8"))
    cases = data["cases"] if isinstance(data, dict) else data
    out = []
    for c in cases:
        gold = c.get("gold_relevant_notes") or c.get("gold_ids") or []
        if c.get("abstain") or not gold:
            continue
        out.append({"id": c["id"], "query": c["query"], "gold": set(gold), "class": c.get("class", "")})
    return out


def collect_pools(storage, index, cases: List[dict]) -> List[dict]:
    """Production's page and fusion pool per case, plus the premise check.

    The premise: patching the key with the fusion order itself must reproduce
    production's page exactly. That is the no-op finding re-asserted through the
    same mechanism the arms use; a mismatch would void every arm.
    """
    rows = []
    for c in cases:
        prod = search_page(storage, index, c["query"], None)
        identity = search_page(storage, index, c["query"], prod["pool"])
        gold_rank = next((i + 1 for i, nid in enumerate(prod["pool"]) if nid in c["gold"]), None)
        rows.append({**c, "pool": prod["pool"], "production_page": prod["page"], "production_search_ms": prod["search_ms"],
                     "gold_fused_rank": gold_rank, "identity_patch_reproduces_production": identity["page"] == prod["page"],
                     "page_is_fusion_top5": prod["page"] == prod["pool"][:PAGE],
                     "page_length": len(prod["page"])})
    return rows


def row(r: dict, page: List[str]) -> dict:
    return {"id": r["id"], "class": r["class"], "order": page,
            "context_recall": int(bool(r["gold"] & set(page))),
            "candidate_recall": int(bool(r["gold"] & set(r["pool"])))}


def run_benchmark(rows: List[dict], storage, index, embedder: Embedder, arms: Dict[str, int]) -> Dict[str, Any]:
    texts: Dict[str, str] = {}
    for r in rows:
        for nid in r["pool"]:
            if nid not in texts:
                note = storage.get(nid) or {}
                texts[nid] = note_text(note) if note else nid
    t0 = time.perf_counter()
    calls_before = embedder.calls
    vectors = dict(zip(texts.keys(), embedder.cached(list(texts.values()))))
    corpus_cost = {"notes_embedded": len(texts), "wall_seconds": round(time.perf_counter() - t0, 2),
                   "embedding_calls": embedder.calls - calls_before}

    results: Dict[str, Any] = {"corpus": corpus_cost, "arms": {}, "controls": {}}
    rng = random.Random(20261010)
    production_rows = [row(r, r["production_page"]) for r in rows]
    results["arms"]["production"] = {"rows": production_rows, "latency_ms": None,
                                     "search_ms_p50": round(statistics.median(r["production_search_ms"] for r in rows), 1)}

    for arm, k in arms.items():
        rrf = arm.startswith("rrf")
        arm_rows, sab_rows, added, search_ms = [], [], [], []
        for r in rows:
            head = r["pool"][:k]
            t0 = time.perf_counter()
            q = embedder.live(r["query"])
            scores = {nid: cosine(q, vectors[nid]) for nid in head}
            order = reorder(r["pool"], scores, k, rrf)
            added.append((time.perf_counter() - t0) * 1000.0)
            res = search_page(storage, index, r["query"], order)
            search_ms.append(res["search_ms"])
            arm_rows.append(row(r, res["page"]))
            random_scores = {nid: rng.random() for nid in head}
            sab = search_page(storage, index, r["query"], reorder(r["pool"], random_scores, k, rrf))
            sab_rows.append(row(r, sab["page"]))
        a_sorted = sorted(added)
        results["arms"][arm] = {
            "k": k, "rows": arm_rows,
            "latency_ms": {"p50": round(statistics.median(added), 1),
                           "p95": round(a_sorted[int(0.95 * (len(a_sorted) - 1))], 1),
                           "mean": round(statistics.fmean(added), 1), "embedding_calls_per_query": 1},
            "search_ms_p50": round(statistics.median(search_ms), 1),
        }
        changed = _rank.cases_with_different_order(arm_rows, sab_rows)
        results["controls"][arm] = {
            "sabotage_changed_cases": len(changed),
            "sabotage_proves_arm_is_live": len(changed) > 0,
            "random_score_arm": {"rows": sab_rows, "paired_vs_production": paired(production_rows, sab_rows)},
        }
    return results


def determinism_control(embedder: Embedder, rows: List[dict]) -> Dict[str, Any]:
    texts = [r["query"] for r in rows[:20]]
    runs = [embedder._call(texts) for _ in range(3)]
    return {"texts": len(texts), "runs": 3, "byte_identical": runs[0] == runs[1] == runs[2]}


# --- decision ----------------------------------------------------------------

def decide(results: Dict[str, Any], rows: List[dict]) -> Dict[str, Any]:
    prod = results["arms"]["production"]["rows"]
    reachable = {r["id"] for r in rows if r["gold_fused_rank"] is not None and r["gold_fused_rank"] > PAGE}
    premise_ok = all(r["identity_patch_reproduces_production"] for r in rows)
    comparisons, holm_in = {}, {}
    for arm in ARMS:
        cmp = paired(prod, results["arms"][arm]["rows"])
        cmp["gains_outside_reachable_set"] = sorted(set(cmp["winning_cases"]) - reachable)
        comparisons[arm] = cmp
        holm_in[arm] = cmp["exact_mcnemar_p"]
    holm_table = holm(holm_in)
    per_arm = {}
    for arm, cmp in comparisons.items():
        ctrl = results["controls"][arm]
        rand = ctrl["random_score_arm"]["paired_vs_production"]
        random_qualifies = rand["gained"] >= 8 and rand["lost"] <= 2 and rand["exact_mcnemar_p"] < 0.05
        criteria = {
            "gains_ge_8": cmp["gained"] >= 8, "losses_le_2": cmp["lost"] <= 2,
            "mcnemar_p_lt_0_05": cmp["exact_mcnemar_p"] < 0.05,
            "holm_p_lt_0_05": holm_table[arm]["p_holm"] < 0.05,
            "arm_is_live": ctrl["sabotage_proves_arm_is_live"],
            "random_scores_do_not_qualify": not random_qualifies,
            "gains_only_from_reachable_cases": not cmp["gains_outside_reachable_set"],
            "premise_holds_in_every_case": premise_ok,
        }
        p95 = results["arms"][arm]["latency_ms"]["p95"]
        per_arm[arm] = {"criteria": criteria, "qualifies": all(criteria.values()),
                        "p95_added_latency_ms": p95, "within_latency_budget": p95 < LATENCY_BUDGET_MS}
    qualifying = [a for a, v in per_arm.items() if v["qualifies"]]
    winner = None
    if qualifying:
        winner = sorted(qualifying, key=lambda a: (ARMS[a], a.startswith("rrf")))[0]
        if "rrf_top50" in qualifying and "embed_top50" in qualifying and winner == "embed_top50":
            e, r = comparisons["embed_top50"], comparisons["rrf_top50"]
            if (r["gained"] - r["lost"]) > (e["gained"] - e["lost"]):
                winner = "rrf_top50"
    verdict = ("no reranker recommended on dense similarity from this model" if not winner else
               f"{winner}: effective and within budget — candidate for wiring" if per_arm[winner]["within_latency_budget"] else
               f"{winner}: effective, too slow ({per_arm[winner]['p95_added_latency_ms']} ms p95)")
    return {"reference_arm": "production", "reachable_cases": len(reachable), "premise_holds_in_every_case": premise_ok,
            "comparisons": comparisons, "holm": holm_table, "per_arm": per_arm, "qualifying": qualifying,
            "winner": winner, "latency_budget_ms": LATENCY_BUDGET_MS, "verdict": verdict}


# --- report ------------------------------------------------------------------

def summarise(rows: List[dict]) -> Dict[str, Any]:
    k = sum(r["context_recall"] for r in rows)
    return {"k": k, "n": len(rows), "text": f"{k}/{len(rows)}", "ci": wilson(k, len(rows))}


def ci_text(s: Dict[str, Any]) -> str:
    return f"{s['ci']['proportion']*100:.2f}% [{s['ci']['lower']*100:.2f}, {s['ci']['upper']*100:.2f}]"


def render(r: Dict[str, Any]) -> str:
    L: List[str] = []
    a = L.append
    a("# Reranker cost envelope — results")
    a("")
    a(f"Preregistration: `07_EVALUATION/reranker_envelope/PREREGISTRATION.md`, committed before this run; deviations in "
      f"`DEVIATIONS.md`. Benchmark v3 SHA-256 `{r['benchmark_sha256']}`, {r['n_cases']} measurable cases, model `{MODEL}` via local Ollama.")
    a("")
    a("### [Operating point: Principal.AI_AGENT, page_size=5, Floor: ACTIV, graph expansion off]")
    a("")
    a("The arms run inside `search()`: only the sort key changes, and pagination and the context-pack budget apply as in production.")
    a("")
    a("| premise | value |")
    a("|---|---|")
    a(f"| identity-order patch reproduces production's page | {r['premise_cases_ok']}/{r['n_cases']} (must be {r['n_cases']}) |")
    a(f"| production's page equals the fusion top-5 | {r['page_is_fusion_top5_cases']}/{r['n_cases']} — the rest lost notes to the pack budget |")
    a(f"| production's page shorter than 5 / empty | {r['short_pages']} / {r['empty_pages']} |")
    a(f"| gold note anywhere in the fusion pool (mean size {r['mean_pool_size']}) | {r['gold_in_pool']}/{r['n_cases']} |")
    a(f"| gold in the pool below rank 5 — reachable by reordering | **{r['decision']['reachable_cases']}** |")
    a("")
    a("| arm | K | context_recall@5 | 95% CI | gains | losses | McNemar p | Holm p | live (sabotage Δ) | added p50 ms | added p95 ms | search p50 ms |")
    a("|---|---|---|---|---|---|---|---|---|---|---|---|")
    s = summarise(r["arms"]["production"]["rows"])
    a(f"| `production` | — | {s['text']} | {ci_text(s)} | — | — | — | — | — | — | — | {r['arms']['production']['search_ms_p50']} |")
    for arm in ARMS:
        s = summarise(r["arms"][arm]["rows"]); c = r["decision"]["comparisons"][arm]; h = r["decision"]["holm"][arm]
        lat = r["arms"][arm]["latency_ms"]; ctrl = r["controls"][arm]
        a(f"| `{arm}` | {ARMS[arm]} | {s['text']} | {ci_text(s)} | {c['gained']} | {c['lost']} | {c['exact_mcnemar_p']} | {h['p_holm']} "
          f"| {ctrl['sabotage_changed_cases']} | {lat['p50']} | {lat['p95']} | {r['arms'][arm]['search_ms_p50']} |")
    a("")
    a("### Controls")
    a("")
    d = r["determinism"]
    a(f"- Embedding service determinism: {d['texts']} texts × {d['runs']} calls, byte-identical: **{d['byte_identical']}**.")
    for arm in ARMS:
        rand = r["controls"][arm]["random_score_arm"]["paired_vs_production"]
        q = rand["gained"] >= 8 and rand["lost"] <= 2 and rand["exact_mcnemar_p"] < 0.05
        a(f"- `{arm}` with random scores: gains {rand['gained']}, losses {rand['lost']}, p = {rand['exact_mcnemar_p']} — "
          f"{'would qualify: rule too loose' if q else 'does not qualify'}.")
        out = r["decision"]["comparisons"][arm]["gains_outside_reachable_set"]
        if out:
            a(f"  - **gains outside the reachable set: {out}** — harness defect, result void.")
    a("")
    a("### Cost")
    a("")
    cc = r["corpus"]
    a(f"- One-time: {cc['notes_embedded']} notes embedded in {cc['wall_seconds']} s over {cc['embedding_calls']} batched calls "
      f"(0 calls means the cache already held them); cache {r['cache_bytes']:,} bytes on disk.")
    a(f"- Per query: added = live query embedding + K cosines, 1 embedding call. Budget: p95 < {r['decision']['latency_budget_ms']} ms. "
      f"`search p50` is the whole call, for scale.")
    a("")
    a("### Decision")
    a("")
    a("| arm | gains ≥ 8 | losses ≤ 2 | p < 0.05 | Holm < 0.05 | live | random does not qualify | gains reachable | premise | qualifies | within budget |")
    a("|---|---|---|---|---|---|---|---|---|---|---|")
    for arm, v in r["decision"]["per_arm"].items():
        c = v["criteria"]
        a(f"| `{arm}` | {c['gains_ge_8']} | {c['losses_le_2']} | {c['mcnemar_p_lt_0_05']} | {c['holm_p_lt_0_05']} | {c['arm_is_live']} "
          f"| {c['random_scores_do_not_qualify']} | {c['gains_only_from_reachable_cases']} | {c['premise_holds_in_every_case']} "
          f"| **{v['qualifies']}** | {v['within_latency_budget']} |")
    a("")
    a(f"**Verdict: {r['decision']['verdict']}.**")
    a("")
    if r.get("heldout"):
        h = r["heldout"]
        a("### Held-out confirmation (v2, one run)")
        a("")
        a(f"Arm `{h['arm']}` vs production on {h['n_cases']} cases (SHA `{h['sha256']}`): production {h['production']['text']}, "
          f"arm {h['arm_recall']['text']}; gains {h['paired']['gained']}, losses {h['paired']['lost']}, McNemar p = {h['paired']['exact_mcnemar_p']}.")
        a("")
    a("*Every figure above is read from `results.json`.*")
    return "\n".join(L) + "\n"


# --- main --------------------------------------------------------------------

def load_vault():
    cwd = os.getcwd(); os.chdir(REPO)
    try:
        index = VaultIndex.load(Path("."), include_raw=True, include_archived=True)
        storage = FileStorageEngine(".")
    finally:
        os.chdir(cwd)
    return storage, index


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--render", action="store_true")
    ap.add_argument("--heldout", action="store_true")
    args = ap.parse_args()

    if args.render:
        REPORT.write_text(render(json.loads(RESULTS.read_text(encoding="utf-8"))), encoding="utf-8", newline="\n")
        print(f"written {REPORT.relative_to(REPO)}"); return 0

    storage, index = load_vault()
    embedder = Embedder()

    if args.heldout:
        r = json.loads(RESULTS.read_text(encoding="utf-8"))
        winner = r["decision"]["winner"]
        if not winner:
            print("NO_QUALIFYING_ARM: the held-out set is not touched"); return 0
        if HELDOUT_RESULT.exists():
            print("HELDOUT_ALREADY_RUN: one run only, by the preregistration"); return 1
        cases = load_cases(HELDOUT, HELDOUT_SHA)
        rows = collect_pools(storage, index, cases)
        res = run_benchmark(rows, storage, index, embedder, {winner: ARMS[winner]})
        prod, arm_rows = res["arms"]["production"]["rows"], res["arms"][winner]["rows"]
        h = {"arm": winner, "n_cases": len(rows), "sha256": HELDOUT_SHA.read_text(encoding="utf-8").split()[0],
             "premise_cases_ok": sum(x["identity_patch_reproduces_production"] for x in rows),
             "production": summarise(prod), "arm_recall": summarise(arm_rows), "paired": paired(prod, arm_rows),
             "rows": {"production": prod, winner: arm_rows}}
        HELDOUT_RESULT.write_text(json.dumps(h, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
        r["heldout"] = {k: v for k, v in h.items() if k != "rows"}
        RESULTS.write_text(json.dumps(r, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
        REPORT.write_text(render(r), encoding="utf-8", newline="\n")
        print(f"HELDOUT {winner}: production {h['production']['text']} arm {h['arm_recall']['text']} "
              f"gains={h['paired']['gained']} losses={h['paired']['lost']} p={h['paired']['exact_mcnemar_p']}")
        return 0

    cases = load_cases(BENCH, BENCH_SHA)
    print(f"CASES={len(cases)}")
    rows = collect_pools(storage, index, cases)
    premise_ok = sum(x["identity_patch_reproduces_production"] for x in rows)
    print(f"POOLS collected; identity patch reproduces production in {premise_ok}/{len(rows)}; "
          f"page==fusion_top5 in {sum(x['page_is_fusion_top5'] for x in rows)}/{len(rows)}; "
          f"short pages {sum(1 for x in rows if x['page_length'] < PAGE)}, empty {sum(1 for x in rows if x['page_length'] == 0)}")
    determinism = determinism_control(embedder, rows)
    print(f"DETERMINISM byte_identical={determinism['byte_identical']}")
    res = run_benchmark(rows, storage, index, embedder, ARMS)
    for arm in ARMS:
        s = summarise(res["arms"][arm]["rows"]); lat = res["arms"][arm]["latency_ms"]
        print(f"{arm:14} recall={s['text']:>7} sabotage_delta={res['controls'][arm]['sabotage_changed_cases']:3} "
              f"added p50={lat['p50']}ms p95={lat['p95']}ms search p50={res['arms'][arm]['search_ms_p50']}ms")
    results = {
        "schema": "reranker-envelope.v2", "model": MODEL,
        "benchmark_sha256": BENCH_SHA.read_text(encoding="utf-8").split()[0], "n_cases": len(rows),
        "premise_cases_ok": premise_ok,
        "page_is_fusion_top5_cases": sum(x["page_is_fusion_top5"] for x in rows),
        "short_pages": sum(1 for x in rows if x["page_length"] < PAGE),
        "empty_pages": sum(1 for x in rows if x["page_length"] == 0),
        "gold_in_pool": sum(1 for x in rows if x["gold_fused_rank"] is not None),
        "mean_pool_size": round(statistics.fmean(len(x["pool"]) for x in rows), 2),
        "determinism": determinism, "corpus": res["corpus"], "arms": res["arms"], "controls": res["controls"],
        "cache_bytes": CACHE.stat().st_size if CACHE.exists() else 0,
        "pools": [{"id": x["id"], "gold_fused_rank": x["gold_fused_rank"], "pool_size": len(x["pool"]),
                   "page_length": x["page_length"]} for x in rows],
    }
    results["decision"] = decide(results, rows)
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    RESULTS.write_text(json.dumps(results, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    REPORT.write_text(render(results), encoding="utf-8", newline="\n")
    print(f"VERDICT={results['decision']['verdict']}")
    print(f"written {RESULTS.relative_to(REPO)} and {REPORT.relative_to(REPO)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
