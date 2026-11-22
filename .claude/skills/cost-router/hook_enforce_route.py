#!/usr/bin/env python3
"""PreToolUse hook (matcher "Edit|Write|NotebookEdit|Bash|Read|Grep|Glob") for the cost-router skill.

Makes the route binding: when the current prompt's route (written by hook_prompt_route.py to
~/.claude/cache/cost-router/route-<session_id>.json) names a subagent and the main session has not
delegated yet, the main session gets FREE_MAIN_CALLS (3) tool calls of its own (one-minute work);
the next one is denied (read-only Read/Grep/Glob only get the reason as additionalContext once; COST_ROUTER_DENY_READS=1 denies them too) with a reason telling it to delegate with the Agent tool.

Only the main session is bound: a call carrying a non-empty agent_id or agent_type comes from a
subagent and is allowed without touching any state. Allowed silently as well: no state, route.subagent null (main-session classes, trivial prompts, "#direct"),
state.delegated true (monotonic for the rest of the prompt once any Agent/Task spawn happened),
COST_ROUTER_ENFORCE=0. A Bash command that is ONE plain command (no ; & | redirect, comment or substitution) running
`python -m routing.route_cli route|dispatch` or the installed lib/vault_router.py route (see invokes_router) marks state.vault_routed and
is always allowed; it also records the task class in the session's route receipt cache
(route_state.add_routed_class), which hook_agent_model.py accepts for later prompts of the same
class. Not counted (`exempt`): one single read-only or one-shot git/gh command (git status/log/diff,
gh pr view/checks/merge/edit, ...; no chain, pipe or redirect) and a short Edit/Write of MEMORY.md,
a memory/*.md note or a todo-*.md checkpoint. Never both paths closed: when hook_agent_model.py denied a spawn (marker delegation_denied, per prompt
epoch) up to BREAKER_EXTRA_CALLS (6) calls past the free ones are allowed (the first with a one-line
additionalContext), then denied.
Any internal error: no output, exit 0 (allow).
"""
from __future__ import annotations

import json
import os
import posixpath
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))

# A router RECEIPT counts only for a SINGLE command: no ; & | < > backtick $( newline or comment outside/inside quotes
# (so nothing can ride along), whose interpreter is a recognised launcher (python/python3[.x][.exe] by name, `py [-3]`,
# `uv run python`, optionally behind `env` and VAR=value assignments) and that runs EITHER
#   - the INSTALLED lib/vault_router.py (absolute path, `~` and $HOME expanded, compared after realpath + normcase) with
#     the sub-command `route`, OR
#   - `-m routing.route_cli route|dispatch` (module form after -m only, never inside a -c string).
# Anything else (echo, a comment, a project copy of the script, a path elsewhere, `-c`) is not a receipt.
MAX_COMMAND = 2000
_ASSIGN = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*=")
_PY_NAME = re.compile(r"^(?:python[0-9.]*|py)(?:\.exe)?$", re.I)
_FLAGS = re.compile(r"^-[IEsSBdOqRuvb]+$")  # value-less interpreter flags; -c, -m and friends are handled explicitly


def _tokens(command: str):
    """Shell-ish words with quotes removed; None when the command is not a single plain command."""
    if not command or len(command) > MAX_COMMAND or any(c in command for c in "\n\r\x00`") or "$(" in command:
        return None
    out: list[str] = []
    cur: list[str] = []
    started = False
    quote = ""
    for ch in command:
        if quote:
            if ch == quote:
                quote = ""
            else:
                cur.append(ch)
        elif ch in "\"'":
            quote, started = ch, True
        elif ch in " \t":
            if started or cur:
                out.append("".join(cur))
                cur, started = [], False
        elif ch in ";&|<>(){}":
            return None
        elif ch == "#" and not (started or cur):
            return None  # a comment, or a word that starts with #
        else:
            cur.append(ch)
    if quote:
        return None
    if started or cur:
        out.append("".join(cur))
    return out or None


def _norm(path: str) -> str:
    return os.path.normcase(os.path.realpath(os.path.expandvars(os.path.expanduser(path))))


def _installed_wrappers() -> set:
    here = Path(__file__).resolve().parent / "lib" / "vault_router.py"
    home = Path(os.path.expanduser("~")) / ".claude" / "skills" / "cost-router" / "lib" / "vault_router.py"
    return {_norm(str(here)), _norm(str(home))}


