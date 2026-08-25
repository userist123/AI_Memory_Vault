"""CLI for Memory V6 extraction, queue inspection, spatial indexing, promotion,
sleep-phase consolidation, retrieval benchmarking, optional git/graph hooks,
and Obsidian report rendering."""
from __future__ import annotations

import argparse
import os
from pathlib import Path

from .extraction import AtomicMemoryExtractor
from .proposal_queue import MemoryProposalQueue
from .spatial_index import SpatialIndex
from .conflict_detector import ConflictDetector
from .queue_promoter import QueuePromoter
from .sleep_consolidation import SleepConsolidator
from .benchmarks.retrieval_benchmark import RetrievalBenchmark
from .git_hooks import PromotionGitHook
from .ranked_search import ranked_search
from .report_view import render_report_file


def root() -> Path:
    return Path(__file__).resolve().parent.parent


def _load_controller():
    from memory_controller.controller import MemoryController
    from memory_controller.authorizer import Principal
    from memory_controller.storage.file_engine import FileStorageEngine
    storage = FileStorageEngine(str(root()))
    return MemoryController(storage), Principal


def _naive_retrieval(controller):
    def _retrieve(query: str):
        needle = query.lower()
        return [
            note.get("id") for note in controller.storage.store.values()
            if needle in str(note.get("content", "")).lower()
            or needle in str(note.get("id", "")).lower()
        ]
    return _retrieve


def _graph_retrieval(controller, principal):
    def _retrieve(query: str):
        try:
            results = ranked_search(controller, principal, query, top_k=10)
            return [r.get("id") for r in results if r.get("id")]
        except Exception:
            return []
    return _retrieve


