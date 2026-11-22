"""Per-project Git integration (spec §11): every revision is a commit of the model + rendered site.

The workspace repo tracks `model/project.json` (the full component model and settings — the source of
truth needed to rebuild the project) and `site/` (what visitors get). Screenshots, exports, builds and
BIM sources stay out (`.gitignore`). Commits carry the revision, operation id and the real actor as author.
Git failures never break an edit: they are reported as `git: {"ok": false, ...}` and audited.
"""

from __future__ import annotations

import json
import os
import shutil
import subprocess
from pathlib import Path
from typing import Any

from .errors import NotFound, ValidationFailed

IGNORE = "artifacts/\nexports/\nbuilds/\nbim/\nimports/\n*.tmp\n"
_ENV_KEEP = ("SYSTEMROOT", "PATH", "TEMP", "TMP", "USERPROFILE", "HOMEDRIVE", "HOMEPATH", "LOCALAPPDATA", "APPDATA")


def git_exe() -> str | None:
    return shutil.which("git")


def _env() -> dict[str, str]:
    env = {k: os.environ[k] for k in _ENV_KEEP if k in os.environ}
    # isolate from the owner's global git config (hooks, signing, credential helpers, templates)
    env.update({"GIT_CONFIG_GLOBAL": os.devnull, "GIT_CONFIG_NOSYSTEM": "1", "GIT_TERMINAL_PROMPT": "0"})
    return env


def _git(repo: Path, *args: str, check: bool = True, timeout: int = 60) -> subprocess.CompletedProcess[str]:
    exe = git_exe()
    if exe is None:
        raise ValidationFailed("git is not installed")
    r = subprocess.run([exe, "-C", str(repo), "-c", "core.autocrlf=false", "-c", "core.hooksPath=NUL" if os.name == "nt"  # noqa: S603
                        else "core.hooksPath=/dev/null", *args], capture_output=True, text=True, timeout=timeout,
                       env=_env(), encoding="utf-8", errors="replace")
    if check and r.returncode != 0:
        raise ValidationFailed(f"git {args[0]} failed", stderr=r.stderr[-500:])
    return r


def ensure_repo(root: Path) -> None:
    if (root / ".git").is_dir():
        return
    _git(root, "init", "-q", "-b", "main")
    (root / ".gitignore").write_text(IGNORE, encoding="utf-8")


def commit_revision(root: Path, model: dict[str, Any], revision: int, summary: str, actor: str,
                    operation_id: str) -> dict[str, Any]:
    if git_exe() is None:
        return {"ok": False, "reason": "git not installed"}
    try:
        ensure_repo(root)
        (root / "model").mkdir(exist_ok=True)
        tmp = root / "model" / "project.json.tmp"
        tmp.write_text(json.dumps(model, indent=1, sort_keys=True, default=str), encoding="utf-8")
        os.replace(tmp, root / "model" / "project.json")
        _git(root, "add", "-A", "--", ".gitignore", "model", "site")
        who = actor.replace("<", "").replace(">", "")[:80]
        msg = f"rev {revision}: {summary[:200]}\n\nRevision: {revision}\nOperation: {operation_id}\nActor: {who}\n"
        _git(root, "-c", "user.name=AI Website Studio", "-c", "user.email=studio@localhost", "commit", "-q",
             "--allow-empty", "--author", f"{who} <{who.split(':', 1)[0]}@ai-website-studio.local>", "-m", msg)
        head = _git(root, "rev-parse", "HEAD").stdout.strip()
        return {"ok": True, "commit": head}
    except Exception as e:  # noqa: BLE001
        return {"ok": False, "reason": f"{type(e).__name__}: {str(e)[:200]}"}


def log(root: Path, limit: int = 100) -> list[dict[str, Any]]:
    if not (root / ".git").is_dir():
        return []
    fmt = "%H%x1f%an%x1f%aI%x1f%s%x1f%b%x1e"
    out = _git(root, "log", f"-{min(limit, 1000)}", f"--format={fmt}").stdout
    items = []
    for rec in out.split("\x1e"):
        rec = rec.strip()
        if not rec:
            continue
        h, author, date, subject, body = (rec.split("\x1f") + [""] * 5)[:5]
        rev = next((int(line.split(":", 1)[1]) for line in body.splitlines() if line.startswith("Revision:")), None)
        items.append({"commit": h, "author": author, "date": date, "subject": subject, "revision": rev})
    return items


def _check_commit(commit: str) -> None:
    if not (7 <= len(commit) <= 40 and all(c in "0123456789abcdef" for c in commit)):
        raise ValidationFailed("commit must be a hex sha")


def model_at(root: Path, commit: str) -> dict[str, Any]:
    _check_commit(commit)
    r = _git(root, "show", f"{commit}:model/project.json", check=False)
    if r.returncode != 0:
        raise NotFound("commit or model not found", commit=commit)
    return json.loads(r.stdout)


def diff_stat(root: Path, a: str, b: str) -> dict[str, Any]:
    _check_commit(a)
    _check_commit(b)
    r = _git(root, "diff", "--numstat", a, b, "--", "model", "site")
    files = []
    for line in r.stdout.splitlines():
        add, rem, path = (line.split("\t") + ["", "", ""])[:3]
        files.append({"path": path, "added": None if add == "-" else int(add), "removed": None if rem == "-" else int(rem)})
    return {"from": a, "to": b, "files": files}