# Exempt from the delegation counter (owner rule 2026-10-16): a single read-only or one-shot git/gh
# command and a one-line memory/checkpoint note. A subagent for these costs more than the action.
SINGLE_COMMAND = re.compile(
    r"^\s*(?:"
    r"git(?:\s+-C\s+[^\s\"']+)?\s+(?:status|log|diff|show|rev-parse|ls-files)"
    r"|git(?:\s+-C\s+[^\s\"']+)?\s+branch(?:\s+--show-current|\s+-[avr]+|\s+--list)?\s*$"
    r"|git(?:\s+-C\s+[^\s\"']+)?\s+remote\s+-v\s*$"
    r"|gh\s+pr\s+(?:view|checks|merge|edit|list|diff|comment|status|ready)"
    r"|gh\s+run\s+(?:view|list)"
    r"|gh\s+issue\s+(?:view|list|comment)"
    r")(?:\s|$)")
# Flags that make a "read-only" git/gh command run a program, write a file or bypass protection.
UNSAFE_FLAG = re.compile(r"(?:^|\s)(?:-c(?=\s|$)|--(?:output|ext-diff|textconv|upload-pack|receive-pack|exec|config|admin)(?=[\s=]|$))")
SHELL_CHAIN = re.compile(r"[;&|`\n<>]|\$\(")  # a chain, pipe, redirect or substitution is not single
MEMORY_NOTE = re.compile(r"(?:^|[\\/])(?:MEMORY\.md|memory[\\/][^\\/]+\.md|todo-[^\\/]+\.md)$", re.I)


def exempt(tool_name, tool_input: dict) -> bool:
    if tool_name == "Bash":
        cmd = str(tool_input.get("command") or "")
        return (len(cmd) <= 300 and not SHELL_CHAIN.search(cmd) and not UNSAFE_FLAG.search(cmd)
                and bool(SINGLE_COMMAND.match(cmd)))
    if tool_name in ("Edit", "Write"):
        path = str(tool_input.get("file_path") or "")
        if not MEMORY_NOTE.search(path):
            return False
        text = str(tool_input.get("new_string") if tool_name == "Edit" else tool_input.get("content") or "")
        return text.count("\n") <= (5 if tool_name == "Edit" else 60)  # a note or a checkpoint, not a dump
    return False


def invokes_router(command: str) -> bool:
    try:
        toks = _tokens(command)
        if not toks:
            return False
        i = 0
        if toks[i] == "env":
            i += 1
        while i < len(toks) and _ASSIGN.match(toks[i]):
            i += 1
        if i + 1 < len(toks) and toks[i] == "uv" and toks[i + 1] == "run":
            i += 2
        if i >= len(toks) or not _PY_NAME.match(os.path.basename(toks[i].replace("\\", "/"))):
            return False
        launcher = os.path.basename(toks[i].replace("\\", "/")).lower()
        i += 1
        if launcher.startswith("py") and not launcher.startswith("python") and i < len(toks) and re.match(r"^-[0-9][0-9.]*$", toks[i]):
            i += 1  # py -3 / py -3.12
        while i < len(toks):
            t = toks[i]
            if t in ("-X", "-W"):
                i += 2
            elif _FLAGS.match(t):
                i += 1
            else:
                break
        if i >= len(toks):
            return False
        if toks[i] == "-m":
            return i + 2 < len(toks) and toks[i + 1] == "routing.route_cli" and toks[i + 2] in ("route", "dispatch")
        if toks[i].startswith("-"):
            return False
        script = os.path.expandvars(os.path.expanduser(toks[i]))
        return (i + 1 < len(toks) and toks[i + 1] == "route" and os.path.isabs(script)  # absolute only: a relative path is the project's copy
                and _norm(script) in _installed_wrappers())
    except Exception:
        return False


def from_subagent(data: dict) -> bool:
    # Main-session payloads carry no agent_id/agent_type (probe-verified, Claude Code 2.1.304).
    return any(str(data.get(k) or "").strip() for k in ("agent_id", "agent_type"))


BREAKER_EXTRA_CALLS = 6  # direct calls allowed past the free ones after a refused spawn


class AllowNote(str):
    """decide() result that allows the call and carries one line of additionalContext."""


ALLOW_NOTE = AllowNote("cost-router: delegation was refused by the Agent gate; direct work allowed for this"
                       " prompt (run the Vault router to delegate)")


def _tier(policy, value):
    """Policy alias of a model name/id/alias ('claude-opus-5-5', 'opus[1m]', 'sonnet'), or None."""
    v = str(value or "").strip().lower()
    if not v:
        return None
    if v in policy.models:
        return v
    for alias, spec in policy.models.items():
        if v.startswith(spec.model_id.lower()):
            return alias
    for alias in policy.models:
        if alias in v:
            return alias
    return None