def main() -> None:
    parser = argparse.ArgumentParser(description="AI Memory Vault Memory V6 CLI")
    sub = parser.add_subparsers(dest="command", required=True)

    extract = sub.add_parser("extract")
    extract.add_argument("--text", required=True)
    extract.add_argument("--source-ref", default="cli:manual")
    extract.add_argument("--enqueue", action="store_true")
    extract.add_argument("--use-ollama", action="store_true",
                          help="Augment deterministic extraction with a local Ollama model")
    extract.add_argument("--ollama-model", default="llama3.1")
    extract.add_argument("--ollama-host", default="http://localhost:11434")

    index = sub.add_parser("index-repo")
    index.add_argument("--output", default="99_SYSTEM/spatial_index.json")

    query = sub.add_parser("query-path")
    query.add_argument("term")

    sub.add_parser("status")

    review = sub.add_parser("review")
    review.add_argument("--show-conflicts", action="store_true")

    approve = sub.add_parser("approve")
    approve.add_argument("candidate_id")
    approve.add_argument("--reviewer", default="human")

    reject = sub.add_parser("reject")
    reject.add_argument("candidate_id")
    reject.add_argument("--reviewer", default="human")

    promote = sub.add_parser("promote-approved")
    promote.add_argument("--principal", default="ai_agent", choices=["human", "admin", "ai_agent"])

    consolidate = sub.add_parser("consolidate")
    consolidate.add_argument("--output", default="04_MEMORY/sleep_consolidation_report.json")
    consolidate.add_argument("--dormant-days", type=int, default=60)
    consolidate.add_argument("--stale-review-days", type=int, default=14)
    consolidate.add_argument("--render", action="store_true", help="Also render an Obsidian Markdown view")
    consolidate.add_argument("--render-output", default="05_RESOURCES/Obsidian/Sleep_Consolidation_Report.md")

    render = sub.add_parser("render-report")
    render.add_argument("--input", default="04_MEMORY/sleep_consolidation_report.json")
    render.add_argument("--output", default="05_RESOURCES/Obsidian/Sleep_Consolidation_Report.md")

    benchmark = sub.add_parser("benchmark")
    benchmark.add_argument("--cases", default=str(Path("cognitive_core") / "benchmarks" / "sample_cases.jsonl"))
    benchmark.add_argument("--k", type=int, default=5)
    benchmark.add_argument("--retrieval", default="substring", choices=["substring", "graph"])

    args = parser.parse_args()
    queue = MemoryProposalQueue(root() / "06_INBOX" / "memory_proposals.jsonl")

    if args.command == "extract":
        local_llm = None
        if args.use_ollama:
            from .ollama_extractor import OllamaExtractionAdapter
            local_llm = OllamaExtractionAdapter(model=args.ollama_model, host=args.ollama_host)
        candidates = AtomicMemoryExtractor(local_llm=local_llm).extract(args.text, args.source_ref)
        for item in candidates:
            print(f"{item.type}: {item.content}")
        if args.enqueue:
            print(f"queued={queue.enqueue(candidates)}")
    elif args.command == "index-repo":
        indexer = SpatialIndex(root())
        indexer.rebuild()
        print(indexer.save(root() / args.output))
    elif args.command == "query-path":
        indexer = SpatialIndex(root())
        indexer.rebuild()
        for node in indexer.query_path(args.term):
            print(node["path"])
    elif args.command == "status":
        print(queue.status())
    elif args.command == "review":
        pending = queue.pending()
        conflicts = {}
        if args.show_conflicts:
            controller, _ = _load_controller()
            promoter = QueuePromoter(queue, controller, None, ConflictDetector())
            conflicts = promoter.scan_conflicts()
        for record in pending:
            flag = conflicts.get(record["candidate_id"])
            marker = f" [CONFLICT x{len(flag)}]" if flag else ""
            print(f"{record['candidate_id']} | {record['type']} | {record['content']}{marker}")
    elif args.command == "approve":
        queue.mark(args.candidate_id, "APPROVED", reviewer=args.reviewer)
        print(f"approved={args.candidate_id}")
    elif args.command == "reject":
        queue.mark(args.candidate_id, "REJECTED", reviewer=args.reviewer)
        print(f"rejected={args.candidate_id}")
    elif args.command == "promote-approved":
        controller, Principal = _load_controller()
        principal_map = {"human": Principal.HUMAN, "admin": Principal.ADMIN, "ai_agent": Principal.AI_AGENT}
        promoter = QueuePromoter(queue, controller, principal_map[args.principal])
        ids = promoter.promote_approved()
        print(f"promoted={ids}")
        if ids and os.getenv("VAULT_GIT_AUTO_COMMIT", "0") == "1":
            hook = PromotionGitHook(repo_path=str(root()))
            note_paths = []
            path_fn = getattr(controller.storage, "note_path", None)
            if callable(path_fn):
                for note_id in ids:
                    resolved = path_fn(note_id)
                    if resolved:
                        note_paths.append(resolved)
            result = hook.commit_promotion(ids, note_paths)
            print(f"git_commit={result}")
    elif args.command == "consolidate":
        controller, _ = _load_controller()
        consolidator = SleepConsolidator(
            controller, dormant_days=args.dormant_days, stale_review_days=args.stale_review_days,
        )
        path = consolidator.save_report(root() / args.output)
        print(f"report={path}")
        if args.render:
            rendered = render_report_file(path, root() / args.render_output)
            print(f"rendered={rendered}")
    elif args.command == "render-report":
        rendered = render_report_file(root() / args.input, root() / args.output)
        print(f"rendered={rendered}")
    elif args.command == "benchmark":
        controller, Principal = _load_controller()
        cases_path = Path(args.cases)
        if not cases_path.is_absolute():
            cases_path = root() / cases_path
        benchmark_suite = RetrievalBenchmark.load_jsonl(cases_path)
        retrieval_fn = _graph_retrieval(controller, Principal.HUMAN) if args.retrieval == "graph" else _naive_retrieval(controller)
        result = benchmark_suite.run(retrieval_fn, k=args.k)
        print(result["summary"])


if __name__ == "__main__":
    main()
