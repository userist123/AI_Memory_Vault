"""Command-line adapter over vault_access.core.VaultAccess (same envelope as the MCP tools).

    python -m cognitive_core.vault_cli domains
    python -m cognitive_core.vault_cli ls governance
    python -m cognitive_core.vault_cli resolve "VAULT_STATE"
    python -m cognitive_core.vault_cli read vault://governance/vault_state --section 3-component-reality-measured
    python -m cognitive_core.vault_cli search "bugetul grafului"
    python -m cognitive_core.vault_cli meta vault://procedures/git_backup_restore_rollback
    python -m cognitive_core.vault_cli check --citations citations.json
    python -m cognitive_core.vault_cli check-config

`--principal` defaults to `cloud_cli.unknown` (what an agent shelling out gets); the owner at the
keyboard passes `--principal owner`. Output is JSON on stdout; the exit code is 0 OK,
2 NOT_FOUND/AMBIGUOUS, 3 DENIED/OUT_OF_ROOT, 1 anything else.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import List, Optional

_PACKAGES = Path(__file__).resolve().parents[1]
if str(_PACKAGES) not in sys.path:
    sys.path.insert(0, str(_PACKAGES))

from vault_access.errors import ErrorCode  # noqa: E402

DEFAULT_PRINCIPAL = "cloud_cli.unknown"


def _search_backend(query: str, limit: int):
    from interfaces import memory_access, vault_runtime
    from interfaces.recall_cli import get_memory_controller
    vault_runtime.ensure_secret_in_env()
    vault_runtime.configure_runtime_dirs()
    return memory_access.search(get_memory_controller(), query, limit)


def _note_eligibility(note_id: str):
    try:
        from interfaces import memory_access, vault_runtime
        from interfaces.recall_cli import get_memory_controller
        vault_runtime.ensure_secret_in_env()
        vault_runtime.configure_runtime_dirs()
        controller = get_memory_controller()
    except Exception:  # noqa: BLE001 - no secret / no controller: policy gates still apply
        return None
    return memory_access._readable(controller, note_id) is not None


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="python -m cognitive_core.vault_cli")
    p.add_argument("--principal", default=DEFAULT_PRINCIPAL)
    sub = p.add_subparsers(dest="cmd", required=True)
    sub.add_parser("domains")
    ls = sub.add_parser("ls")
    ls.add_argument("domain")
    ls.add_argument("--cursor", type=int, default=0)
    r = sub.add_parser("resolve")
    r.add_argument("query")
    r.add_argument("--domain")
    rd = sub.add_parser("read")
    rd.add_argument("uri")
    rd.add_argument("--section")
    rd.add_argument("--line-start", type=int)
    rd.add_argument("--line-end", type=int)
    rd.add_argument("--text", action="store_true", help="print only the verbatim text and its citation")
    s = sub.add_parser("search")
    s.add_argument("query")
    s.add_argument("--domain")
    s.add_argument("--limit", type=int, default=5)
    m = sub.add_parser("meta")
    m.add_argument("uri")
    c = sub.add_parser("check")
    c.add_argument("--citations", required=True, help="JSON file: [{uri, quote, line_start?, line_end?}]")
    sub.add_parser("check-config")
    return p


def main(argv: Optional[List[str]] = None) -> int:
    args = build_parser().parse_args(argv)
    if args.cmd == "check-config":
        sys.path.insert(0, str(_PACKAGES.parents[1] / "30_SCRIPTS" / "routing"))
        from build_route_manifest import main as check_main  # type: ignore
        return check_main(["--check"])
    from vault_access.core import VaultAccess
    from vault_access.errors import VaultAccessError
    interface = "cli"
    if args.principal == "owner":
        # Agents reach this CLI through their shell, which has no terminal: owner access needs an
        # interactive confirmation on a real TTY. It is a speed bump against an agent asserting
        # `owner`, not a security boundary against the user's own account (see access_policy.yaml).
        if not (sys.stdin.isatty() and sys.stdout.isatty()):
            print(json.dumps({"ok": False, "code": "DENIED_POLICY",
                              "error": "owner access needs an interactive terminal"}, ensure_ascii=False))
            return 3
        typed = input("Owner access to the vault (inbox, archive, RAW). Type OWNER to continue: ")
        if typed.strip() != "OWNER":
            print(json.dumps({"ok": False, "code": "DENIED_POLICY", "error": "not confirmed"}))
            return 3
        interface = "cli_owner"
    try:
        access = VaultAccess(args.principal, interface, search_backend=_search_backend,
                             note_eligibility=_note_eligibility)
    except VaultAccessError as exc:
        print(json.dumps({"ok": False, "code": exc.code.value, "error": exc.message}, ensure_ascii=False))
        return exc.code.exit_code
    if args.cmd == "domains":
        env = access.list("*")
    elif args.cmd == "ls":
        env = access.list(args.domain, cursor=args.cursor)
    elif args.cmd == "resolve":
        env = access.resolve(args.query, domain=args.domain)
    elif args.cmd == "read":
        env = access.read(args.uri, section=args.section, line_start=args.line_start, line_end=args.line_end)
        if args.text and env["ok"]:
            sys.stdout.write(env["evidence"][0]["text"] + "\n\n-- " + env["cite_as"] + "\n")
            return 0
    elif args.cmd == "search":
        env = access.search(args.query, domain=args.domain, limit=args.limit)
    elif args.cmd == "meta":
        env = access.metadata(args.uri)
    else:
        citations = json.loads(Path(args.citations).read_text(encoding="utf-8"))
        env = access.check_quotes(citations)
        if env["ok"] and not env["all_ok"]:
            print(json.dumps(env, ensure_ascii=False, indent=2))
            return 4
    print(json.dumps(env, ensure_ascii=False, indent=2))
    return ErrorCode(env["code"]).exit_code


if __name__ == "__main__":
    raise SystemExit(main())
