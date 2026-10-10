#!/usr/bin/env python3
"""Install the cost-router skill once, for every project on this machine.

    python3 .claude/skills/cost-router/install.py            # user scope: ~/.claude
    python3 .claude/skills/cost-router/install.py --no-hook  # skill + agents only
    python3 .claude/skills/cost-router/install.py --no-context # no global CLAUDE.md rules, no deny rules
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
  3. merges three hooks into ~/.claude/settings.json (kept out with --no-hook): UserPromptSubmit (one
     route line per non-trivial prompt), PreToolUse on Agent (sets each subagent's model from
     the route of its brief, whatever the subagent type) and SessionStart (a ~1k-token map of the
     current git repository, lib/repo_map.py, at startup, /clear and compaction);
  4. context economy for every project (kept out with --no-context): a marked block of reading
     rules in ~/.claude/CLAUDE.md (the rest of that file is never touched) and Read deny rules for
     tool caches (__pycache__, .pytest_cache, .mypy_cache, .ruff_cache, .git/objects) in
     ~/.claude/settings.json permissions.deny (not with --no-hook, which never touches settings.json).
     --uninstall removes exactly the block and these rules.
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
HOOK_MARK = "cost-router/hook_"  # every hook this installer owns
HOOK_SPECS = (  # (event, script, matcher)
    ("UserPromptSubmit", "hook_prompt_route.py", None),
    ("PreToolUse", "hook_agent_model.py", "Agent"),
    ("SessionStart", "hook_repo_map.py", "startup|clear|compact"),
)
DENY_RULES = (  # pure caches only: never source, never worth reading (deny beats any project allow, so
    # dependency trees such as node_modules/.venv and build output such as dist/ stay readable for debugging)
    "Read(**/__pycache__/**)", "Read(**/.pytest_cache/**)", "Read(**/.mypy_cache/**)",
    "Read(**/.ruff_cache/**)", "Read(**/.git/objects/**)",
)
DENY_ADDED = "cost-router.deny-added.json"  # the rules this installer added, so uninstall removes only those
RULES_START = "<!-- cost-router:context-rules:start -->"
RULES_END = "<!-- cost-router:context-rules:end -->"
CONTEXT_RULES = f"""{RULES_START}
## Context economy (every project; installed by cost-router, edit outside this block)
- When the session starts with a "Repo map" (cost-router hook), use it instead of exploring: open
  files by path, grep narrowly inside one directory, never read a directory marked heavy (⚠)
  wholesale, read large files by offset/limit.
- Broad inventories ("where is", "list every") go to an Explore subagent; keep only its conclusion.
- One task per session. At a milestone write a short checkpoint (task, branch, done, next, key
  files) and start the next task with /clear from that checkpoint, not from the old transcript.
