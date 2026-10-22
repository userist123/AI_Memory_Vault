#!/usr/bin/env python3
"""Install the cost-router skill once, for every project on this machine.

    python3 .claude/skills/cost-router/install.py            # user scope: ~/.claude
    python3 .claude/skills/cost-router/install.py --no-hook  # skill + agents only
    python3 .claude/skills/cost-router/install.py --no-context # no global CLAUDE.md rules, no deny rules
    python3 .claude/skills/cost-router/install.py --uninstall
    python3 .claude/skills/cost-router/install.py --verify [--json]  # read-only: installed == source? exit 1 if not
    python3 .claude/skills/cost-router/install.py --home <dir>    # another HOME (tests)
    python3 .claude/skills/cost-router/install.py --session-start # what the SessionStart hook runs:
                                                                  # skill + agents only, and nothing at
                                                                  # all after an --uninstall (marker file)
    --session-start and --self-update do nothing unless the owner opted in (default OFF since
    2026-10-22): COST_ROUTER_SELF_INSTALL=1, or create ~/.claude/cost-router.auto-refresh.

What it does (idempotent, prints every path it touched):
  1. copies this skill directory to ~/.claude/skills/cost-router/ and the canonical router
     module + policy into its lib/ so it works outside this repository;
  2. copies the three subagents (Explore, vault-worker, vault-reviewer) to ~/.claude/agents/; a file
     of that name is replaced only when it is still the copy an earlier install wrote (owner edits are
     kept; --force overwrites them too);
  3. merges six hooks into ~/.claude/settings.json (kept out with --no-hook): UserPromptSubmit (one
     route line per non-trivial prompt; stores the route per session), PreToolUse on Agent|Task (sets each
     subagent's model from the route of its brief, whatever the subagent type), PreToolUse on
     Edit|Write|NotebookEdit|Bash|Read|Grep|Glob (hook_enforce_route.py: makes the route binding),
     PreToolUse on SendMessage (hook_resume_guard.py: a subagent is resumed at most once),
     SessionStart repo map (a ~1k-token map of the
     current git repository, lib/repo_map.py, at startup, /clear and compaction) and SessionStart
     self-update (hook_self_update.py: at most hourly, fetches the latest skill from the canonical
     repository and re-runs this installer, so one install stays current);
  4. context economy for every project (kept out with --no-context): a marked block of reading
     rules in ~/.claude/CLAUDE.md (the rest of that file is never touched) and Read deny rules for
     tool caches (__pycache__, .pytest_cache, .mypy_cache, .ruff_cache, .git/objects) in
     ~/.claude/settings.json permissions.deny (not with --no-hook, which never touches settings.json).
     --uninstall removes exactly the block and these rules.
The installer itself runs nothing from the network and writes only under ~/.claude. The self-update
hook it registers runs install.py from the canonical repository's `main` at most hourly ONLY after the
owner opted in (COST_ROUTER_SELF_INSTALL=1 or ~/.claude/cost-router.auto-refresh; default OFF since
2026-10-22; `main` is unpinned and unsigned: HIGH-2 open); --no-hook installs without it. Every install holds
~/.claude/cost-router.lock and swaps the skill directory in with a rename, so concurrent sessions or
a killed run never leave a half-deleted skill behind.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import time
from contextlib import contextmanager
from pathlib import Path

SKILL_SRC = Path(__file__).resolve().parent
REPO = SKILL_SRC.parents[2]
ROUTER_SRC = REPO / "03_IMPLEMENTATION" / "packages" / "routing" / "claude_model_router.py"
POLICY_SRC = REPO / "04_CONFIG" / "claude_model_routing.json"
AGENTS_SRC = REPO / ".claude" / "agents"
HOOK_MARK = "cost-router/hook_"  # every hook this installer owns
HOOK_SPECS = (  # (event, script, matcher)
    ("UserPromptSubmit", "hook_prompt_route.py", None),
    ("PreToolUse", "hook_agent_model.py", "Agent|Task"),
    ("PreToolUse", "hook_enforce_route.py", "Edit|Write|NotebookEdit|Bash|Read|Grep|Glob"),
    ("PreToolUse", "hook_resume_guard.py", "SendMessage"),
    ("PostToolUse", "hook_agent_usage.py", "Agent|Task"),
    ("SessionStart", "hook_repo_map.py", "startup|clear|compact"),
    ("SessionStart", "hook_self_update.py", "startup|resume"),
)
HOOK_TIMEOUT = {"hook_self_update.py": 60}  # seconds; a fetch is about two, the rest is margin
AGENT_MANIFEST = "agents.sha256.json"  # under the installed skill: hashes of the agent files it wrote
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
- Write to the owner in the language set by `language` in ~/.claude/settings.json (Romanian for
  this owner): replies, short progress notes, commit messages, PR titles, descriptions and
  comments. File contents and internal text stay in English: code, skills, prompts, .md files,
  subagent briefs and self check-ins.
## Agents and review rounds (every project; cost-router skill §2A, measured 2026-10-14..16)
- Resume a subagent at most once (SendMessage). Any further round is a NEW agent seeded with a
  checkpoint of at most 40 lines: task, branch/PR, head SHA, findings to fix, files, test commands.
- Security-sensitive work: write the threat model / probe list into the worker brief before
  implementation and give the reviewer the same list; crypto/PDF/signature briefs state the trust
  model and the whitelist design. Ask the owner the open product questions before dispatch.
- At most 2 review rounds; round 2 reviews only the delta and runs only the affected test
  projects; then decide in the main session (fix, or an accepted residual recorded with the owner).
  The full suite runs once before merge.
- One implementation agent at a time. Agents push and stop; they never poll CI. The main session
  checks CI once, then dispatches the review. Before parallel branches, reserve ids and file
  ownership in the coordination checkpoint.
- Implementers of risky code run on Sonnet because an Opus reviewer pass is mandatory; design,
  root cause, verification, critical risk and live operations (production, deploy, delete,
  rotate) stay on Opus.
{RULES_END}
"""
DISABLED_MARKER = "cost-router.disabled"  # written by --uninstall under ~/.claude; honoured by --session-start
# Owner decision 2026-10-22: hook-driven refresh (--session-start, --self-update) is OFF by default. It runs only
# after an explicit opt-in: COST_ROUTER_SELF_INSTALL=1, or this marker under ~/.claude (removed by --uninstall).
# The opt-outs (COST_ROUTER_NO_SELF_INSTALL=1, COST_ROUTER_OFF, the disabled marker) keep priority.
OPT_IN_MARKER = "cost-router.auto-refresh"