def _advisory_note(sid, state: dict):
    """A note when the route's model tier EQUALS the session's model tier (delegating to the same tier only adds
    overhead); None when the session model is unknown, above the route tier (delegation saves money) or below it
    (a risk-floored or review route must still go to the stronger model). Never raises."""
    try:
        import route_state  # noqa: E402
        from route import _load_router, policy_path  # noqa: E402
        if state.get("same_tier"):  # the prompt hook found session tier == route tier (class/risk checked there)
            return f"cost-router: same tier ({state.get('model')}): direct work ok, delegation is advisory here"
        # Fallback for states written before the prompt hook learned `same_tier`: the same class/risk exclusions
        # apply (review, design and frontier routes, and any risk-floored route, are never relaxed).
        if state.get("task_class") in ("review", "design", "frontier") or state.get("risk") in ("high", "critical"):
            return None
        session = route_state.session_model(sid)
        if not session:
            return None
        policy = _load_router().ClaudeModelPolicy.from_file(policy_path())
        s_tier, r_tier = _tier(policy, session), _tier(policy, state.get("model"))
        if s_tier and r_tier and policy.rank(r_tier) == policy.rank(s_tier):  # equal tiers only: never relax a
            return (f"cost-router: delegation is advisory here (session model {session} is on the route"  # risk floor
                    f" tier {r_tier})")
    except Exception:
        return None
    return None


# --- Best-effort role guard (declared limits of dfir-analyst and test-author) ---------------------------------------
# Both roles have Bash, so their limits are DECLARED in the agent file; this guard denies the obvious violations when
# the payload carries agent_type. Bash is Turing-complete (python -c, encoded commands, aliases): this is a speed bump,
# not a sandbox. It fails open on any error; only the explicit denials below are enforced.
_CMDPOS = (r"(?:^|[;&|(`]|\$\()\s*(?:(?:sudo|env|time|command|call|start|&)\s+|[A-Za-z_]\w*=\S*\s+)*")
_DFIR_CMDS = (
    r"curl|wget|iwr|irm|invoke-webrequest|invoke-restmethod|ssh|scp|sftp|ftp|nc|ncat|npm|npx|pnpm|yarn|"
    r"pip3?\s+install|python[\d.]*(?:\.exe)?\s+(?:-\w+\s+)*-m\s+(?:http\.server|pip\s+install|ensurepip)|py\s+(?:-[\d.]+\s+)?-m\s+(?:http\.server|pip\s+install)|"
    r"git(?:\s+-C\s+\S+)?\s+(?:push|fetch|clone|pull)|"
    r"out-file|set-content|add-content|tee|cp|mv|rm|del|erase|rmdir|copy|move|remove-item|new-item")
_DFIR_RE = re.compile(_CMDPOS + r"(?:" + _DFIR_CMDS + r")(?![\w.-])", re.I)
_BS = chr(92)
_QUOTED = re.compile('"(?:' + _BS * 2 + '.|[^"' + _BS * 2 + '])*"' + "|'[^']*'")
_SAFE_REDIR = re.compile(r"\d?>&\d|\d?>>?\s*(?:/dev/null|nul|\$null)(?![\w./-])", re.I)
_REDIR = re.compile(r"(?<![<-])>")
_TEST_DIRS = {"20_tests", "tests", "test"}
_TEST_FILE = re.compile(r"(?:^test_.*\.py$|_test\.py$|Tests\.cs$)", re.I)


def _deny(reason: str):
    return {"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": "deny",
                                   "permissionDecisionReason": reason}}


def role_guard(data: dict):
    """A deny payload for an obvious violation by dfir-analyst or test-author, else None. Never raises."""
    try:
        role = str(data.get("agent_type") or "").strip().lower()
        tool = data.get("tool_name")
        ti = data.get("tool_input") if isinstance(data.get("tool_input"), dict) else {}
        if role == "dfir-analyst" and tool == "Bash":
            cmd = ti.get("command")
            if not isinstance(cmd, str):
                return None
            flat = _SAFE_REDIR.sub(" ", _QUOTED.sub('""', cmd))
            if _DFIR_RE.search(flat) or _REDIR.search(flat):
                return _deny("cost-router: dfir-analyst is read-only and offline (network none, write none); this Bash"
                             " command reaches the network or writes a file. Report what you need instead.")
        elif role == "test-author" and tool in ("Write", "Edit", "NotebookEdit", "MultiEdit"):
            path = ti.get("file_path") or ti.get("notebook_path")
            if not isinstance(path, str) or not path.strip():
                return None
            parts = [x for x in posixpath.normpath(path.replace("\\", "/")).split("/") if x]
            if parts and not (_TEST_FILE.search(parts[-1]) or any(x.lower() in _TEST_DIRS for x in parts[:-1])):
                return _deny("cost-router: test-author writes tests only (a 20_TESTS/tests/test directory, test_*.py,"
                             " *_test.py, *Tests.cs); this path is production code. Report the needed change instead.")
    except Exception:
        return None
    return None


