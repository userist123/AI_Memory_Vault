"""CLI for Memory V6: extraction, queue, spatial indexing, promotion, sleep-phase
consolidation, retrieval benchmarking, git/graph hooks, Obsidian rendering,
semantic (Qdrant) search, security audits, skill routing, and trading decisions."""
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
from .qdrant_retrieval import SemanticRetrieval
from .security_audit import SecurityAuditor
from .skill_router import SkillRouter


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
    extract.add_argument("--use-ollama", action="store_true")
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
    consolidate.add_argument("--render", action="store_true")
    consolidate.add_argument("--render-output", default="05_RESOURCES/Obsidian/Sleep_Consolidation_Report.md")

    render = sub.add_parser("render-report")
    render.add_argument("--input", default="04_MEMORY/sleep_consolidation_report.json")
    render.add_argument("--output", default="05_RESOURCES/Obsidian/Sleep_Consolidation_Report.md")

    benchmark = sub.add_parser("benchmark")
    benchmark.add_argument("--cases", default=str(Path("cognitive_core") / "benchmarks" / "sample_cases.jsonl"))
    benchmark.add_argument("--k", type=int, default=5)
    benchmark.add_argument("--retrieval", default="substring", choices=["substring", "graph", "semantic"])

    reindex = sub.add_parser("reindex-semantic")

    search_semantic = sub.add_parser("search-semantic")
    search_semantic.add_argument("query")
    search_semantic.add_argument("--top-k", type=int, default=10)

    audit = sub.add_parser("security-audit")
    audit.add_argument("--target", required=True)
    audit.add_argument("--output", default="04_MEMORY/security_audit_report.json")

    route = sub.add_parser("route-skill")
    route.add_argument("task")
    route.add_argument("--top-k", type=int, default=5)

    label_outcome = sub.add_parser("label-outcome")
    label_outcome.add_argument("--run-id", required=True)
    label_outcome.add_argument("--outcome", required=True, choices=["success", "failure", "partial", "unknown"])
    label_outcome.add_argument("--evidence", required=True)
    label_outcome.add_argument("--confidence", default="medium", choices=["low", "medium", "high"])
    label_outcome.add_argument("--labeled-by", default="human")
    label_outcome.add_argument("--output", default="04_MEMORY/outcome_events.jsonl")

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
        if args.retrieval == "graph":
            retrieval_fn = _graph_retrieval(controller, Principal.HUMAN)
        elif args.retrieval == "semantic":
            semantic = SemanticRetrieval(controller)
            retrieval_fn = lambda q: semantic.query(q, top_k=args.k)
        else:
            retrieval_fn = _naive_retrieval(controller)
        result = benchmark_suite.run(retrieval_fn, k=args.k)
        print(result["summary"])
    elif args.command == "reindex-semantic":
        controller, _ = _load_controller()
        count = SemanticRetrieval(controller).reindex()
        print(f"indexed={count}")
    elif args.command == "search-semantic":
        controller, _ = _load_controller()
        results = SemanticRetrieval(controller).query(args.query, top_k=args.top_k)
        for note_id in results:
            print(note_id)
    elif args.command == "security-audit":
        auditor = SecurityAuditor(args.target)
        report = auditor.run()
        output_path = root() / args.output
        output_path.parent.mkdir(parents=True, exist_ok=True)
        import json
        output_path.write_text(json.dumps(report.to_dict(), ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"files_scanned={report.files_scanned} findings={len(report.findings)} report={output_path}")
    elif args.command == "route-skill":
        router = SkillRouter(root() / ".agents" / "skills")
        for match in router.route(args.task, top_k=args.top_k):
            print(f"{match.score:.4f}  {match.skill}")
    elif args.command == "label-outcome":
        import json
        import uuid
        from datetime import datetime, timezone
        from .council_model_execution import OutcomeEvent

        event = OutcomeEvent(
            event_id=f"evt_{uuid.uuid4().hex[:12]}",
            run_id=args.run_id,
            timestamp=datetime.now(timezone.utc).isoformat(),
            outcome=args.outcome,
            source="human",
            confidence=args.confidence,
            evidence=args.evidence,
            labeled_by=args.labeled_by,
        )
        out_path = Path(args.output)
        if not out_path.is_absolute():
            out_path = root() / out_path
        out_path.parent.mkdir(parents=True, exist_ok=True)
        with open(out_path, "a", encoding="utf-8") as f:
            f.write(json.dumps(event.to_dict(), ensure_ascii=False) + "\n")
        print(f"event_id={event.event_id} run_id={event.run_id} outcome={event.outcome} source={event.source} evidence={event.evidence}")


if __name__ == "__main__":
    main()