def auto_refresh_opted_in(home: Path) -> bool:
    return (os.environ.get("COST_ROUTER_SELF_INSTALL", "").strip() == "1"
            or (home / ".claude" / OPT_IN_MARKER).exists())


def hook_entry(skill_dir: Path, script: str = "hook_prompt_route.py", matcher=None) -> dict:
    target = (skill_dir / script).as_posix()
    cmd = (f'for py in python3 python; do if "$py" -c "import sys" >/dev/null 2>&1; then '
           f'"$py" "{target}"; exit 0; fi; done; exit 0')
    entry = {"hooks": [{"type": "command", "command": cmd, "timeout": HOOK_TIMEOUT.get(script, 10)}]}
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
    for event in dict.fromkeys(spec[0] for spec in HOOK_SPECS):  # an event may carry several of our hooks
        entries = [e for e in hooks.get(event, []) if not _owned(e)]
        if not remove:
            entries += [hook_entry(skill_dir, script, matcher) for ev, script, matcher in HOOK_SPECS if ev == event]
        if entries:
            hooks[event] = entries
        else:
            hooks.pop(event, None)
    if not hooks:
        settings.pop("hooks", None)
    settings_path.parent.mkdir(parents=True, exist_ok=True)
    settings_path.write_text(json.dumps(settings, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return str(settings_path)


def _sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _written(skill_dir: Path) -> dict:
    """Hashes of the agent files the previous install wrote (empty when unknown)."""
    try:
        data = json.loads((skill_dir / AGENT_MANIFEST).read_text(encoding="utf-8"))
        return data if isinstance(data, dict) else {}
    except (OSError, ValueError):
        return {}


class LockBusy(Exception):
    pass


LOCK_STALE_SECONDS = 120  # a lock older than this belongs to a run that was killed


@contextmanager
def install_lock(home: Path, wait: float):
    """Hold ~/.claude/cost-router.lock; raise LockBusy if another live run keeps it past `wait`."""
    path = home / ".claude" / "cost-router.lock"
    path.parent.mkdir(parents=True, exist_ok=True)
    deadline = time.time() + wait
    while True:
        try:
            fd = os.open(path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            os.write(fd, f"{os.getpid()}\n".encode())
            os.close(fd)
            break
        except FileExistsError:
            try:
                if time.time() - path.stat().st_mtime > LOCK_STALE_SECONDS:
                    path.unlink()
                    continue
            except OSError:
                continue
            if time.time() >= deadline:
                raise LockBusy(f"{path} is held by another install")
            time.sleep(0.2)
    try:
        yield
    finally:
        try:
            path.unlink()
        except OSError:
            pass


def _swap_in(staging: Path, skill_dir: Path) -> None:
    """Replace skill_dir with staging by renames; on failure the previous copy stays in place."""
    old = staging.with_name(f"cost-router.old-{os.getpid()}")
    skill_dir.parent.mkdir(parents=True, exist_ok=True)
    if skill_dir.exists():
        os.replace(skill_dir, old)
    try:
        os.replace(staging, skill_dir)
    except OSError:
        if old.exists():
            os.replace(old, skill_dir)
        raise
    shutil.rmtree(old, ignore_errors=True)


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


def _context_installed(home: Path) -> bool:
    """Whether an earlier install added the context-economy rules (so a self-update keeps them)."""
    claude_md = home / ".claude" / "CLAUDE.md"
    try:
        has_block = RULES_START in claude_md.read_text(encoding="utf-8")
    except OSError:
        has_block = False
    return has_block or (home / ".claude" / DENY_ADDED).exists()


_IGNORE = ("__pycache__", "chat")
_GENERATED = {"VERSION", AGENT_MANIFEST}  # written by install(), not part of the source manifest


def _file_sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def manifest(root: Path, extra: dict[str, Path] | None = None) -> dict[str, str]:
    """relative path -> sha256 of the files install() copies from `root` (same ignore rules), plus `extra`
    (relative path -> source file). VERSION and the agent manifest are generated per install and excluded."""
    out: dict[str, str] = {}
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in _IGNORE]
        for name in filenames:
            path = Path(dirpath) / name
            rel = path.relative_to(root).as_posix()
            if rel not in _GENERATED:
                out[rel] = _file_sha(path)
    for rel, src in (extra or {}).items():
        out[rel] = _file_sha(src)
    return dict(sorted(out.items()))


def _added_files() -> dict[str, Path]:
    return {"lib/claude_model_router.py": ROUTER_SRC, "policy.json": POLICY_SRC}


def source_manifest() -> dict[str, str]:
    return manifest(SKILL_SRC, _added_files())


def manifest_hash(m: dict[str, str]) -> str:
    return hashlib.sha256(json.dumps(dict(sorted(m.items())), sort_keys=True).encode("utf-8")).hexdigest()


def _repo_head() -> str:
    try:
        git = dict(cwd=REPO, capture_output=True, text=True, timeout=10)
        head = subprocess.run(["git", "rev-parse", "HEAD"], **git)
        if head.returncode != 0 or not head.stdout.strip():
            return "unknown"
        dirty = subprocess.run(["git", "status", "--porcelain", "--", ".claude/skills/cost-router",
                                "03_IMPLEMENTATION/packages/routing/claude_model_router.py",
                                "04_CONFIG/claude_model_routing.json"], **git)
        return head.stdout.strip() + ("+dirty" if dirty.stdout.strip() else "")
    except Exception:
        return "unknown"


_VERSION_REF = re.compile(r"^([0-9a-f]{7,40}|unknown|[A-Za-z0-9._/-]+)(\+dirty)?$")


def verify(home: Path) -> dict:
    """Read-only: compare what install() would copy with the installed skill directory."""
    skill_dir = home / ".claude" / "skills" / "cost-router"
    want = source_manifest()
    have = manifest(skill_dir) if skill_dir.is_dir() else {}
    vfile = skill_dir / "VERSION"
    version = vfile.read_text(encoding="utf-8").splitlines() if vfile.is_file() else []
    missing = sorted(set(want) - set(have))
    extra = sorted(set(have) - set(want))
    differing = sorted(k for k in set(want) & set(have) if want[k] != have[k])
    version_problem = ""
    if version:
        if not _VERSION_REF.match(version[0].strip()):
            version_problem = f"line 1 is not a git ref or 'unknown': {version[0][:40]!r}"
        elif len(version) < 2 or version[1].strip() != manifest_hash(have):
            version_problem = "line 2 does not match the manifest hash of the installed tree"
    return {"ok": bool(version) and not (missing or extra or differing or version_problem),
            "installed_dir": str(skill_dir),
            "missing": missing, "extra": extra, "differing": differing,
            "installed_version": version, "source_version": [_repo_head(), manifest_hash(want)],
            "version_missing": not version, "version_problem": version_problem}


def install(home: Path, with_hook: bool, force: bool, session_start: bool = False,
            with_context: bool = True, source_ref: str | None = None, self_update: bool = False) -> list[str]:
    touched: list[str] = []
    marker = home / ".claude" / DISABLED_MARKER
    if (session_start or self_update) and marker.exists():
        return [f"skipped: {marker} exists (cost-router was uninstalled on this machine; run install.py to re-enable)"]
    if with_hook:
        load_settings(home / ".claude" / "settings.json")  # fail before anything is copied
    skill_dir = home / ".claude" / "skills" / "cost-router"
    written = _written(skill_dir)
    staging = home / ".claude" / f"cost-router.staging-{os.getpid()}"  # same filesystem, outside skills/
    for leftover in (*(home / ".claude").glob("cost-router.staging-*"), *(home / ".claude").glob("cost-router.old-*")):
        shutil.rmtree(leftover, ignore_errors=True)  # from a killed run; safe, we hold the lock
    shutil.copytree(SKILL_SRC, staging, ignore=shutil.ignore_patterns(*_IGNORE))  # chat/ is the claude.ai upload
    (staging / "lib").mkdir(exist_ok=True)
    shutil.copy2(ROUTER_SRC, staging / "lib" / "claude_model_router.py")
    shutil.copy2(POLICY_SRC, staging / "policy.json")
    (staging / "VERSION").write_text(
        f"{source_ref or _repo_head()}\n{manifest_hash(source_manifest())}\n", encoding="utf-8")
    agents_dir = home / ".claude" / "agents"
    agents_dir.mkdir(parents=True, exist_ok=True)
    agent_hashes = {}
    for src in sorted(AGENTS_SRC.glob("*.md")):
        dst = agents_dir / src.name
        if dst.exists() and not force:
            current = _sha(dst)
            if current == _sha(src):
                agent_hashes[src.name] = current
                touched.append(f"{dst} (already current)")
                continue
            if written.get(src.name) != current:
                touched.append(f"{dst} (kept existing: edited here; --force to overwrite)")
                continue
        shutil.copy2(src, dst)
        agent_hashes[src.name] = _sha(dst)
        touched.append(str(dst))
    (staging / AGENT_MANIFEST).write_text(json.dumps(agent_hashes, indent=2) + "\n", encoding="utf-8")
    try:
        _swap_in(staging, skill_dir)
    finally:
        shutil.rmtree(staging, ignore_errors=True)
    touched.insert(0, str(skill_dir) + (f" (source {source_ref})" if source_ref else ""))
    if with_hook:
        touched.append(merge_hook(home / ".claude" / "settings.json", skill_dir)
                       + " (UserPromptSubmit + PreToolUse/Agent + SessionStart repo-map and self-update hooks)")
    if with_context:
        if with_hook:  # --no-hook keeps settings.json untouched altogether
            touched.append(merge_deny(home / ".claude" / "settings.json") + f" ({len(DENY_RULES)} Read deny rules)")
        touched.append(merge_rules(home / ".claude" / "CLAUDE.md") + " (context-economy rules block)")
    if not (session_start or self_update) and marker.exists():
        marker.unlink()
        touched.append(f"removed {marker} (re-enabled)")
    return touched


def uninstall(home: Path) -> list[str]:
    touched: list[str] = []
    skill_dir = home / ".claude" / "skills" / "cost-router"
    written = _written(skill_dir)
    if skill_dir.exists():
        shutil.rmtree(skill_dir)
        touched.append(f"removed {skill_dir}")
    for name in ("Explore.md", "vault-worker.md", "vault-reviewer.md"):
        p = home / ".claude" / "agents" / name
        if p.exists() and _sha(p) in {_sha(AGENTS_SRC / name), written.get(name)}:
            p.unlink()
            touched.append(f"removed {p}")
    settings = home / ".claude" / "settings.json"
    if settings.exists():
        touched.append(merge_hook(settings, skill_dir, remove=True) + " (hook removed)")
        touched.append(merge_deny(settings, remove=True) + " (deny rules removed)")
    if (home / ".claude" / "CLAUDE.md").exists():
        touched.append(merge_rules(home / ".claude" / "CLAUDE.md", remove=True) + " (rules block removed)")
    opt_in = home / ".claude" / OPT_IN_MARKER
    if opt_in.exists():
        opt_in.unlink()
        touched.append(f"removed {opt_in} (auto-refresh opt-in)")
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
    ap.add_argument("--self-update", action="store_true",
                    help="what the self-update hook runs: hooks included, skipped after an --uninstall, never re-enables")
    ap.add_argument("--source-ref", help="commit the files came from (written to VERSION; set by the self-update hook)")
    ap.add_argument("--session-start", action="store_true", help="quiet mode for the SessionStart hook: implies --no-hook, honours the disabled marker")
    ap.add_argument("--verify", action="store_true", help="read-only: compare the installed skill with this source; exit 1 on any difference")
    ap.add_argument("--json", action="store_true", help="with --verify: print a JSON object")
    a = ap.parse_args(argv)
    if (a.session_start or a.self_update) and (os.environ.get("COST_ROUTER_NO_SELF_INSTALL", "").strip() == "1"
                            or os.environ.get("COST_ROUTER_OFF", "").strip().lower() in ("1", "true", "yes", "on")):
        return 0  # isolation / kill switch (COST_ROUTER_OFF: every hook is inert) (benchmark children): no output, no writes, never touches --home
    if (a.session_start or a.self_update) and not auto_refresh_opted_in(a.home):
        return 0  # default OFF (owner, 2026-10-22): a session start never rewrites ~/.claude without an opt-in
    if a.session_start:
        a.no_hook = True
        a.no_context = True
    for p in (ROUTER_SRC, POLICY_SRC, AGENTS_SRC):
        if not p.exists():
            print(f"missing source: {p}", file=sys.stderr)
            return 1
    if a.verify:
        r = verify(a.home)
        if a.json:
            print(json.dumps(r, indent=2))
        else:
            for key in ("missing", "extra", "differing"):
                for rel in r[key]:
                    print(f"{key}: {rel}")
            print("installed VERSION: " + (" / ".join(r["installed_version"]) or "MISSING"))
            if r["version_problem"]:
                print("VERSION problem: " + r["version_problem"])
            print("source VERSION:    " + " / ".join(r["source_version"]))
            print("OK: installed skill matches this source" if r["ok"] else "DIFFERENT: run install.py to refresh")
        return 0 if r["ok"] else 1
    quiet = a.session_start or a.self_update  # hook-driven: never wait on another run, skip instead
    try:
        with install_lock(a.home, wait=0 if quiet else 30):
            if a.self_update:  # keep whatever the owner chose at install time
                a.no_context = not _context_installed(a.home)
            touched = uninstall(a.home) if a.uninstall else install(
                a.home, not a.no_hook, a.force, a.session_start, not a.no_context, a.source_ref, a.self_update)
    except LockBusy as exc:
        print(f"skipped: {exc}" if quiet else f"not installed: {exc}", file=sys.stdout if quiet else sys.stderr)
        return 0 if quiet else 1
    except ValueError as exc:
        print(f"not installed: {exc}", file=sys.stderr)
        return 1
    print("\n".join(touched) or "nothing to do")
    if not a.uninstall:
        print("\nDone. New sessions in every project load the skill; type /cost-router to invoke it by hand."
              + ("" if a.no_hook else " Each non-trivial prompt now gets a one-line route hint, each session"
                 " starts with a map of its repository. Automatic refresh (at most hourly) is OFF until you opt in:"
                 " create ~/.claude/cost-router.auto-refresh or set COST_ROUTER_SELF_INSTALL=1."))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
