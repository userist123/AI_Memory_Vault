"""CLI: ``python -m active_knowledge_engine run --dry-run [--vault-root PATH]``.

Milestone 1 supports the dry run only. Exit codes: 0 = COMPLETED, 3 = COMPLETED_WITH_SKIPS
(storage could not read some note files; listed in the record), 1 = FAILED or STALE_SNAPSHOT,
2 = usage (no --dry-run) or missing HMAC secret when a benchmark runs.

``python -m active_knowledge_engine split-plan --note-id ID`` writes a dry-run split manifest
(exit 0 = PLANNED; 1 = NEEDS_PROVENANCE, UNSPLITTABLE, DOES_NOT_FIT or COVERAGE_FAILED).

``python -m active_knowledge_engine apply-split --manifest PLAN.json [--execute]`` is the OWNER
step for a PLANNED split: without --execute it validates and lists; with --execute it needs an
interactive terminal and the parent id typed back (exit 0 = APPLIED, 1 = FAILED, 2 = refused).

``python -m active_knowledge_engine compare BASELINE.json CANDIDATE.json`` compares the
retrieval_quality stage of two job records (exact McNemar). Exit codes: 0 = NO_CHANGE,
IMPROVED or INCONCLUSIVE, 4 = REGRESSED, 2 = NOT_COMPARABLE.
"""
from __future__ import annotations