# --- M12: tool-level ALLOWLIST guard for agent_type `fable-executor` (threat model X1-X7, SKILL.md 4G) ---------------
# The executor's brief is attacker-shaped text, so the decision is taken on what it RUNS. Bash: one simple command
# (Git Bash quoting rules) whose program and sub-command are on the allowlist below; everything else is denied with
# the rule it broke. Write/Edit/NotebookEdit: the real path must be inside the git worktree of the session cwd, not in
# .git, not through a link, not in a nested repository, not in ~/.claude. COST_ROUTER_OFF, COST_ROUTER_ENFORCE=0 and
# #direct never disable it (X7). Any internal error DENIES (fail closed). Residuals (documented): the CONTENTS of an
# allowed worktree script, package.json script, test file or project file are not inspected; a same-user process
# outside Claude Code is out of scope.
FABLE_EXECUTOR_ROLE = "fable-executor"
_EX = "cost-router: fable-executor "
_EX_MAX = 2000
_EX_READ_ONLY = frozenset({"ls", "dir", "cat", "type", "get-content", "select-string", "rg", "grep", "find", "head",
                           "tail", "wc", "pwd"})
_EX_PY = re.compile(r"^(?:python(?:3(?:\.\d{1,2})?)?|py)$")
_EX_PY_FLAGS = re.compile(r"^-[IBuEsSOq]+$")
_EX_PY_X = frozenset({"utf8", "utf8=1", "utf8=0", "dev"})
_EX_GIT_READ = frozenset({"status", "diff", "log", "show", "blame", "rev-parse", "ls-files"})
_EX_GIT_BAD_FLAG = re.compile(r"^(?:--(?:output|ext-diff|textconv|exec|upload-pack|receive-pack|config|amend|"
                              r"open-files-in-pager)(?:=|$)|-O)")
_EX_BRANCH = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._/-]{0,99}$")
_EX_BRANCH_LIST = frozenset({"--show-current", "--list", "-a", "-v", "-vv", "-r", "-av", "-avv", "-l"})
_EX_PKG_FORMS = (("test",), ("run", "test"), ("run", "build"), ("run", "lint"))
_EX_FIND_BAD = frozenset({"-exec", "-execdir", "-ok", "-okdir", "-delete", "-fls"})
_EX_NET = frozenset({"curl", "wget", "iwr", "irm", "invoke-webrequest", "invoke-restmethod", "ssh", "scp", "sftp",
                     "ftp", "nc", "ncat", "pip", "pip3", "uv", "gh", "docker", "certutil", "start-bitstransfer", "npx"})
_EX_DEPLOY = frozenset({"vercel", "netlify", "firebase", "terraform", "kubectl", "helm", "twine", "az", "aws",
                        "gcloud"})
_EX_FS = frozenset({"rm", "rd", "rmdir", "del", "erase", "remove-item", "format", "diskpart", "cipher", "mv", "cp",
                    "move", "copy", "mkdir", "touch", "new-item", "set-content", "out-file", "ln", "mklink"})
_REPARSE = 0x400  # FILE_ATTRIBUTE_REPARSE_POINT: symlinks, junctions, mount points


def _ex_deny(rule: str, why: str) -> str:
    return f"{_EX}guard ({rule}): {why}; denied. Report what you need to the main session instead."


def _ex_normal(cmd: str) -> str:
    """NFKC, format chars (Cf: zero-width, bidi, BOM, soft hyphen) removed, look-alike letters folded to ASCII."""
    import unicodedata
    s = unicodedata.normalize("NFKC", cmd)
    s = "".join(c for c in s if unicodedata.category(c) != "Cf")
    try:
        from specialists import CONFUSABLES  # noqa: E402
        s = s.translate(CONFUSABLES)
    except Exception:
        pass
    return s


