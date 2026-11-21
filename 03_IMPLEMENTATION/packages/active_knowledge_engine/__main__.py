"""CLI: ``python -m active_knowledge_engine run --dry-run [--vault-root PATH]``.

Milestone 1 supports the dry run only. Exit codes: 0 = COMPLETED, 3 = COMPLETED_WITH_SKIPS
(storage could not read some note files; listed in the record), 1 = FAILED or STALE_SNAPSHOT,
2 = usage (no --dry-run).
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(prog="active_knowledge_engine")
    sub = parser.add_subparsers(dest="command", required=True)
    run = sub.add_parser("run", help="read-only shadow evaluation of the memory")
    run.add_argument("--dry-run", action="store_true", help="required: Milestone 1 never writes memory")
    run.add_argument("--vault-root", default=str(REPO_ROOT))
    run.add_argument("--output-dir", default=None,
                     help="default: <vault-root>/08_OBSERVABILITY/active_knowledge_engine")
    run.add_argument("--trigger", default="manual", choices=["manual", "scheduled", "ci"])
    run.add_argument("--dormant-days", type=int, default=60)
    run.add_argument("--stale-review-days", type=int, default=14)
    run.add_argument("--max-items", type=int, default=None)
    args = parser.parse_args(argv)

    if not args.dry_run:
        print("only --dry-run is supported in AKE Milestone 1 (no memory writes)", file=sys.stderr)
        return 2

    # keep the controller's audit log and telemetry out of the caller's working directory,
    # exactly as the MCP server and the other CLIs do
    from interfaces import vault_runtime
    vault_runtime.configure_runtime_dirs()

    from memory_controller.controller import MemoryController
    from memory_controller.storage.file_engine import FileStorageEngine

    from .runner import AKEShadowRunner, JobStatus

    vault = Path(args.vault_root).resolve()
    out = Path(args.output_dir) if args.output_dir else vault / "08_OBSERVABILITY" / "active_knowledge_engine"
    controller = MemoryController(storage=FileStorageEngine(str(vault)))
    runner = AKEShadowRunner(controller, output_dir=out, vault_root=vault, dormant_days=args.dormant_days,
                             stale_review_days=args.stale_review_days, max_items=args.max_items)
    record = runner.run(trigger=args.trigger)
    print(f"record={runner.output_dir / (record['job_id'] + '.json')}")
    print(f"status={record['status']} reused={record['reused_existing_record']} "
          f"skipped={record['storage']['skipped_count']}")
    for stage in record["stages"]:
        count = stage.get("output", {}).get("case_count")
        print(f"stage={stage['name']} status={stage['status']}" + (f" cases={count}" if count is not None else ""))
    return {JobStatus.COMPLETED.value: 0, JobStatus.COMPLETED_WITH_SKIPS.value: 3}.get(record["status"], 1)


if __name__ == "__main__":
    sys.exit(main())
