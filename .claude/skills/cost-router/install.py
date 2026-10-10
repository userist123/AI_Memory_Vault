#!/usr/bin/env python3
"""Install the cost-router skill once, for every project on this machine.

    python3 .claude/skills/cost-router/install.py            # user scope: ~/.claude
    python3 .claude/skills/cost-router/install.py --no-hook  # skill + agents only
    python3 .claude/skills/cost-router/install.py --uninstall
    python3 .claude/skills/cost-router/install.py --home <dir>    # another HOME (tests)
    python3 .claude/skills/cost-router/install.py --session-start # what the SessionStart hook runs:
                                                                  # skill + agents only, and nothing at
                                                                  # all after an --uninstall (marker file)

What it does (idempotent, prints every path it touched):
  1. copies this skill directory to ~/.claude/skills/cost-router/ and the canonical router
     module + policy into its lib/ so it works outside this repository;
  2. copies the three subagents (Explore, vault-worker, vault-reviewer) to ~/.claude/agents/
     unless a file of that name already exists (use --force to overwrite);
  3. merges one UserPromptSubmit hook into ~/.claude/settings.json (kept out with --no-hook).
Nothing is executed from the network; only files under ~/.claude are written.
"""
from __future__ import annotations

import argparse
import json
import shutil
import sys
from pathlib import Path

SKILL_SRC = Path(__file__).resolve().parent
REPO = SKILL_SRC.parents[2]
ROUTER_SRC = REPO / "03_IMPLEMENTATION" / "packages" / "routing" / "claude_model_router.py"
POLICY_SRC = REPO / "04_CONFIG" / "claude_model_routing.json"
AGENTS_SRC = REPO / ".claude" / "agents"
HOOK_MARK = "cost-router/hook_prompt_route.py"
DISABLED_MARKER = "cost-router.disabled"  # written by --uninstall under ~/.claude; honoured by --session-start


def hook_entry(skill_dir: Path) -> dict:
    cmd = (f'for py in python3 python; do if "$py" -c "import sys" >/dev/null 2>&1; then '
           f'"$py" "{skill_dir / "hook_prompt_route.py"}"; exit 0; fi; done; exit 0')
    return {"hooks": [{"type": "command", "command": cmd, "timeout": 10}]}


def load_settings(settings_path: Path) -> dict:
    """Parse the user settings or raise ValueError with a message that names the file."""
    if not settings_path.exists():
        return {}
    try:
        data = json.loads(settings_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise ValueError(f"{settings_path} is not strict JSON ({exc.msg} at line {exc.lineno}); "
                         "remove comments/trailing commas or run with --no-hook") from exc
    if not isinstance(data, dict):
        raise ValueError(f"{settings_path} must contain a JSON object")
    return data


def merge_hook(settings_path: Path, skill_dir: Path, remove: bool = False) -> str:
    settings = load_settings(settings_path)
    hooks = settings.setdefault("hooks", {})
    entries = [e for e in hooks.get("UserPromptSubmit", []) if HOOK_MARK not in json.dumps(e)]
    if not remove:
        entries.append(hook_entry(skill_dir))
    if entries:
        hooks["UserPromptSubmit"] = entries
    else:
        hooks.pop("UserPromptSubmit", None)
    if not hooks:
        settings.pop("hooks", None)
    settings_path.parent.mkdir(parents=True, exist_ok=True)
    settings_path.write_text(json.dumps(settings, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return str(settings_path)


def install(home: Path, with_hook: bool, force: bool, session_start: bool = False) -> list[str]:
    touched: list[str] = []
    marker = home / ".claude" / DISABLED_MARKER
    if session_start and marker.exists():
        return [f"skipped: {marker} exists (cost-router was uninstalled on this machine; run install.py to re-enable)"]
    if with_hook:
        load_settings(home / ".claude" / "settings.json")  # fail before anything is copied
    skill_dir = home / ".claude" / "skills" / "cost-router"
    if skill_dir.exists():
        shutil.rmtree(skill_dir)
    shutil.copytree(SKILL_SRC, skill_dir, ignore=shutil.ignore_patterns("__pycache__"))
    (skill_dir / "lib").mkdir(exist_ok=True)
    shutil.copy2(ROUTER_SRC, skill_dir / "lib" / "claude_model_router.py")
    shutil.copy2(POLICY_SRC, skill_dir / "policy.json")
    touched.append(str(skill_dir))
    agents_dir = home / ".claude" / "agents"
    agents_dir.mkdir(parents=True, exist_ok=True)
    for src in sorted(AGENTS_SRC.glob("*.md")):
        dst = agents_dir / src.name
        if dst.exists() and not force:
            touched.append(f"{dst} (kept existing; --force to overwrite)")
            continue
        shutil.copy2(src, dst)
        touched.append(str(dst))
    if with_hook:
        touched.append(merge_hook(home / ".claude" / "settings.json", skill_dir) + " (UserPromptSubmit hook)")
    if not session_start and marker.exists():
        marker.unlink()
        touched.append(f"removed {marker} (re-enabled)")
    return touched


def uninstall(home: Path) -> list[str]:
    touched: list[str] = []
    skill_dir = home / ".claude" / "skills" / "cost-router"
    if skill_dir.exists():
        shutil.rmtree(skill_dir)
        touched.append(f"removed {skill_dir}")
    for name in ("Explore.md", "vault-worker.md", "vault-reviewer.md"):
        p = home / ".claude" / "agents" / name
        if p.exists() and p.read_text(encoding="utf-8") == (AGENTS_SRC / name).read_text(encoding="utf-8"):
            p.unlink()
            touched.append(f"removed {p}")
    settings = home / ".claude" / "settings.json"
    if settings.exists():
        touched.append(merge_hook(settings, skill_dir, remove=True) + " (hook removed)")
    marker = home / ".claude" / DISABLED_MARKER
    marker.parent.mkdir(parents=True, exist_ok=True)
    marker.write_text("cost-router uninstalled; the SessionStart hook will not reinstall it. Delete this file or run install.py to re-enable.\n", encoding="utf-8")
    touched.append(f"wrote {marker} (SessionStart will not reinstall)")
    return touched


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--home", type=Path, default=Path.home())
    ap.add_argument("--no-hook", action="store_true")
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--uninstall", action="store_true")
    ap.add_argument("--session-start", action="store_true", help="quiet mode for the SessionStart hook: implies --no-hook, honours the disabled marker")
    a = ap.parse_args(argv)
    if a.session_start:
        a.no_hook = True
    for p in (ROUTER_SRC, POLICY_SRC, AGENTS_SRC):
        if not p.exists():
            print(f"missing source: {p}", file=sys.stderr)
            return 1
    try:
        touched = uninstall(a.home) if a.uninstall else install(a.home, not a.no_hook, a.force, a.session_start)
    except ValueError as exc:
        print(f"not installed: {exc}", file=sys.stderr)
        return 1
    print("\n".join(touched) or "nothing to do")
    if not a.uninstall:
        print("\nDone. New sessions in every project load the skill; type /cost-router to invoke it by hand."
              + ("" if a.no_hook else " Each non-trivial prompt now gets a one-line route hint."))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