def _ex_words(cmd: str):
    """Words of ONE simple command with Git Bash quoting applied: [(text, unquoted_glob, leading_tilde)], or a deny
    reason (str) for any chain, pipe, redirect, substitution, expansion, comment or multi-line input."""
    if any(ord(c) < 32 and c != "\t" for c in cmd) or ord(max(cmd)) > 126:
        return _ex_deny("X5", "control characters or non-ASCII text in the command")
    words, cur, started, glob, tilde, quote, i = [], [], False, False, False, "", 0
    while i < len(cmd):
        ch = cmd[i]
        if quote == "'":
            if ch == "'":
                quote = ""
            else:
                cur.append(ch)
        elif quote == '"':
            if ch == '"':
                quote = ""
            elif ch in "$`":
                return _ex_deny("X5", "expansion or substitution inside double quotes")
            elif ch == "\\" and i + 1 < len(cmd) and cmd[i + 1] in '"\\':
                i += 1
                cur.append(cmd[i])
            else:
                cur.append(ch)
        elif ch in "'\"":
            quote, started = ch, True
        elif ch in " \t":
            if started or cur:
                words.append(("".join(cur), glob, tilde))
            cur, started, glob, tilde = [], False, False, False
        elif ch == "\\":
            if i + 1 >= len(cmd):
                return _ex_deny("X5", "line continuation")
            i += 1
            cur.append(cmd[i])
            started = True
        elif ch in ";&|<>()`$!{}@":
            return _ex_deny("X5", f"shell operator {ch!r} (one simple command only: no chain, pipe, redirect, "
                                  "substitution, here-string or variable)")
        elif ch == "#" and not (started or cur):
            return _ex_deny("X5", "a comment")
        else:
            if ch in "*?[":
                glob = True
            if ch == "~" and not (started or cur):
                tilde = True
            cur.append(ch)
        i += 1
    if quote:
        return _ex_deny("X5", "unbalanced quotes")
    if started or cur:
        words.append(("".join(cur), glob, tilde))
    return words or _ex_deny("X5", "an empty command")


def _ex_home_claude() -> str:
    return os.path.normcase(os.path.realpath(os.path.join(os.path.expanduser("~"), ".claude")))


def _ex_root(cwd: str):
    """Real path of the git worktree root that holds `cwd` (git rev-parse, 5 s), else the nearest ancestor with a
    .git entry, else None."""
    import subprocess
    top = None
    try:
        r = subprocess.run(["git", "-C", cwd, "rev-parse", "--show-toplevel"], capture_output=True, text=True,
                           timeout=5, encoding="utf-8", stdin=subprocess.DEVNULL)
        if r.returncode == 0 and r.stdout.strip():
            top = r.stdout.strip()
    except Exception:
        top = None
    if not top:
        d = os.path.abspath(cwd)
        while True:
            if os.path.lexists(os.path.join(d, ".git")):
                top = d
                break
            parent = os.path.dirname(d)
            if parent == d:
                return None
            d = parent
    root = os.path.realpath(top)
    return root if os.path.isdir(root) else None


def _ex_within(path: str, root: str) -> bool:
    a, b = os.path.normcase(path), os.path.normcase(root)
    try:
        return os.path.commonpath([a, b]) == b
    except ValueError:
        return False  # another drive


def _ex_shape_problem(raw: str):
    """Reasons a path string is never acceptable as a write/exec target, whatever it resolves to."""
    if not raw or not raw.strip():
        return "an empty path"
    p = raw.replace("/", "\\")
    if p.startswith("\\\\"):
        return "a UNC or device path"
    if re.match(r"^[A-Za-z]:(?!\\)", p):
        return "a drive-relative path"
    if ":" in (p[2:] if re.match(r"^[A-Za-z]:", p) else p):
        return "a colon in the path (alternate data stream or URL)"
    if "\x00" in p:
        return "a NUL byte in the path"
    return None


def _ex_resolve(raw: str, cwd: str, root: str):
    """(real, lexical) absolute paths of `raw` when it stays inside `root`, else a reason (str)."""
    bad = _ex_shape_problem(raw)
    if bad:
        return bad
    lexical = os.path.abspath(os.path.join(cwd, os.path.expanduser(raw) if raw.startswith("~") else raw))
    real = os.path.realpath(lexical)
    if not _ex_within(real, root):
        return f"{raw!r} resolves outside the git worktree {root}"
    if _ex_within(real, _ex_home_claude()):
        return f"{raw!r} is inside ~/.claude"
    return real, lexical


def _ex_is_link(p: str) -> bool:
    try:
        st = os.lstat(p)
    except OSError:
        return False
    if os.path.islink(p) or getattr(st, "st_file_attributes", 0) & _REPARSE:
        return True
    isj = getattr(os.path, "isjunction", None)
    return bool(isj and isj(p))


