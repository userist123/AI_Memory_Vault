"""Owner CLI: ``python -m ake_owner_tools apply-split --manifest PLAN.json [--execute]``.

The OWNER step for a PLANNED split manifest from ``python -m active_knowledge_engine split-plan``.
Without --execute it validates and lists; with --execute it needs an interactive terminal and the
parent id typed back (exit 0 = APPLIED, 1 = FAILED, 2 = refused).

It is a separate package on purpose: the Active Knowledge Engine stays provably write-free
(AST guard in 20_TESTS/test_ake_shadow_runner.py) and never imports this one.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(prog="ake_owner_tools")
    sub = parser.add_subparsers(dest="command", required=True)
    ap = sub.add_parser("apply-split", help="OWNER: create, attest and promote the children of a PLANNED split")
    ap.add_argument("--manifest", required=True)
    ap.add_argument("--vault-root", default=str(REPO_ROOT))
    ap.add_argument("--execute", action="store_true",
                    help="really apply (needs an interactive terminal and the parent id typed back)")
    args = parser.parse_args(argv)

    if args.command == "apply-split":
        from interfaces import vault_runtime
        vault_runtime.configure_runtime_dirs()
        from memory_controller.authorizer import Principal
        from memory_controller.controller import MemoryController
        from memory_controller.storage.file_engine import FileStorageEngine

        from .split_apply import ManifestError, apply_split, preflight

        manifest = json.loads(Path(args.manifest).read_text(encoding="utf-8"))
        controller = MemoryController(storage=FileStorageEngine(str(Path(args.vault_root).resolve())))
        try:
            parent, _body, children = preflight(controller, manifest)
        except ManifestError as exc:
            print(f"refused: {exc}", file=sys.stderr)
            return 2
        if not args.execute:
            print(f"DRY RUN: {len(children)} children of {manifest['parent_id']} would be created RAW, "
                  "attested with their manifest hash, reviewed, promoted and read back. Nothing written.")
            print(f"Parent: {parent.get('lifecycle')}/{parent.get('verification')} type={parent.get('type')}. "
                  "You would attest that each child is an EXACT slice of this verified note.")
            for ch in children:
                print(f"  {ch['index']:03d} {ch['child_id_proposed']} lines {ch['line_start']}-{ch['line_end']} "
                      f"sha256 {ch['content_sha256'][:12]}")
            return 0
        # both ends must be a terminal: on Windows the NUL device reports isatty() on stdin
        # A guard against piped or scripted runs, not proof that the owner is present: any code
        # that imports split_apply can pass Principal.HUMAN (the runtime authority layer is not wired).
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
    return 2


if __name__ == "__main__":
    sys.exit(main())
