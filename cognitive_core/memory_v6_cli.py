"""CLI for Memory V6 extraction, queue inspection, and spatial indexing."""
from __future__ import annotations

import argparse
from pathlib import Path

from .extraction import AtomicMemoryExtractor
from .proposal_queue import MemoryProposalQueue
from .spatial_index import SpatialIndex


def root() -> Path:
    return Path(__file__).resolve().parent.parent


def main() -> None:
    parser = argparse.ArgumentParser(description="AI Memory Vault Memory V6 CLI")
    sub = parser.add_subparsers(dest="command", required=True)
    extract = sub.add_parser("extract")
    extract.add_argument("--text", required=True)
    extract.add_argument("--source-ref", default="cli:manual")
    extract.add_argument("--enqueue", action="store_true")
    index = sub.add_parser("index-repo")
    index.add_argument("--output", default="99_SYSTEM/spatial_index.json")
    query = sub.add_parser("query-path")
    query.add_argument("term")
    status = sub.add_parser("status")
    args = parser.parse_args()
    queue = MemoryProposalQueue(root() / "06_INBOX" / "memory_proposals.jsonl")

    if args.command == "extract":
        candidates = AtomicMemoryExtractor().extract(args.text, args.source_ref)
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


if __name__ == "__main__":
    main()