def executor_path_problem(raw, cwd) -> str | None:
    """None when a Write/Edit/NotebookEdit target is acceptable for fable-executor, else the deny reason (X6)."""
    if not isinstance(raw, str):
        return _ex_deny("X6", "no file path")
    root = _ex_root(cwd)
    if root is None:
        return _ex_deny("X6", "the session cwd is not inside a git worktree, so no write is allowed")
    got = _ex_resolve(raw, cwd, root)
    if isinstance(got, str):
        return _ex_deny("X6", got)
    real, lexical = got
    if not _ex_within(lexical, root):
        return _ex_deny("X6", f"{raw!r} reaches the worktree only through a link")
    lex_rel, real_rel = os.path.relpath(lexical, root), os.path.relpath(real, root)
    for part in re.split(r"[\\/]", real_rel) + re.split(r"[\\/]", lex_rel):
        if part.rstrip(". ").lower() == ".git":
            return _ex_deny("X6", f"{raw!r} is inside .git")
    cur = root
    parts = [x for x in re.split(r"[\\/]", lex_rel) if x and x != "."]
    for n, part in enumerate(parts):
        cur = os.path.join(cur, part)
        if _ex_is_link(cur):
            return _ex_deny("X6", f"{raw!r} goes through a symlink or junction ({part})")
        if n < len(parts) - 1 and os.path.lexists(os.path.join(cur, ".git")):
            return _ex_deny("X6", f"{raw!r} is inside another repository ({part})")
    try:
        if os.path.isfile(real) and os.stat(real).st_nlink > 1:
            return _ex_deny("X6", f"{raw!r} is a hard link")
    except OSError:
        pass
    return None


def _ex_path_args(words, cwd: str, root: str, rule: str = "X4"):
    """Every path-like argument (or --opt=value) of an executing program must stay inside the worktree."""
    for w in words:
        pieces = w.split("=")  # --opt=value and ini overrides such as `-o cache_dir=../x`: every piece is checked
        for val in ([w] if len(pieces) == 1 else pieces[1:]) + ([pieces[0]] if len(pieces) > 1 else []):
            val = val.split("::", 1)[0]  # pytest node id
            if not val or not ("/" in val or "\\" in val or val.startswith((".", "~")) or re.match(r"^[A-Za-z]:", val)):
                continue
            got = _ex_resolve(val, cwd, root)
            if isinstance(got, str):
                return _ex_deny(rule, got)
    return None


def _ex_git(args, cwd: str, root: str):
    i = 0
    while i < len(args) and args[i].startswith("-"):
        a = args[i]
        if a == "--no-pager":
            i += 1
        elif a == "-C" and i + 1 < len(args):
            got = _ex_resolve(args[i + 1], cwd, root)
            if isinstance(got, str):
                return _ex_deny("X1", f"git -C {got}")
            cwd = got[0]
            i += 2
        else:
            return _ex_deny("X1", f"git global option {a!r} is not allowed (config, git-dir and exec overrides)")
    if i >= len(args):
        return _ex_deny("X1", "bare git")
    sub, rest = args[i].lower(), args[i + 1:]
    bad = next((a for a in rest if _EX_GIT_BAD_FLAG.match(a)), None)
    if bad:
        return _ex_deny("X1", f"git flag {bad!r} runs a program, writes a file or rewrites history")
    if sub in _EX_GIT_READ or sub in ("add", "commit"):
        return None
    if sub == "branch" and all(a in _EX_BRANCH_LIST for a in rest):
        return None
    if sub == "stash" and rest == ["list"]:
        return None
    if (((sub == "switch" and rest[:1] in (["-c"], ["--create"])) or (sub == "checkout" and rest[:1] == ["-b"]))
            and len(rest) in (2, 3) and all(_EX_BRANCH.match(a) for a in rest[1:])):
        return None
    return _ex_deny("X1", f"git {sub!r} is not on the executor allowlist (status, diff, log, show, blame, add, "
                          "commit, switch -c/checkout -b <new branch>, stash list, branch listing)")


def _ex_python(prog: str, args, cwd: str, root: str):
    i = 0
    if prog == "py" and args[:1] and re.match(r"^-[23](?:\.\d{1,2})?$", args[0]):
        i = 1  # py -3 / py -3.12
    while i < len(args):
        a = args[i]
        if _EX_PY_FLAGS.match(a):
            i += 1
        elif a == "-X" and i + 1 < len(args) and args[i + 1] in _EX_PY_X:
            i += 2
        else:
            break
    if i >= len(args):
        return _ex_deny("X5", "an interactive interpreter (no script)")
    if args[i] == "-m":
        if i + 1 < len(args) and args[i + 1] == "pytest":
            return _ex_path_args(args[i + 2:], cwd, root)
        return _ex_deny("X2", "python -m is allowed only for pytest")
    if args[i].startswith("-"):
        return _ex_deny("X5", f"interpreter option {args[i]!r} (-c, stdin and the like are wrappers)")
    got = _ex_resolve(args[i], cwd, root)
    if isinstance(got, str):
        return _ex_deny("X4", "script " + got)
    return _ex_path_args(args[i + 1:], cwd, root)


def _ex_node(args, cwd: str, root: str):
    i = 1 if args[:1] == ["--test"] else 0
    if i >= len(args):
        return None if i else _ex_deny("X5", "an interactive interpreter (no script)")
    if args[i].startswith("-"):
        return _ex_deny("X5", f"node option {args[i]!r} (-e, -p, -r, --import and the like are wrappers)")
    got = _ex_resolve(args[i], cwd, root)
    if isinstance(got, str):
        return _ex_deny("X4", "script " + got)
    return _ex_path_args(args[i + 1:], cwd, root)