- Do not re-read a file you already have unchanged in context; do not paste whole files back.
{RULES_END}
"""
DISABLED_MARKER = "cost-router.disabled"  # written by --uninstall under ~/.claude; honoured by --session-start


def hook_entry(skill_dir: Path, script: str = "hook_prompt_route.py", matcher=None) -> dict:
    cmd = (f'for py in python3 python; do if "$py" -c "import sys" >/dev/null 2>&1; then '
           f'"$py" "{skill_dir / script}"; exit 0; fi; done; exit 0')
    entry = {"hooks": [{"type": "command", "command": cmd, "timeout": 10}]}
    return {"matcher": matcher, **entry} if matcher else entry


def _owned(entry) -> bool:
    return HOOK_MARK in json.dumps(entry).replace("\\\\", "/")


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
    for event, script, matcher in HOOK_SPECS:
        entries = [e for e in hooks.get(event, []) if not _owned(e)]
        if not remove:
            entries.append(hook_entry(skill_dir, script, matcher))
        if entries:
            hooks[event] = entries
        else:
            hooks.pop(event, None)
    if not hooks:
        settings.pop("hooks", None)
    settings_path.parent.mkdir(parents=True, exist_ok=True)
    settings_path.write_text(json.dumps(settings, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return str(settings_path)


def merge_deny(settings_path: Path, remove: bool = False) -> str:
    """Add the missing DENY_RULES; on removal drop only the ones recorded as added by us."""
    settings = load_settings(settings_path)
    record = settings_path.parent / DENY_ADDED
    try:
        added = set(json.loads(record.read_text(encoding="utf-8")))
    except (OSError, ValueError, TypeError):
        added = set()
    perms = settings.setdefault("permissions", {})
    deny = list(perms.get("deny", []))
    if remove:
        deny = [r for r in deny if r not in added]
        record.unlink(missing_ok=True)
    else:
        for rule in DENY_RULES:
            if rule not in deny:
                deny.append(rule)
                added.add(rule)
        record.write_text(json.dumps(sorted(added & set(DENY_RULES)), indent=1) + "\n", encoding="utf-8")
    if deny:
        perms["deny"] = deny
    else:
        perms.pop("deny", None)
    if not perms:
        settings.pop("permissions", None)
    settings_path.parent.mkdir(parents=True, exist_ok=True)
    settings_path.write_text(json.dumps(settings, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return str(settings_path)


def merge_rules(claude_md: Path, remove: bool = False) -> str:
    """Insert, refresh or remove the marked block; every other byte of the file is kept as is
    (line endings included). A damaged block (markers missing, repeated or out of order) is left
    alone and reported rather than guessed at."""
    def read() -> str:
        with open(claude_md, encoding="utf-8", newline="") as fh:
            return fh.read()

    def write(text: str) -> None:
        claude_md.parent.mkdir(parents=True, exist_ok=True)
        with open(claude_md, "w", encoding="utf-8", newline="") as fh:
            fh.write(text)

    text = read() if claude_md.exists() else ""
    nl = "\r\n" if "\r\n" in text else "\n"
    block = CONTEXT_RULES.replace("\n", nl)
    n_start, n_end = text.count(RULES_START), text.count(RULES_END)
    if (n_start, n_end) not in ((0, 0), (1, 1)) or (n_start and text.index(RULES_START) > text.index(RULES_END)):
        return f"{claude_md} (rules block damaged, left untouched; fix the cost-router markers by hand)"
    if n_start:
        i, j = text.index(RULES_START), text.index(RULES_END) + len(RULES_END)
        if text[j:j + len(nl)] == nl:
            j += len(nl)
        if remove:
            if text[i - len(nl):i] == nl and (i == len(nl) or text[i - 2 * len(nl):i - len(nl)] == nl):
                i -= len(nl)  # the blank line we put before the block
            text = text[:i] + text[j:]
        else:
            text = text[:i] + block + text[j:]
    elif not remove:
        sep = "" if not text else (nl if text.endswith(nl) else nl + nl)
        text += sep + block
    if text:
        write(text)
    elif claude_md.exists():
        claude_md.unlink()
    return str(claude_md)


def install(home: Path, with_hook: bool, force: bool, session_start: bool = False,
            with_context: bool = True) -> list[str]:
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
        touched.append(merge_hook(home / ".claude" / "settings.json", skill_dir)
                       + " (UserPromptSubmit + PreToolUse/Agent + SessionStart repo-map hooks)")
    if with_context:
        if with_hook:  # --no-hook keeps settings.json untouched altogether
            touched.append(merge_deny(home / ".claude" / "settings.json") + f" ({len(DENY_RULES)} Read deny rules)")
        touched.append(merge_rules(home / ".claude" / "CLAUDE.md") + " (context-economy rules block)")
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
        touched.append(merge_deny(settings, remove=True) + " (deny rules removed)")
    if (home / ".claude" / "CLAUDE.md").exists():
        touched.append(merge_rules(home / ".claude" / "CLAUDE.md", remove=True) + " (rules block removed)")
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
    ap.add_argument("--no-context", action="store_true", help="skip the global CLAUDE.md rules block and Read deny rules")
    ap.add_argument("--uninstall", action="store_true")
    ap.add_argument("--session-start", action="store_true", help="quiet mode for the SessionStart hook: implies --no-hook, honours the disabled marker")
    a = ap.parse_args(argv)
    if a.session_start:
        a.no_hook = True
        a.no_context = True
    for p in (ROUTER_SRC, POLICY_SRC, AGENTS_SRC):
        if not p.exists():
            print(f"missing source: {p}", file=sys.stderr)
            return 1
    try:
        touched = uninstall(a.home) if a.uninstall else install(a.home, not a.no_hook, a.force, a.session_start, not a.no_context)
    except ValueError as exc:
        print(f"not installed: {exc}", file=sys.stderr)
        return 1
    print("\n".join(touched) or "nothing to do")
    if not a.uninstall:
        print("\nDone. New sessions in every project load the skill; type /cost-router to invoke it by hand."
              + ("" if a.no_hook else " Each non-trivial prompt now gets a one-line route hint and each session"
                 " starts with a map of its repository."))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