import argparse
import json
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
    run.add_argument("--benchmark", default=None,
                     help="frozen benchmark JSON with a .sha256 sidecar; default: held-out v2 under the vault root, if present")
    run.add_argument("--no-benchmark", action="store_true", help="skip the retrieval_quality stage")
    run.add_argument("--k", type=int, default=5, help="agent page size (production memory_search default: 5)")
    sp = sub.add_parser("split-plan", help="dry-run split manifest for a note too large for the agent pack")
    sp.add_argument("--note-id", required=True)
    sp.add_argument("--vault-root", default=str(REPO_ROOT))
    ap = sub.add_parser("apply-split", help="OWNER: create, attest and promote the children of a PLANNED split")
    ap.add_argument("--manifest", required=True)
    ap.add_argument("--vault-root", default=str(REPO_ROOT))
    ap.add_argument("--execute", action="store_true",
                    help="really apply (needs an interactive terminal and the parent id typed back)")
    cmp = sub.add_parser("compare", help="paired comparison of two job records")
    cmp.add_argument("baseline")
    cmp.add_argument("candidate")
    args = parser.parse_args(argv)

    if args.command == "split-plan":
        from interfaces import vault_runtime
        vault_runtime.configure_runtime_dirs()
        from memory_controller.controller import MemoryController
        from memory_controller.storage.file_engine import FileStorageEngine

        from .split_plan import plan_split

        vault = Path(args.vault_root).resolve()
        plan = plan_split(MemoryController(storage=FileStorageEngine(str(vault))), args.note_id)
        out_dir = vault / "08_OBSERVABILITY" / "active_knowledge_engine" / "split_plans"
        out_dir.mkdir(parents=True, exist_ok=True)
        safe_name = "".join(ch if ch.isalnum() or ch in "-_." else "_" for ch in args.note_id)
        out = out_dir / f"{safe_name}.json"
        out.write_text(json.dumps(plan, indent=2, sort_keys=True, ensure_ascii=False) + chr(10),
                       encoding="utf-8", newline=chr(10))
        print(f"plan={out}")
        print(f"status={plan['status']} children={len(plan['children'])} parent_chars={plan['parent_chars']}")
        if plan["status"] == "UNSPLITTABLE":
            print(f"blocking_line={plan['blocking_line']}")
        return 0 if plan["status"] == "PLANNED" else 1

    if args.command == "apply-split":
        from interfaces import vault_runtime
        vault_runtime.configure_runtime_dirs()
        from memory_controller.authorizer import Principal
        from memory_controller.controller import MemoryController
        from memory_controller.storage.file_engine import FileStorageEngine

        from .split_apply import ManifestError, _check_manifest, apply_split

        manifest = json.loads(Path(args.manifest).read_text(encoding="utf-8"))
        controller = MemoryController(storage=FileStorageEngine(str(Path(args.vault_root).resolve())))
        try:
            _parent, _body, children = _check_manifest(controller, manifest)
        except ManifestError as exc:
            print(f"refused: {exc}", file=sys.stderr)
            return 2
        if not args.execute:
            print(f"DRY RUN: {len(children)} children of {manifest['parent_id']} would be created RAW, "
                  "attested with their manifest hash, reviewed, promoted and read back. Nothing written.")
            for ch in children:
                print(f"  {ch['index']:03d} {ch['child_id_proposed']} lines {ch['line_start']}-{ch['line_end']} "
                      f"sha256 {ch['content_sha256'][:12]}")
            return 0
        # both ends must be a terminal: on Windows the NUL device reports isatty() on stdin
        if not (sys.stdin.isatty() and sys.stdout.isatty()):
            print("refused: --execute is an owner action and needs an interactive terminal", file=sys.stderr)
            return 2

        def confirm(parent_id: str, count: int) -> bool:
            try:
                typed = input(f"Owner: type the parent id to create and attest {count} children of it: ").strip()
            except EOFError:
                return False
            return typed == parent_id

        result = apply_split(controller, manifest, principal=Principal.HUMAN, confirm=confirm)
        print(json.dumps(result, indent=2, ensure_ascii=False))
        return {"APPLIED": 0, "FAILED": 1}.get(result["status"], 2)

    if args.command == "compare":
        from .compare import compare_records

        result = compare_records(*(json.loads(open(p, encoding="utf-8").read()) for p in (args.baseline, args.candidate)))
        print(json.dumps(result, indent=2, sort_keys=True))
        return {"NOT_COMPARABLE": 2, "REGRESSED": 4}.get(result["verdict"], 0)

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
    benchmark = None
    if not args.no_benchmark:
        default = vault / "07_EVALUATION" / "heldout_retrieval_benchmark_v2" / "heldout.json"
        benchmark = Path(args.benchmark) if args.benchmark else (default if default.is_file() else None)
    if benchmark is not None:
        # search() signs pagination tokens: same secret source as the MCP server and recall_cli
        try:
            vault_runtime.ensure_secret_in_env()
        except (vault_runtime.VaultSecretMissing, vault_runtime.VaultSecretInvalid) as exc:
            print(f"HMAC secret unavailable for the retrieval stage: {exc}", file=sys.stderr)
            return 2
    out = Path(args.output_dir) if args.output_dir else vault / "08_OBSERVABILITY" / "active_knowledge_engine"
    controller = MemoryController(storage=FileStorageEngine(str(vault)))
    runner = AKEShadowRunner(controller, output_dir=out, vault_root=vault, dormant_days=args.dormant_days,
                             stale_review_days=args.stale_review_days, max_items=args.max_items,
                             benchmark_path=benchmark, k=args.k)
    record = runner.run(trigger=args.trigger)
    print(f"record={runner.output_dir / (record['job_id'] + '.json')}")
    print(f"status={record['status']} reused={record['reused_existing_record']} "
          f"skipped={record['storage']['skipped_count']}")
    for stage in record["stages"]:
        out = stage.get("output", {})
        extra = f" cases={out['case_count']}" if "case_count" in out else ""
        if stage["name"] == "retrieval_quality" and "summary" in out:
            s = out["summary"]
            extra = f" hits={s['hits']}/{s['n_measurable']} k={s['k']} misses={s['miss_classes']}"
        print(f"stage={stage['name']} status={stage['status']}{extra}")
    return {JobStatus.COMPLETED.value: 0, JobStatus.COMPLETED_WITH_SKIPS.value: 3}.get(record["status"], 1)


if __name__ == "__main__":
    sys.exit(main())