def executor_bash_problem(cmd, cwd) -> str | None:
    """None when a Bash command is on the fable-executor allowlist, else the deny reason naming the rule."""
    if not isinstance(cmd, str) or not cmd.strip():
        return _ex_deny("X5", "no command text")
    if len(cmd) > _EX_MAX:
        return _ex_deny("X5", f"a command longer than {_EX_MAX} characters")
    if _ex_normal(cmd) != cmd:
        return _ex_deny("X5", "Unicode look-alike, compatibility or zero-width characters in the command")
    words = _ex_words(cmd.strip())
    if isinstance(words, str):
        return words
    prog_raw = words[0][0]
    if "=" in prog_raw:
        return _ex_deny("X5", "an environment assignment before the command")
    if "/" in prog_raw or "\\" in prog_raw or words[0][2]:
        return _ex_deny("X5", "a program given by path (only allowlisted programs from PATH)")
    prog = prog_raw.lower()
    prog = prog[:-4] if prog.endswith(".exe") else prog
    args = [w[0] for w in words[1:]]
    if any(w[2] for w in words[1:]):
        return _ex_deny("X5", "home (~) expansion in an argument")
    if prog in _EX_READ_ONLY:
        if prog == "find" and any(a.lower() in _EX_FIND_BAD or a.lower().startswith("-fprint") for a in args):
            return _ex_deny("X4", "find with -exec/-delete/-fprint runs or writes")
        if prog == "rg" and any(a == "--pre" or a.startswith("--pre=") for a in args):
            return _ex_deny("X5", "rg --pre runs a program")
        return None
    executing = prog in ("git", "pytest", "py.test", "node", "npm", "pnpm", "yarn", "dotnet") or bool(_EX_PY.match(prog))
    if not executing:
        rule = "X2" if prog in _EX_NET else "X3" if prog in _EX_DEPLOY else "X4" if prog in _EX_FS else "X5"
        return _ex_deny(rule, f"program {prog!r} is not on the executor allowlist (pytest, python <worktree script>|"
                              "-m pytest, node <worktree script>, dotnet build|test, npm|pnpm|yarn test|run "
                              "test|build|lint, git local subcommands, read-only ls/cat/grep/rg/find)")
    if any(w[1] for w in words[1:]):
        return _ex_deny("X5", "an unquoted glob in an executing command")
    root = _ex_root(cwd)
    if root is None:
        return _ex_deny("X4", "the session cwd is not inside a git worktree, so only read-only commands run")
    if prog == "git":
        return _ex_git(args, cwd, root)
    if _EX_PY.match(prog):
        return _ex_python(prog, args, cwd, root)
    if prog in ("pytest", "py.test"):
        return _ex_path_args(args, cwd, root)
    if prog == "node":
        return _ex_node(args, cwd, root)
    if prog == "dotnet":
        if args[:1] not in (["build"], ["test"]):
            return _ex_deny("X3", "dotnet is allowed only for build and test")
        return _ex_path_args(args[1:], cwd, root)
    for form in _EX_PKG_FORMS:  # npm / pnpm / yarn
        n = len(form)
        if tuple(args[:n]) == form and (len(args) == n or args[n] == "--"):
            return _ex_path_args(args[n + 1:], cwd, root)
    return _ex_deny("X2", f"{prog} is allowed only for test / run test / run build / run lint (never install or "
                          "publish)")


_EX_WRITE_TOOLS = ("Write", "Edit", "NotebookEdit", "MultiEdit")
_EX_READ_TOOLS = ("Read", "Grep", "Glob")


def is_fable_executor(data: dict) -> bool:
    role = str(data.get("agent_type") or "").strip().lower()
    return role == FABLE_EXECUTOR_ROLE or role.endswith(":" + FABLE_EXECUTOR_ROLE)


def executor_guard(data: dict):
    """A deny payload for a fable-executor tool call outside the allowlist, else None; other callers: None.
    Fails CLOSED: any internal error denies."""
    try:
        if not isinstance(data, dict) or not is_fable_executor(data):
            return None
    except Exception:
        return None
    try:
        tool = data.get("tool_name")
        ti = data.get("tool_input") if isinstance(data.get("tool_input"), dict) else {}
        cwd = str(data.get("cwd") or os.getcwd())
        if tool in _EX_READ_TOOLS:
            return None
        if tool == "Bash":
            reason = executor_bash_problem(ti.get("command"), cwd)
        elif tool in _EX_WRITE_TOOLS:
            reason = executor_path_problem(ti.get("file_path") or ti.get("notebook_path"), cwd)
        else:
            reason = _ex_deny("X6", f"tool {tool!r} is not one of the executor's tools")
    except Exception as exc:
        reason = _ex_deny("X7", f"internal guard error ({type(exc).__name__}), failing closed")
    return _deny(reason) if reason else None


READ_TOOLS = ("Read", "Grep", "Glob")


def _soften(data: dict, calls: int, reason: str):
    """A deny of a read-only tool costs a whole retry request (prefix re-read) and the model re-issues the read
    anyway: allow it and deliver the reason once as context. COST_ROUTER_DENY_READS=1 restores the deny."""
    if data.get("tool_name") not in READ_TOOLS or os.environ.get("COST_ROUTER_DENY_READS", "").strip() == "1":
        return reason
    return AllowNote(reason) if calls == route_state_free() + 1 else None


def route_state_free() -> int:
    import route_state  # noqa: E402
    return route_state.FREE_MAIN_CALLS


def decide(data: dict):
    """Return a deny reason, an AllowNote (allow + context), or None to allow. Updates the session state as a side effect."""
    import route_state  # noqa: E402
    if route_state.enforcement_disabled() or from_subagent(data):
        return None
    sid = data.get("session_id")
    state = route_state.load(sid)
    if state is None:
        return None
    tool_input = data.get("tool_input") if isinstance(data.get("tool_input"), dict) else {}
    if data.get("tool_name") == "Bash" and invokes_router(str(tool_input.get("command") or "")):
        if not state.get("vault_routed"):
            route_state.mark(sid, "vault_routed")
        route_state.add_routed_class(sid, state.get("task_class"))  # receipt reused for this class
        return None
    if not state.get("subagent") or state.get("delegated"):
        return None
    if exempt(data.get("tool_name"), tool_input):
        return None
    tid = str(data.get("tool_use_id") or "")
    prior = route_state.seen_get(sid, "pre:" + tid) if tid else None
    if prior is not None:  # replay of an already counted call: same decision, no second count
        calls = int(prior) if str(prior).isdigit() else 1
    else:
        try:
            calls = int(state.get("main_calls") or 0) + 1
        except (TypeError, ValueError):
            calls = 1
        state["main_calls"] = calls
        route_state.save(sid, state)
        if tid:
            route_state.seen_event(sid, "pre:" + tid, calls)
    if calls <= route_state.FREE_MAIN_CALLS:
        return None
    advisory = _advisory_note(sid, state)
    if advisory:  # delegating to a model that is not cheaper than this session is pure overhead: never deny
        return AllowNote(advisory) if calls == route_state.FREE_MAIN_CALLS + 1 else None
    if state.get("delegation_denied"):  # the Agent gate refused the spawn: a capped direct allowance
        cap = route_state.FREE_MAIN_CALLS + BREAKER_EXTRA_CALLS
        if calls > cap:
            return _soften(data, calls, f"cost-router: direct-work allowance after a refused dispatch is used up ({cap} calls);"
                           " fix the blocker named by the Agent gate or put #direct in the prompt")
        return ALLOW_NOTE if calls == route_state.FREE_MAIN_CALLS + 1 else None
    return _soften(data, calls, f"cost-router: route is {state.get('task_class')} -> delegate to {state.get('subagent')}"
                   f" ({state.get('model')}) with the Agent tool; the main session dispatches, it does not do"
                   " this work itself. Owner override: put #direct in the prompt.")


def main() -> int:
    try:
        data = json.load(sys.stdin)
    except Exception:
        return 0
    if not isinstance(data, dict):
        return 0
    ex = executor_guard(data)  # M12: before every switch (COST_ROUTER_OFF, ENFORCE=0, #direct, shadowing) - X7
    if ex:
        print(json.dumps(ex))
        return 0
    try:  # COST_ROUTER_OFF=1: equal-arm benchmarks / owner kill switch; no output, no state
        import route_state as _rs  # noqa: E402
        if _rs.disabled_by_env():
            return 0
    except Exception:
        pass
    try:
        from route import is_shadowed  # noqa: E402
        if is_shadowed(Path(__file__)):
            return 0
        guard = None
        try:
            import route_state as _rs2  # noqa: E402
            if not _rs2.enforcement_disabled():
                guard = role_guard(data)
        except Exception:
            guard = None
        if guard:
            print(json.dumps(guard))
            return 0
        reason = decide(data)
    except Exception:
        return 0
    if isinstance(reason, AllowNote):
        print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse", "additionalContext": str(reason)}}))
    elif reason:
        print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse",
                                                 "permissionDecision": "deny",
                                                 "permissionDecisionReason": reason}}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
