#!/usr/bin/env python3
"""PreToolUse hook (matcher "Agent|Task") for the cost-router skill.

Sets the model of every subagent spawn from the policy route of its brief (description + prompt):
  - the exact `fable-consultant` agent -> Fable 5.2, read-only and advisory only (requires an opus session);
  - the exact `fable-executor` agent -> Fable 5.2 only through _fable_executor_gate (default off, SKILL.md 4G);
  - every other Fable alias/model ID -> Opus (or capped to session tier), including unclassified briefs;
  - no model given             -> the route's model;
  - a model given              -> kept between the route's model and one tier above it (a one-notch
                                  escalation is allowed; a model cheaper than the route is raised);
  - risk detected in the brief -> never below the risk floor (Opus);
  - a reviewer/verifier subagent_type ("review"/"verif" in it) -> never below Opus.
A brief that matches no task class and carries no risk is left alone (the subagent's own
frontmatter model applies, except `inherit` / Fable which are forced to Opus) unless that default is
above the session tier or UNKNOWN (a plugin `a:b` type, a type with no project/user agent file that
is not a built-in, an unreadable file or an unrecognised `model:`), which is capped to the session
tier with a note. An explicit model the policy does not recognise (alias, exact id, alias with a
`[..]` suffix, longest model_id prefix) is never passed through: on a risky/review brief the floor
applies, on a classified brief the route model, on an unclassified brief the session tier (with a note).
Route state (lib/route_state.py): every spawn sets state.delegated=true, which unlocks the main
session for hook_enforce_route.py. Inside a vault root PINNED at install time (~/.claude/skills/cost-router/config.json; a directory merely shaped like
the vault is not enough) a spawn for an implement/review/design/frontier
route first runs the Vault router in-hook (`python -I`, package path from the pinned root, never the project cwd; 5 s): ROUTED/PLANNED records the
receipt and allows; BLOCKED or an unavailable router denies (delegated is not set then). A receipt from
Bash (state.vault_routed, set by hook_enforce_route.py) also passes. "#direct" and
COST_ROUTER_ENFORCE=0 skip that gate (they do NOT disable the Fable guard). Outside the Vault it is never required.
The `vault-router: ...` / `subagent model x -> y` context line is emitted only with COST_ROUTER_VERBOSE_ROUTE=1 (denials always carry their reason). A read-only subagent type (an agent file whose frontmatter `tools` are all in the read-only allowlist: Read, Grep, Glob, LS, WebFetch, WebSearch, the vault-memory read tools; the project's Explore has Bash and is NOT read-only) is clamped in the explore class (haiku, cap one tier above) only for briefs with no risk rule and a class outside review/design/frontier (COST_ROUTER_READONLY_FLOOR=1 restores the old floor).
Fable consultant routing fails closed when its policy cannot be loaded, and malformed Agent hook payloads exit 2. The Agent hook is configured with onFailure=block; the installed copy shadows the project hook when registered as blocking and the project's release-pin seq is not higher than the installed one (equal or lower or no pin: the installed copy answers; higher: exit 2, stale install).
"""
from __future__ import annotations

import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile
import time
import unicodedata
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))

RISK_PREFIXES = ("risk_detected", "risk_floor")
SPAWN_TOOLS = ("Agent", "Task")  # "Task" is the legacy name of the spawn tool
FABLE_CONSULTANT = "fable-consultant"
FABLE_EXECUTOR = "fable-executor"  # M11: gated, default off (see _fable_executor_gate)
HEAVY_CLASSES = ("implement", "design", "frontier")  # M12 condition (i): the owner prompt's route class, never the brief
REVIEWER_MARKS = ("review", "verif")  # vault-reviewer, code-review, verifier: never below Opus
# vault-worker, implementer: routed as implement; on risky code the floor moves to the mandatory
# Opus reviewer (policy rules.implementer_risk_floor), so the worker stays on Sonnet.
WORKER_MARKS = ("worker", "implement")
VAULT_ROUTER_REASON = ("cost-router: run the Vault router first: python -m routing.route_cli route --goal \"<task>\""
                       " (PYTHONPATH=03_IMPLEMENTATION/packages), show the owner agent/model/cost, then dispatch.")


def _cached_receipt(route_state, sid, task_class) -> bool:
    """The Vault router already ran for this task class in this session: reuse that receipt."""
    try:
        return route_state.class_routed(sid, task_class)
    except AttributeError:  # an older lib copy
        return False


ROUTER_TIMEOUT = 5
_ROUTER_RUNNER = ("import sys, runpy; sys.path.insert(0, sys.argv[1]); sys.argv = ['route_cli'] + sys.argv[2:]; "
                  "runpy.run_module('routing.route_cli', run_name='__main__', alter_sys=True)")
_ROUTER_NOTE = ""  # one context line set by gate_and_mark when the in-hook Vault router allowed the spawn


def _goal(tool_input: dict) -> str:
    raw = str(tool_input.get("prompt") or tool_input.get("description") or "").strip()
    return " ".join(raw.split())[:300] or "<unnamed subagent task>"


def _run_vault_router(route_state, data: dict, goal: str):
    """Run `routing.route_cli route` in-hook. Returns (decision dict, None) or (None, short cause)."""
    # Only a vault root PINNED at install time (~/.claude/skills/cost-router/config.json) ever runs code: the project
    # directory is untrusted input (a cloned repo can be shaped like the vault).
    root = route_state.pinned_vault_root(data)
    if root is None:
        return None, "project is not a pinned vault root"
    env = {k: v for k, v in os.environ.items() if not k.upper().startswith("PYTHON")}
    pkgs = str(root / "03_IMPLEMENTATION" / "packages")
    # `-I` (isolated): no PYTHONPATH, no cwd / script-dir on sys.path, no user site; the package path is explicit,
    # taken from the pinned root. argv list (never a shell), one `--goal=<goal>` element so a dash-leading goal is
    # not an option; stdout goes to a temp file, not a pipe, so a grandchild holding the handle cannot outlive the
    # timeout.
    cmd = [sys.executable, "-I", "-c", _ROUTER_RUNNER, pkgs, "route", f"--goal={goal}"]
    fd, tmp = tempfile.mkstemp(prefix="cost-router-route-", suffix=".out")
    returncode = None
    try:
        with os.fdopen(fd, "wb") as fh:
            try:
                proc = subprocess.Popen(cmd, cwd=str(root), env=env, stdin=subprocess.DEVNULL, stdout=fh,
                                        stderr=subprocess.DEVNULL)
                try:
                    returncode = proc.wait(timeout=ROUTER_TIMEOUT)
                except subprocess.TimeoutExpired:
                    _kill_tree(proc)
                    return None, f"timeout {ROUTER_TIMEOUT}s"
            except Exception as exc:
                return None, type(exc).__name__
        out = Path(tmp).read_bytes().decode("utf-8", errors="replace")
    finally:
        try:
            os.unlink(tmp)
        except OSError:
            pass
    i = out.find("{")
    dec = None
    if i >= 0:
        try:
            dec, _ = json.JSONDecoder().raw_decode(out[i:])
        except ValueError:
            dec = None
    # the JSON status wins whatever the exit code: route_cli exits 1 on BLOCKED
    if isinstance(dec, dict) and isinstance(dec.get("status"), str):
        return dec, None
    if returncode:
        return None, f"exit {returncode}"
    return None, "no JSON output" if i < 0 else "no status"


def _kill_tree(proc) -> None:
    try:
        if os.name == "nt":
            subprocess.run(["taskkill", "/T", "/F", "/PID", str(proc.pid)], stdin=subprocess.DEVNULL,
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=3)
        else:
            proc.kill()
    except Exception:
        pass
    try:
        proc.kill()
        proc.wait(timeout=2)
    except Exception:
        pass


def _blocked_reason(dec: dict) -> str:
    detail = None
    for key in ("rejected", "warnings", "reason"):  # never the fixed policy_gates list
        val = dec.get(key)
        if val:
            detail = val
            break
    if isinstance(detail, (list, tuple)):
        parts = []
        for item in detail:
            if isinstance(item, dict):
                item = " ".join(str(item[k]) for k in ("name", "id", "agent_id", "reason") if item.get(k)) or json.dumps(item, default=str)
            if str(item) not in parts:  # the real router repeats one rejection per runtime: keep each once
                parts.append(str(item))
        warnings = dec.get("warnings") if detail is not dec.get("warnings") else None
        if isinstance(warnings, (list, tuple)):  # keep the availability warning next to the rejections
            parts.extend(str(w) for w in warnings if str(w) not in parts)
        detail = "; ".join(parts)
    elif detail is not None and not isinstance(detail, str):
        detail = json.dumps(detail, ensure_ascii=False, default=str)
    detail = " ".join((detail or "no detail").split())[:160]
    return (f"cost-router: Vault router BLOCKED this dispatch ({detail}); do the work directly or change the task")


def gate_and_mark(data: dict):
    """Return a deny reason for the Vault-router gate, else mark the session delegated and return None.

    Where the gate would deny, the Vault router runs first, in-hook: ROUTED/PLANNED records the receipt and
    allows; BLOCKED or an unavailable router denies (and unlocks the capped direct-work allowance)."""
    global _ROUTER_NOTE
    _ROUTER_NOTE = ""
    try:
        import route_state  # noqa: E402
        sid = data.get("session_id")
        state = route_state.load(sid)
        if state is None:
            return None
        if (not route_state.enforcement_disabled() and not state.get("direct")
                and state.get("task_class") in route_state.VAULT_GATED_CLASSES
                and not state.get("vault_routed") and route_state.in_vault(data)
                and not _cached_receipt(route_state, sid, state.get("task_class"))):
            tool_input = data.get("tool_input") if isinstance(data.get("tool_input"), dict) else {}
            dec, cause = _run_vault_router(route_state, data, _goal(tool_input))
            status = dec.get("status") if dec else None
            if status in ("ROUTED", "PLANNED"):
                contract = dec.get("execution_contract")
                contract = contract if isinstance(contract, dict) else {}
                stype = contract.get("subagent_type")
                via = contract.get("execute_via")
                state["vault_route"] = {"status": status, "execute_via": via, "subagent_type": stype,
                                        "ts": int(time.time())}
                if via != "claude_code_agent_tool":
                    route_state.save(sid, state)  # no receipt: this route is not a Claude subagent
                    route_state.mark(sid, "delegation_denied")
                    primary = dec.get("primary")
                    runtime = primary.get("runtime_id") if isinstance(primary, dict) else None
                    target = f"{via or 'another runtime'}/{runtime or stype or 'n/a'}"
                    return (f"cost-router: Vault router routed this task to {target} instead of a Claude subagent;"
                            " dispatch it with python -m routing.route_cli dispatch --goal \"...\" --execute,"
                            " or do the work directly")
                route_state.add_routed_class(sid, state.get("task_class"))
                route_state.save(sid, state)
                _ROUTER_NOTE = f"vault-router: {status} -> {stype or 'n/a'}"[:120]
            else:
                # never close both paths: tell hook_enforce_route.py direct work is now allowed (capped)
                route_state.mark(sid, "delegation_denied")
                if status == "BLOCKED":
                    return _blocked_reason(dec)
                return f"{VAULT_ROUTER_REASON} (in-hook router unavailable: {cause or 'status ' + str(status)[:40]})"
        if not state.get("delegated"):
            route_state.mark(sid, "delegated")  # a marker file: no counter write can clear it
    except Exception:
        return None
    return None


def _resolve_model(policy, value):
    """Policy alias of a model string, or None when unrecognised: exact alias or model_id, an alias with a `[..]`
    suffix (`opus[1m]`), else the LONGEST model_id prefix followed by "-" or "[" (the lib/transcript_usage.py
    matcher). Family words inside other ids (`claude-opus-4-1`, `gpt-5`) are NOT guessed."""
    v = str(value).strip().lower()
    if not v:
        return None
    for alias, spec in policy.models.items():
        if v == alias.lower() or v == str(spec.model_id).lower():
            return alias
    m = re.fullmatch(r"([a-z]+)\[[a-z0-9]+\]", v)
    if m and m.group(1) in policy.models:
        return m.group(1)
    hit, best = None, 0
    for alias, spec in policy.models.items():
        mid = str(spec.model_id).lower()
        if len(mid) > best and len(v) > len(mid) and v.startswith(mid) and v[len(mid)] in "-[":
            hit, best = alias, len(mid)
    return hit


def _alias(policy, value):
    """None when no model was given (key absent / null); "unknown" for any other unrecognised value ("" included)."""
    if value is None:
        return None
    return _resolve_model(policy, value) or "unknown"


def _model_label(value) -> str:
    return repr(" ".join(str(value).split())[:40])


def _requests_fable(value) -> bool:
    """Recognize first-party aliases and provider-prefixed Fable model IDs."""
    return "fable" in str(value or "").strip().lower()


def _is_inherit(value) -> bool:
    return str(value or "").strip().lower() == "inherit"


def _session_is_fable(data: dict) -> bool:
    """Read-only (no state written, so COST_ROUTER_OFF stays inert): does the session run Fable? Payload model, then
    the transcript's last assistant model, then the stored session model, then ANTHROPIC_MODEL."""
    try:
        import route_state  # noqa: E402
        found = data.get("model") if isinstance(data.get("model"), str) else None
        if not (found and found.strip()):
            found = None
            path = data.get("transcript_path")
            if path:
                try:
                    from transcript_usage import last_assistant_model  # noqa: E402
                    found = last_assistant_model(path)
                except Exception:
                    found = None
            found = found or route_state.session_model(data.get("session_id")) or os.environ.get("ANTHROPIC_MODEL")
        return _requests_fable(found)
    except Exception:
        return False


def _pin_seq(skill_dir: Path):
    """seq of the release pin inside a skill directory, or None (absent, unreadable or invalid)."""
    try:
        import release_pin  # noqa: E402  (lib/ is on sys.path)
        return release_pin.load_pin(skill_dir / release_pin.PIN_NAME)["seq"]
    except Exception:
        return None


def _shadow_verdict(hook_file: Path):
    """The project copy of this hook next to a registered user-scope install: ("defer", None) when the installed copy
    answers (the project copy stays silent), ("stale", message) when the install is older than this project's release.

    Compared by release-pin `seq`, never by bytes: a project on another commit of the vault (or a copy that merely
    differs) must not block every agent. Equal seq -> the installed copy answers; project seq higher -> stale install,
    block with a message that names both; no readable pin in the project, or a lower one -> the installed copy answers."""
    home_claude = Path.home() / ".claude"
    user_skill = home_claude / "skills" / "cost-router"
    if not (user_skill / hook_file.name).is_file():
        return "stale", "the installed hook file is missing"
    project_seq = _pin_seq(hook_file.parent)
    if project_seq is None:
        return "defer", None
    installed_seq = _pin_seq(user_skill)
    if installed_seq is None or project_seq > installed_seq:
        return "stale", (f"this project ships release seq {project_seq}, the installed copy has "
                         f"{installed_seq if installed_seq is not None else 'no release pin'}")
    return "defer", None


def _user_agent_hook_is_registered(settings_path=None) -> bool:
    """Confirm the user-scope Agent hook is actually registered as a blocking PreToolUse hook."""
    path = Path(settings_path) if settings_path else Path.home() / ".claude" / "settings.json"
    try:
        settings = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError):
        return False
    hooks = settings.get("hooks") if isinstance(settings, dict) else None
    entries = hooks.get("PreToolUse") if isinstance(hooks, dict) else None
    if not isinstance(entries, list):
        return False
    for entry in entries:
        if not isinstance(entry, dict) or entry.get("matcher") not in {"Agent|Task", "Task|Agent"}:
            continue
        commands = entry.get("hooks")
        if not isinstance(commands, list):
            continue
        for hook in commands:
            if not isinstance(hook, dict) or hook.get("type") != "command":
                continue
            command = str(hook.get("command") or "").replace("\\", "/")
            if "cost-router/hook_agent_model.py" in command and hook.get("onFailure") == "block":
                return True
    return False


FABLE_CONSULTANT_FRONTMATTER_KEYS = frozenset({
    "name", "description", "tools", "disallowedTools", "model", "effort", "permissionMode",
})
FABLE_CONSULTANT_TOOLS = frozenset({"Read", "Grep", "Glob"})
FABLE_CONSULTANT_REQUIRED_DENIALS = frozenset({
    "Agent", "Bash", "PowerShell", "Edit", "Write", "NotebookEdit", "Skill",
    "WebFetch", "WebSearch", "ToolSearch", "mcp__*",
})


def _read_agent_frontmatter(path: Path):
    """Read scalar YAML frontmatter; preserve invalid/unreadable states for fail-closed routing."""
    try:
        lines = path.read_text(encoding="utf-8-sig").splitlines()
    except (OSError, UnicodeError):
        return {"__invalid__": True, "__unreadable__": True, "model": "fable-unresolved"}
    while lines and not lines[0].strip():
        lines.pop(0)
    if not lines or lines[0].strip() != "---":
        return None
    fields = {}
    closed = False
    for line in lines[1:]:
        if line.strip() == "---":
            closed = True
            break
        value = line.strip()
        if not value or value.startswith("#"):
            continue
        key, sep, raw = value.partition(":")
        key = key.strip()
        if not sep or not key or key in fields:
            fields["__invalid__"] = True
            continue
        fields[key] = raw.strip().strip("\"'")
    if not closed:
        fields["__invalid__"] = True
    return fields


def _agent_frontmatter(subagent_type: str):
    """Resolve a subagent by declared name; unreadable/ambiguous scope is not trusted."""
    return _agent_resolve(subagent_type)[0]


def _agent_resolve(subagent_type: str):
    """(frontmatter fields or None/invalid marker, the definition file or None) of a subagent."""
    raw_type = str(subagent_type or "").strip()
    name = raw_type.split(":")[-1].lower()
    if not name:
        return None, None
    project = Path(os.environ.get("CLAUDE_PROJECT_DIR") or Path.cwd()).resolve()
    cwd = Path.cwd().resolve()
    try:
        cwd.relative_to(project)
        roots = [cwd]
        while roots[-1] != project:
            roots.append(roots[-1].parent)
    except ValueError:
        roots = [project]
        if cwd != project:
            roots.append(cwd)
    roots.append(Path.home())
    roots = tuple(dict.fromkeys(roots))
    plugin = raw_type.split(":", 1)[0].lower() if ":" in raw_type else None
    bases = [(root / ".claude" / "agents", None) for root in roots]
    if plugin:
        bases.extend((root / ".claude" / "plugins", plugin) for root in roots)
    for base, plugin_scope in bases:
        if not base.is_dir():
            continue
        matches = []
        unreadable = False
        try:
            candidates = sorted(path for path in base.rglob("*.md") if path.is_file())
        except OSError:
            return {"__invalid__": True, "name": name, "model": "fable-unresolved"}, None
        for candidate in candidates:
            try:
                fields = _read_agent_frontmatter(candidate)
            except Exception:
                unreadable = True
                continue
            if fields and fields.get("__unreadable__"):
                unreadable = True
                continue
            if fields is None or _norm_name(fields.get("name")) != name:
                continue
            if plugin_scope:
                try:
                    if plugin_scope not in candidate.resolve().as_posix().lower():
                        continue
                except OSError:
                    unreadable = True
                    continue
            matches.append((fields, candidate))
        if unreadable:
            return {"__invalid__": True, "name": name, "model": "fable-unresolved"}, None
        if len(matches) == 1:
            return matches[0]
        if len(matches) > 1:
            return {"__invalid__": True, "name": name, "model": "fable-ambiguous"}, None
    return None, None


PINNED_AGENTS = "agents.pinned.json"  # {file name: sha256 of the SOURCE agent file}, written by install.py
PRIVILEGED_AGENTS = frozenset({FABLE_CONSULTANT, FABLE_EXECUTOR, "vault-reviewer"})


def _pinned_agent_hashes():
    """{agent name: sha256} pinned at install time, from the INSTALLED skill only (never from the project); None when
    nothing is installed or the file is unreadable, which keeps the structural checks as the only guard."""
    try:
        data = json.loads((Path.home() / ".claude" / "skills" / "cost-router" / PINNED_AGENTS).read_text(encoding="utf-8"))
        return {Path(k).stem.lower(): v for k, v in data.items() if isinstance(k, str) and isinstance(v, str)}
    except Exception:
        return None


def _under_home_claude(path: Path) -> bool:
    try:
        path.resolve().relative_to((Path.home() / ".claude").resolve())
        return True
    except (ValueError, OSError):
        return False


_ZERO_WIDTH = dict.fromkeys(map(ord, "\u200b\u200c\u200d\u2060\ufeff\u00ad\u180e\u200e\u200f"))


def _norm_name(value) -> str:
    """An agent name as Claude Code would compare it: zero-width characters and BOM removed, NFKC, a trailing YAML
    comment dropped, quotes stripped, casefolded."""
    text = unicodedata.normalize("NFKC", str(value or "")).translate(_ZERO_WIDTH)
    text = re.split(r"\s#", text, maxsplit=1)[0].strip().strip("\"'").translate(_ZERO_WIDTH).strip()
    return text.casefold()


def _project_agent_roots() -> list:
    """The project and cwd directories whose `.claude/agents` Claude Code searches before the home directory."""
    project = Path(os.environ.get("CLAUDE_PROJECT_DIR") or Path.cwd()).resolve()
    cwd = Path.cwd().resolve()
    try:
        cwd.relative_to(project)
        roots = [cwd]
        while roots[-1] != project:
            roots.append(roots[-1].parent)
    except ValueError:
        roots = [project] + ([cwd] if cwd != project else [])
    home = Path.home().resolve()
    return [r for r in dict.fromkeys(roots) if r != home]


def _lf_sha256(raw: bytes) -> str:
    """sha256 of an agent definition with line endings normalized to LF (CRLF and CR): a Windows checkout with
    core.autocrlf must hash like the pinned (LF) source. install.py applies the same normalization."""
    return hashlib.sha256(raw.replace(b"\r\n", b"\n").replace(b"\r", b"\n")).hexdigest()


_PLAIN_NAME = re.compile(r"^name:[ \t]*([A-Za-z0-9_-]+)[ \t]*(?:[ \t]#.*)?$")
_NAMEISH = re.compile(r"""^[ \t]*\??[ \t]*["']?name["']?[ \t]*:""", re.IGNORECASE)
_TOP_KEY = re.compile(r"^(?:[A-Za-z_][A-Za-z0-9_-]*[ \t]*:(?:[ \t]|$)|-(?:[ \t]|$))")


def _strict_agent_name(raw: bytes):
    """(name casefolded, None) when the file's frontmatter has exactly ONE plain `name: <[A-Za-z0-9_-]+>` line (a
    trailing ` # comment` is stripped) and no other YAML form of the name key (quotes, escapes, tags, anchors, block
    scalars, flow mappings, `? name`, multi-line plain scalars, duplicates); else (None, why)."""
    try:
        text = raw.decode("utf-8")
    except UnicodeError:
        return None, "not valid UTF-8"
    if text.startswith("﻿"):
        text = text[1:]
    lines = text.replace("\r\n", "\n").replace("\r", "\n").split("\n")
    i = 0
    while i < len(lines) and not lines[i].strip():
        i += 1
    if i >= len(lines) or lines[i].rstrip() != "---":
        return None, "no frontmatter"
    name = None
    after_name = False
    closed = False
    for ln in lines[i + 1:]:
        if ln.rstrip() == "---":
            closed = True
            break
        if not ln.strip() or ln.lstrip().startswith("#"):
            continue
        indented = ln[0] in " \t"
        if after_name and indented:
            return None, "the name value continues on the next line"
        after_name = False
        if _NAMEISH.match(ln):
            m = _PLAIN_NAME.match(ln)
            if not m or name is not None:
                return None, "the name is not a single plain `name: value` line"
            name = m.group(1).casefold()
            after_name = True
            continue
        if not indented and not _TOP_KEY.match(ln):
            return None, "an unsupported YAML form (not a plain `key: value` line)"
    if not closed:
        return None, "unclosed frontmatter"
    if name is None:
        return None, "no `name:` line"
    return name, None


def _project_override_problem(pinned, targets):
    """A message when a project `.claude/agents/**/*.md` cannot be classified or claims a privileged name that is
    one of the `targets` (the spawned agent, plus fable-consultant on a Fable-sensitive spawn) without being the pinned
    definition (LF-normalized sha256). Every file must carry exactly one plain `name:` line (_strict_agent_name): a
    file that does not may claim a privileged name in a form the Claude Code YAML parser reads differently from this
    one, so it is treated as possibly privileged. Files of other, plain-named agents are out of scope."""
    for root in _project_agent_roots():
        base = root / ".claude" / "agents"
        if not base.is_dir():
            continue
        try:
            files = sorted(p for p in base.rglob("*.md") if p.is_file())
        except OSError:
            return f"cannot list {base}"
        for f in files:
            rel = f.relative_to(base).as_posix()
            try:
                raw = f.read_bytes()
            except OSError:
                return (f"project agent definition {rel} is unreadable, so it cannot be checked against the pinned "
                        "definitions")
            name, why = _strict_agent_name(raw)
            if name is None:
                return (f"project agent definition {rel} may claim a privileged agent name ({why}) and cannot be "
                        "checked against the pinned definitions")
            digest = _lf_sha256(raw)
            for claimed in sorted({name, _norm_name(f.stem)} & PRIVILEGED_AGENTS & set(targets)):
                if pinned.get(claimed) != digest:
                    return (f"project agent definition {rel} claims the privileged name {claimed!r} and does not "
                            "match the pinned sha256 recorded at install")
    return None


def _definition_mismatch(stype: str, fields, path, pinned):
    """A message when a PRIVILEGED agent definition (fable-consultant, vault-reviewer, any agent on opus or fable) is
    not the one pinned at install time: a project `.claude/agents/<name>.md` is searched before the home directory and
    its body is not part of any frontmatter check. None when it matches, is not privileged, or nothing is pinned."""
    if pinned is None or not isinstance(fields, dict) or path is None:
        return None
    name = str(fields.get("name") or stype).strip().lower()
    model = str(fields.get("model") or "").lower()
    privileged = (name in PRIVILEGED_AGENTS or stype in PRIVILEGED_AGENTS or "opus" in model or "fable" in model)
    if not privileged:
        return None
    try:
        actual = _lf_sha256(Path(path).read_bytes())
    except OSError:
        return f"definition of agent {name!r} is unreadable"
    if pinned.get(name) == actual:
        return None
    if name not in pinned and _under_home_claude(Path(path)):
        return None  # the owner's own user-level agent, never pinned by the installer
    return (f"definition of agent {name!r} ({Path(path).name}) does not match the pinned sha256 recorded at install"
            if name in pinned else
            f"definition of agent {name!r} ({Path(path).name}) is an unpinned project definition")


def _agent_frontmatter_model(subagent_type: str):
    fields = _agent_frontmatter(subagent_type)
    return fields.get("model") if fields else None


def _csv_values(value) -> frozenset[str]:
    if not isinstance(value, str):
        return frozenset()
    return frozenset(item.strip() for item in value.split(",") if item.strip())


def _valid_fable_consultant_frontmatter(fields) -> bool:
    """Require the exact, pinned, read-only consultant definition before granting Fable."""
    if not isinstance(fields, dict) or fields.get("__invalid__"):
        return False
    if set(fields) != FABLE_CONSULTANT_FRONTMATTER_KEYS:
        return False
    return (
        fields.get("name") == FABLE_CONSULTANT
        and bool(str(fields.get("description") or "").strip())
        and _csv_values(fields.get("tools")) == FABLE_CONSULTANT_TOOLS
        and FABLE_CONSULTANT_REQUIRED_DENIALS <= _csv_values(fields.get("disallowedTools"))
        and fields.get("model") == "claude-fable-5-2"
        and fields.get("effort") == "high"
        and fields.get("permissionMode") == "plan"
    )


FABLE_EXECUTOR_FRONTMATTER_KEYS = frozenset({"name", "description", "tools", "disallowedTools", "model", "effort"})
FABLE_EXECUTOR_TOOLS = frozenset({"Read", "Grep", "Glob", "Bash", "Edit", "Write"})
FABLE_EXECUTOR_REQUIRED_DENIALS = frozenset({
    "Agent", "Task", "NotebookEdit", "WebFetch", "WebSearch", "ToolSearch", "Skill", "PowerShell", "mcp__*",
})


def _valid_fable_executor_frontmatter(fields) -> bool:
    """The exact, pinned fable-executor definition: its OWN key set and tool set (never a relaxed consultant check).
    Workspace tools only: no Agent/Task (spawn), no MCP, no WebFetch/WebSearch, no NotebookEdit."""
    if not isinstance(fields, dict) or fields.get("__invalid__"):
        return False
    if set(fields) != FABLE_EXECUTOR_FRONTMATTER_KEYS:
        return False
    return (
        fields.get("name") == FABLE_EXECUTOR
        and bool(str(fields.get("description") or "").strip())
        and _csv_values(fields.get("tools")) == FABLE_EXECUTOR_TOOLS
        and FABLE_EXECUTOR_REQUIRED_DENIALS <= _csv_values(fields.get("disallowedTools"))
        and fields.get("model") == "claude-fable-5-2"
        and fields.get("effort") == "high"
    )


READ_ONLY_TOOLS = frozenset({"Read", "Grep", "Glob", "LS", "WebFetch", "WebSearch"})
READ_ONLY_MCP = re.compile(r"mcp__vault-memory__(?:memory_search|memory_get|vault_read|vault_resolve|vault_list"
                           r"|vault_search|vault_get_metadata|vault_check_quotes)")


def _frontmatter_tools(text: str):
    """The frontmatter `tools` of an agent file as a list of names (comma, bracket or YAML-list forms); None when
    absent or unparseable."""
    lines = text.splitlines()
    if not lines or lines[0].strip() != "---":
        return None
    try:
        end = next(i for i in range(1, len(lines)) if lines[i].strip() == "---")
    except StopIteration:
        return None
    fm = lines[1:end]
    for i, ln in enumerate(fm):
        m = re.match(r"tools\s*:(.*)$", ln)
        if not m:
            continue
        rest = m.group(1).strip()
        if rest:
            if rest.startswith("["):
                if not rest.endswith("]"):
                    return None
                rest = rest[1:-1]
            names = [t.strip().strip("'\"") for t in rest.split(",")]
        else:
            names = []
            for nxt in fm[i + 1:]:
                lm = re.match(r"\s+-\s+(.+?)\s*$", nxt)
                if lm:
                    names.append(lm.group(1).strip().strip("'\""))
                elif nxt.strip() and not nxt.strip().startswith("#"):
                    break
        return names if names and all(re.fullmatch(r"[A-Za-z0-9_.\-]+", n) for n in names) else None
    return None


def _read_only_agent(stype: str, data: dict | None) -> bool:
    """Strict allowlist: a subagent type is read-only only when its project file .claude/agents/<type>.md exists and
    EVERY tool of its frontmatter `tools` is a read-only tool (READ_ONLY_TOOLS, or a vault-memory read MCP tool).
    Any parse failure, unknown tool, missing file (built-in `Explore` included) -> False (the old floor applies)."""
    try:
        import route_state  # noqa: E402
        if not re.fullmatch(r"[a-z0-9_.-]+", stype or ""):
            return False
        d = route_state.project_dir(data) / ".claude" / "agents"
        f = d / (stype + ".md")
        if not f.is_file():
            files = [x for x in d.glob("*.md") if x.stem.lower() == stype] if d.is_dir() else []
            if not files:
                return False
            f = files[0]
        tools = _frontmatter_tools(f.read_text(encoding="utf-8", errors="replace"))
        return bool(tools) and all(t in READ_ONLY_TOOLS or READ_ONLY_MCP.fullmatch(t) for t in tools)
    except Exception:
        return False


def _risk_label(d) -> str:
    risk = ""
    for rule in getattr(d, "applied_rules", ()):  # risk_detected:high(..), risk_floor[_deferred..]:critical->opus
        m = re.match(r"risk_(?:detected|floor\w*):(high|critical)\b", str(rule))
        if m and (m.group(1) == "critical" or not risk):
            risk = m.group(1)
    return risk


def _deferred(d) -> bool:
    """The router moved the risk floor to the mandatory Opus verifier (policy rules.implementer_risk_floor)."""
    return any(str(r).startswith("risk_floor_deferred") for r in getattr(d, "applied_rules", ()))


def _review_brief(d) -> bool:
    """A review/verify brief, including one the router re-labelled implement because of a worker subagent_type."""
    return getattr(d, "task_class", None) == "review" or any(
        str(r).startswith("role:implementer(review->") for r in getattr(d, "applied_rules", ()))


def needs_opus(d, reviewer: bool = False) -> bool:
    """Opus is mandatory for review/verify (by subagent type OR brief) and for risk the policy does not defer.

    Owner rule 2026-10-16 (rules.implementer_risk_floor): a risk-high IMPLEMENTER stays on its class model
    because the Opus reviewer pass is mandatory; critical risk and live operations are never deferred by the
    router, so they still need opus here."""
    if reviewer or _review_brief(d):
        return True
    return bool(_risk_label(d)) and not _deferred(d)


def required_tier(policy, d, reviewer: bool = False):
    """Minimum tier this spawn may run on: opus (needs_opus), the implementer class model for deferred risky
    implementation work, or None (normal work: capped at the session tier, never denied)."""
    if needs_opus(d, reviewer):
        return "opus"
    if _risk_label(d) and _deferred(d):
        return getattr(d, "model", None) if getattr(d, "model", None) in policy.models else "sonnet"
    return None


def _tier(policy, value):
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


def apply_session_policy(policy, d, reviewer: bool, target, tool_input: dict, session, authorized: bool,
                         agent_default=None, stype: str = ""):
    """Owner escalation policy (R1-R5). Returns (target, deny_reason_or_None, note_or_None). Downward routing
    (session above the target) is unchanged; a model choice in tool_input is never an authorization."""
    s_tier = _tier(policy, session)
    note = None
    if s_tier is None:
        s_tier = min(policy.models, key=policy.rank)  # R5: unknown session -> cheapest tier (fail closed)
        note = "session model unknown; set ANTHROPIC_MODEL"
    s_name = session if session and _tier(policy, session) else "unknown"
    if stype == FABLE_CONSULTANT and target == "fable":
        if s_tier in ("opus", "fable"):
            return "fable", None, note
        return None, f"cost-router: {FABLE_CONSULTANT} requires an opus session; session is {s_name}", note
    raw = tool_input.get("model")
    unknown_explicit = raw is not None and _resolve_model(policy, raw) is None
    req = required_tier(policy, d, reviewer)
    if req and policy.rank(s_tier) < policy.rank(req):
        label = f"{d.task_class}/{_risk_label(d) or 'review'}"
        if authorized:
            up = target if target and policy.rank(target) >= policy.rank(req) else req
            return up, None, f"escalated {s_name} -> {up} (authorized by #escalate)" + (f"; {note}" if note else "")
        return None, (f"cost-router: {label} requires {req}; session is {s_name}; the owner can authorize with"
                      f" #escalate in the prompt or run the session on {req}"), note
    if req == "opus":  # session already >= opus: the mandatory floor stays
        return target, None, note
    if unknown_explicit:  # R2-2: never passed through; #escalate cannot authorize a model the policy cannot rank
        out = target if (target and policy.rank(target) <= policy.rank(s_tier)) else s_tier
        cap = f"unrecognised model {_model_label(raw)}; capped to {out}"
        return out, None, cap + (f"; {note}" if note else "")
    if target and policy.rank(target) > policy.rank(s_tier):
        if authorized:
            return target, None, f"escalated {s_name} -> {target} (authorized by #escalate)" + (f"; {note}" if note else "")
        cap = f"not escalated: session {s_name}; #escalate allows {target}"
        return s_tier, None, cap + (f"; {note}" if note else "")
    if not target and raw is None:
        if agent_default is UNKNOWN_DEFAULT:  # R2-1: plugin / unresolvable agent default -> the session tier
            cap = f"agent default unknown; capped to session tier {s_tier}"
            return s_tier, None, cap + (f"; {note}" if note else "")
        if agent_default is not None:
            dt = _resolve_model(policy, agent_default)  # unclassified brief: the agent default must not be dearer
            if dt is None:  # F3: an unrankable default is capped even with #escalate (like an explicit model)
                cap = f"agent default {_model_label(agent_default)} unrecognised; capped to session tier {s_tier}"
                return s_tier, None, cap + (f"; {note}" if note else "")
            if dt == "fable" and stype != FABLE_CONSULTANT:
                if not authorized and policy.rank("opus") > policy.rank(s_tier):
                    cap = f"not escalated: agent default {dt} above session {s_name}; #escalate allows opus"
                    return s_tier, None, cap + (f"; {note}" if note else "")
                return "opus", None, note
            if not authorized and policy.rank(dt) > policy.rank(s_tier):
                cap = f"not escalated: agent default {dt} above session {s_name}; #escalate allows {dt}"
                return s_tier, None, cap + (f"; {note}" if note else "")
        elif s_tier == "fable" and stype != FABLE_CONSULTANT:
            return "opus", None, note
    return target, None, note


UNKNOWN_DEFAULT = object()  # the agent default model cannot be resolved: capped to the session tier
BUILTIN_AGENTS = frozenset({"general-purpose", "explore", "plan", "statusline-setup", "output-style-setup",
                            "claude-code-guide"})  # inherit, or cheaper than any session (Explore: haiku)


def _agent_file(d: Path, stype: str):
    """<d>/<stype>.md matched by a directory listing (never a path built from the type name), or None."""
    if not d.is_dir():
        return None
    files = [x for x in d.glob("*.md") if x.stem.lower() == stype and x.is_file()]
    return files[0] if files else None


def _agent_default_model(stype: str, data: dict | None):
    """Model a spawn without tool_input.model would run on: CLAUDE_CODE_SUBAGENT_MODEL, else the `model:` of the
    project agent file, else of the user agent file (~/.claude/agents); None (empty type, inherit, no `model:`,
    built-in) means the session model, which needs no cap. UNKNOWN_DEFAULT: a plugin type (`a:b`), an invalid type
    name, a non-built-in type with no agent file, or an unreadable file."""
    env = os.environ.get("CLAUDE_CODE_SUBAGENT_MODEL", "").strip()
    if env and env.lower() != "inherit":
        return env
    if not stype:
        return None  # no subagent_type: general-purpose
    if not re.fullmatch(r"[a-z0-9_-][a-z0-9_.-]*", stype):
        return UNKNOWN_DEFAULT  # plugin `a:b` names and anything path-like are never file paths
    try:
        import route_state  # noqa: E402
        f = (_agent_file(route_state.project_dir(data) / ".claude" / "agents", stype)
             or _agent_file(Path.home() / ".claude" / "agents", stype))
        if f is None:
            return None if stype in BUILTIN_AGENTS else UNKNOWN_DEFAULT
        lines = f.read_text(encoding="utf-8-sig", errors="replace").splitlines()  # F2: BOM tolerated
        if not lines or lines[0].strip() != "---":
            return None  # no frontmatter: no model key, the session model applies
        end = next((i for i in range(1, len(lines)) if lines[i].strip() == "---"), None)  # F1: whole-line delimiter
        if end is None:
            return UNKNOWN_DEFAULT  # unclosed frontmatter: fail closed
        fm = chr(10).join(lines[1:end])
        m = re.search(r"^model[ \t]*:[ \t]*(.*?)[ \t]*$", fm, re.MULTILINE)
        val = m.group(1).split(" #")[0].strip().strip("'\"").strip() if m else ""
        return None if not val or val.lower() == "inherit" else val
    except Exception:
        return UNKNOWN_DEFAULT


def choose(policy, mod, tool_input: dict, data: dict | None = None):
    """Return (target_alias_or_None, route_decision)."""
    brief = f"{tool_input.get('description') or ''} {str(tool_input.get('prompt') or '')[:2000]}"
    stype = str(tool_input.get("subagent_type") or "").strip().lower()
    reviewer = any(m in stype for m in REVIEWER_MARKS)
    role = "reviewer" if reviewer else ("implementer" if any(m in stype for m in WORKER_MARKS) else None)
    try:
        d = mod.route(policy, brief, for_subagent=True, role=role)
    except TypeError:  # a router copy older than the role parameter
        d = mod.route(policy, brief, for_subagent=True)
    risky = any(r.startswith(RISK_PREFIXES) for r in d.applied_rules)
    requested_model = tool_input.get("model")
    explicit = _alias(policy, requested_model)
    if stype == FABLE_CONSULTANT:
        allowlist = {str(value).strip().lower() for value in policy.rules.get("fable_subagent_allowlist", ())}
        if stype not in allowlist:
            return "opus", d
        if str(requested_model or "").strip().lower() == "inherit":
            return "opus", d
        if explicit == "unknown":
            return ("fable" if _requests_fable(requested_model) else "opus"), d
        return (explicit or "fable"), d
    if (_requests_fable(requested_model) or str(requested_model or "").strip().lower() == "inherit"
            or _requests_fable(os.environ.get("CLAUDE_CODE_SUBAGENT_MODEL"))):
        return "opus", d
    if explicit == "unknown":
        if not needs_opus(d, reviewer) and d.task_class == "unclassified" and not risky:
            return None, d  # apply_session_policy caps it to the session tier (R2-2)
        explicit = None  # never kept: risky/review -> the floor, a classified brief -> the route model
    if (role is None and not risky and d.task_class not in ("unclassified", "review", "design", "frontier")
            and os.environ.get("COST_ROUTER_READONLY_FLOOR", "").strip() != "1"
            and "explore" in policy.task_classes and _read_only_agent(stype, data)):
        # a strictly read-only agent (allowlisted tools, see _read_only_agent) cannot do the risky thing, but a
        # review/verify/design brief or risk >= high keeps the old floor whatever the tools (env restores the floor)
        import dataclasses
        lo = policy.task_classes["explore"].model
        hi = policy.higher_tier(lo) or lo
        target = explicit or lo
        if policy.rank(target) < policy.rank(lo):
            target = lo
        if policy.rank(target) > policy.rank(hi):
            target = hi
        return target, dataclasses.replace(d, task_class="explore", applied_rules=())
    if d.task_class == "unclassified" and not risky:
        if reviewer and (explicit is None or policy.rank(explicit) < policy.rank("opus") or explicit == "fable"):
            return "opus", d
        if explicit is None:
            frontmatter_model = _agent_frontmatter_model(stype)
            if (_requests_fable(frontmatter_model)
                    or str(frontmatter_model or "").strip().lower() == "inherit"
                    or _requests_fable(os.environ.get("CLAUDE_CODE_SUBAGENT_MODEL"))):
                return "opus", d
            return None, d
        # HIGH-A: an explicit model is never passed through unchecked; apply_session_policy caps it (R3/R5)
        return (explicit if explicit in policy.models else None), d
    lo = d.model
    hi = policy.higher_tier(lo) or lo
    if hi == "fable":
        hi = "opus"
    target = explicit or lo
    if policy.rank(target) < policy.rank(lo):
        target = lo
    if policy.rank(target) > policy.rank(hi):
        target = hi
    if target == "fable":
        target = "opus"
    if needs_opus(d, reviewer) and policy.rank(target) < policy.rank("opus"):
        target = "opus"  # verification (by type OR by brief) and high/critical risk: a cheap model must not do it
    return target, d


FABLE_EXECUTOR_MAX_CAP = 10  # a policy max_per_session above this is malformed (disabled), not a bigger budget
LOCAL_ONLY_IDS = frozenset({"dfir-analyst"})  # privacy specialists, also when the registry says nothing
ATTEMPT_SOURCES = ("payload", "transcript", "stored", "env")  # a settings-file model is a guess, never an Opus session


def _installed_skill_dir() -> Path:
    return Path.home() / ".claude" / "skills" / "cost-router"


def _installed_policy_raw():
    """(policy dict, None) from the INSTALLED policy.json, only when its bytes match the sha256 install.py recorded in
    pins.sha256.json; else (None, why). Never the project copy, an env var or prompt text (threat E2)."""
    d = _installed_skill_dir()
    try:
        data = (d / "policy.json").read_bytes()
    except OSError:
        return None, "the installed policy.json is missing"
    try:
        record = json.loads((d / "pins.sha256.json").read_text(encoding="utf-8"))
    except Exception:
        return None, "pins.sha256.json is missing or unreadable"
    want = record.get("policy.json") if isinstance(record, dict) else None
    if not isinstance(want, str) or want != hashlib.sha256(data).hexdigest():
        return None, ("the installed policy.json is not the one recorded at install (pins.sha256.json); re-run "
                      "install.py with a release pin that includes it")
    try:
        raw = json.loads(data.decode("utf-8"))
    except Exception:
        return None, "the installed policy.json is not valid JSON"
    return (raw, None) if isinstance(raw, dict) else (None, "the installed policy.json is not an object")


def _executor_budget(raw: dict):
    """(max_per_session, None) when rules.fable_executor is exactly {"enabled": true, "max_per_session": 1..10},
    else (None, why). Anything else, including the shipped enabled=false, means disabled."""
    rules = raw.get("rules")
    block = rules.get("fable_executor") if isinstance(rules, dict) else None
    if not isinstance(block, dict) or set(block) != {"enabled", "max_per_session"}:
        return None, "rules.fable_executor is missing or malformed in the installed policy: disabled"
    if block["enabled"] is not True:
        return None, "fable_executor is disabled in the installed policy (rules.fable_executor.enabled is not true)"
    n = block["max_per_session"]
    if type(n) is not int or not 1 <= n <= FABLE_EXECUTOR_MAX_CAP:
        return None, f"rules.fable_executor.max_per_session must be an int 1..{FABLE_EXECUTOR_MAX_CAP}: disabled"
    return n, None


def _failed_ladder(sid: str, epoch: str):
    """The label of a check that, in this session and prompt epoch, has a FAILED receipt from a sonnet-tier attempt
    and one from an opus-tier attempt, and whose newest receipt is still failing; else None."""
    import check  # noqa: E402  (the hook's own lib: receipts live under ~/.claude/cache, never in the project)
    groups: dict = {}
    for r in check.all_receipts(session=sid):  # sorted oldest first
        if r.get("prompt_epoch") == epoch and r.get("session") == sid and type(r.get("exit")) is int:
            groups.setdefault(r["label"], []).append(r)
    for label, rs in sorted(groups.items()):
        if rs[-1]["exit"] == 0:
            continue  # the check passes now: nothing is left to execute
        tiers = {r.get("tier") for r in rs if r["exit"] != 0}
        if {"sonnet", "opus"} <= tiers:
            return label
    return None


_WB = r"(?<![^\W_])"  # word start: start of text or a non-alphanumeric char before
_WE = r"(?![^\W_])"  # word end
# Live operations the executor never touches, on top of the policy's never_for_stems (matched on the FOLDED brief).
# Inflection-aware so `application`, `dropdown`, `relationship`, `shipment` stay benign.
EXECUTOR_LIVE_PATTERNS = (
    ("push", _WB + r"push(?:es|ed|ing)?" + _WE),
    ("apply", _WB + r"appl(?:y|ies|ied|ying)" + _WE),
    ("drop", _WB + r"drop(?:s|ped|ping)?" + _WE),
    ("upload", _WB + r"upload"),
    ("ship", _WB + r"ship(?:s|ped|ping)?" + _WE),
    ("release", _WB + r"releas"),
    ("rollout", _WB + r"roll(?:s|ed|ing)?[\s-]?outs?" + _WE),
    ("migrate", _WB + r"migrat"),
    ("truncate", _WB + r"truncat"),
    ("rm -rf", _WB + r"rm\s+-[a-z]*(?:rf|fr)"),
    ("force-push", _WB + r"force[\s-]?push"),
)
# Stems that are live operations wherever they occur, compounds included (autodeploy, redeploy, republish, auto-rotate).
EXECUTOR_SUBSTRING_STEMS = ("deploy", "rotate", "publish")


def _live_operation_hit(brief: str, stems, specialists):
    """The first live-operation stem/pattern in `brief`, else None. The brief is normalized first (specialists:
    NFKC, Cf/zero-width removed, cedilla -> comma-below, casefold, whitespace collapsed). A policy stem with diacritics
    (`șterg`, `producț`) matches the normalized text with its diacritics; an ASCII stem matches the diacritic-folded
    text too (`şterge` -> `sterge`), so folding never turns `producț` into a bare `product`."""
    conf = getattr(specialists, "CONFUSABLES", None)  # M12: look-alike letters folded to ASCII first
    src = unicodedata.normalize("NFKC", str(brief)).translate(conf) if conf else brief
    kept = " " + specialists.normalize(src) + " "
    folded = " " + specialists.fold(src) + " "
    for k in stems:
        kn, kf = specialists.normalize(k), specialists.fold(k)
        if not kn:
            continue
        if re.search(_WB + re.escape(kn), kept) or (kf == kn and re.search(_WB + re.escape(kf), folded)):
            return k
    for name, pat in EXECUTOR_LIVE_PATTERNS:
        if re.search(pat, folded):
            return name
    return next((k for k in EXECUTOR_SUBSTRING_STEMS if k in folded), None)


def _fable_executor_gate(data: dict, tool_input: dict, fields, path, pinned, off: bool) -> int:
    """M11 gate for a `fable-executor` spawn (default off). Every condition must hold, else exit 2 naming it:
    (a) rules.fable_executor.enabled in the INSTALLED, hash-verified policy; (b) the session runs Opus; (c) the
    definition is the pinned one and passes its own exact validator, no project file claims the name; (d) this session
    and prompt epoch hold a failed check.py receipt from a sonnet-tier AND from an opus-tier attempt of the same check,
    AND this hook itself recorded a real (allowed, writing) sonnet-tier and opus-tier spawn in that epoch;
    (f) no critical risk, no live-operation stem (rules.implementer_risk_floor.never_for_stems); (g) no local_only
    specialist on this prompt or in the brief; (e) a slot of max_per_session, taken under a cross-process lock.
    (h) COST_ROUTER_OFF bypasses none of it. (i) heavy task only: the CURRENT owner prompt's route state (written by
    hook_prompt_route from the owner's own text) is implement/design/frontier, not a short follow-up/slash/#direct. Receipts and route state can be written by the main session: this limits
    induced overspend, it does not stop a hostile Opus session (threat E1)."""
    def deny(cond: str, why: str) -> int:
        print(f"cost-router: fable-executor blocked: condition ({cond}) {why}; blocking spawn", file=sys.stderr)
        return 2

    raw, why = _installed_policy_raw()
    if raw is None:
        return deny("a", f"policy flag: {why}; the executor stays disabled")
    max_n, why = _executor_budget(raw)
    if max_n is None:
        return deny("a", f"policy flag: {why}")
    try:
        from route import _load_router  # noqa: E402
        mod = _load_router()
        policy = mod.ClaudeModelPolicy(raw)
        import route_state  # noqa: E402
        import specialists  # noqa: E402
    except Exception as exc:
        return deny("a", f"the installed policy cannot be loaded ({type(exc).__name__})")
    sid = data.get("session_id")
    session, source = route_state.resolve_session_model_ex(sid, data, data.get("transcript_path"))
    tier = _tier(policy, session) if source in ATTEMPT_SOURCES else None
    if tier != "opus" or _requests_fable(session):
        return deny("b", f"session model: the executor is spawned only from an Opus session; session is "
                         f"{_model_label(session) if session and source in ATTEMPT_SOURCES else 'unknown'}")
    if not isinstance(pinned, dict) or not isinstance(pinned.get(FABLE_EXECUTOR), str):
        return deny("c", "definition: agents.pinned.json has no pinned fable-executor definition (re-run install.py)")
    if path is None or not _valid_fable_executor_frontmatter(fields):
        return deny("c", "definition: fable-executor is missing or violates its exact agent contract")
    try:
        actual = _lf_sha256(Path(path).read_bytes())
    except OSError:
        return deny("c", "definition: the fable-executor definition is unreadable")
    if actual != pinned[FABLE_EXECUTOR]:
        return deny("c", f"definition: {Path(path).name} does not match the pinned sha256 recorded at install")
    problem = _project_override_problem(pinned, {FABLE_EXECUTOR, FABLE_CONSULTANT})
    if problem:
        return deny("c", f"definition: {problem}")
    model = tool_input.get("model")
    if model is not None and str(model).strip().lower() not in ("fable", "claude-fable-5-2"):
        return deny("c", f"definition: the executor runs only on its pinned Fable model, not {_model_label(model)}")
    state = route_state.load(sid)
    epoch = state.get("epoch") if isinstance(state, dict) else None
    if not isinstance(epoch, str) or not epoch:
        return deny("d", "failed checks: no route state for the current prompt (the prompt hook did not run)")
    task_class = str(state.get("task_class") or "")
    if task_class not in HEAVY_CLASSES or state.get("followup"):
        why = ("the current owner prompt is a short follow-up or slash command" if state.get("followup") else
               f"the current owner prompt is routed {task_class or 'unclassified'!r}")
        return deny("i", f"heavy task only: {why}; the executor runs only for an implement/design/frontier prompt")
    sid_s = str(sid or "")
    if not re.fullmatch(r"[A-Za-z0-9_-]{1,64}", sid_s):
        return deny("d", "failed checks: the session id cannot key check.py receipts")
    try:
        label = _failed_ladder(sid_s, epoch)
    except Exception:
        label = None
    if label is None:
        return deny("d", "failed checks: needs a FAILED check.py receipt of the same check from a sonnet-tier attempt "
                         "AND from an opus-tier attempt in this prompt (check.py run --session <id> --tier sonnet|opus)")
    try:
        ladder = _real_ladder(sid_s, epoch)
    except Exception:
        ladder = False
    if not ladder:
        return deny("d", "failed attempts: this hook recorded no real sonnet-tier AND opus-tier implementation spawn "
                         "in this prompt (receipts alone do not count; read-only/reviewer agents are not attempts)")
    brief = f"{tool_input.get('description') or ''} {tool_input.get('prompt') or ''}"
    try:
        try:
            d = mod.route(policy, brief[:20000], for_subagent=True, role="implementer")
        except TypeError:
            d = mod.route(policy, brief[:20000], for_subagent=True)
        risk = _risk_label(d)
    except Exception:
        return deny("f", "risk: the brief cannot be routed")
    if risk == "critical" or state.get("risk") == "critical":
        return deny("f", "risk: critical risk stays on Opus")
    rule = policy.rules.get("implementer_risk_floor")
    stems = [str(k).lower() for k in (rule.get("never_for_stems") or ()) if k] if isinstance(rule, dict) else []
    if not stems:
        return deny("f", "risk: the installed policy has no live-operation stems")
    hit = _live_operation_hit(brief, stems, specialists)
    if hit:
        return deny("f", f"risk: live operation ({hit!r}) stays on Opus")
    spec = state.get("spec")
    if not isinstance(spec, list):
        return deny("g", "privacy: the prompt has no specialist record, so local_only work cannot be excluded")
    reg = specialists.load(_installed_skill_dir() / "registry.json")
    if not reg:
        return deny("g", "privacy: the installed registry.json is missing or invalid, so local_only work cannot be "
                         "excluded")
    local = {i for i, e in reg.items() if e.get("privacy") == "local_only"} | LOCAL_ONLY_IDS
    bad = [str(x) for x in spec if str(x) in local] + specialists.local_only_hits(brief, reg)
    if bad:
        return deny("g", f"privacy: local_only specialist {bad[0]!r} on this prompt")
    ok, count = route_state.reserve_fable_executor(sid_s, max_n)
    if not ok:
        return deny("e", f"budget: {count}/{max_n} fable-executor spawns already used this session" if count >= 0
                    else "budget: the per-session counter is unavailable (lock or write failed)")
    reason = None if off else gate_and_mark(data)
    if reason:
        print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": "deny",
                                                 "permissionDecisionReason": reason}}))
        return 0
    ctx = (f"cost-router: fable-executor allowed ({count}/{max_n} this session; check {label[:40]!r} failed on sonnet "
           "and opus). An Opus vault-reviewer pass on the executor's diff is mandatory before DONE: spawn "
           "vault-reviewer with the diff and the same acceptance check.")
    print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse", "additionalContext": ctx}}))
    return 0


def _record_real_spawn(data: dict, stype: str, policy, target, tool_input: dict, session) -> None:
    """E1 hardening: record a spawn this hook ALLOWED with the tier it will actually run on (the rewrite target, else
    the given model, else the agent default, else the session model) for the current prompt epoch. Read-only and
    reviewer agents are recorded with writer=False: they are not implementation attempts. Never raises."""
    try:
        if policy is None:
            return
        import route_state  # noqa: E402
        sid = data.get("session_id")
        state = route_state.load(sid)
        epoch = state.get("epoch") if isinstance(state, dict) else None
        if not isinstance(epoch, str) or not epoch:
            return
        default = _agent_default_model(stype, data)
        eff = next((m for m in (target, tool_input.get("model"), None if default is UNKNOWN_DEFAULT else default)
                    if m and not _is_inherit(m)), None) or session
        tier = _tier(policy, eff)
        if not tier:
            return
        writer = not (_read_only_agent(stype, data) or any(m in stype for m in REVIEWER_MARKS))
        route_state.record_spawn(sid, epoch, tier, stype, writer)
    except Exception:
        pass


def _real_ladder(sid: str, epoch: str) -> bool:
    """A real sonnet-tier AND a real opus-tier implementation spawn were allowed by this hook in this prompt epoch."""
    import route_state  # noqa: E402
    tiers = {e.get("tier") for e in route_state.spawns(sid) if e.get("epoch") == epoch and e.get("writer") is True}
    return {"sonnet", "opus"} <= tiers


def _verbose() -> bool:
    return os.environ.get("COST_ROUTER_VERBOSE_ROUTE", "").strip() == "1"


def _main() -> int:
    off = False
    try:  # COST_ROUTER_OFF=1: equal-arm benchmarks / owner kill switch: efficiency behaviour only, see below
        import route_state as _rs  # noqa: E402
        off = _rs.disabled_by_env()
    except Exception:
        pass
    try:
        data = json.load(sys.stdin)
    except Exception:
        if off:
            return 0
        print("cost-router: invalid Agent hook input; blocking spawn", file=sys.stderr)
        return 2
    if not isinstance(data, dict):
        if off:
            return 0
        print("cost-router: Agent hook input must be a JSON object; blocking spawn", file=sys.stderr)
        return 2
    if data.get("tool_name") not in SPAWN_TOOLS:
        return 0
    tool_input = data.get("tool_input")
    if not isinstance(tool_input, dict):
        if off:
            return 0
        print("cost-router: malformed Agent tool input; blocking spawn", file=sys.stderr)
        return 2
    stype = str(tool_input.get("subagent_type") or "").strip().lower()
    if stype not in (FABLE_CONSULTANT, FABLE_EXECUTOR) and _requests_fable(os.environ.get("CLAUDE_CODE_SUBAGENT_MODEL")):
        # CLAUDE_CODE_SUBAGENT_MODEL may override ANY model key (tool_input.model, frontmatter, our own rewrite), so
        # rewriting `model` cannot keep a non-consultant spawn off Fable: deny it, also under COST_ROUTER_OFF
        print("cost-router: CLAUDE_CODE_SUBAGENT_MODEL resolves to a Fable model and overrides every model setting; "
              "only the pinned fable-consultant and the gated fable-executor may run on Fable. Unset CLAUDE_CODE_SUBAGENT_MODEL (or set it to a "
              "non-Fable model); blocking spawn", file=sys.stderr)
        return 2
    agent_frontmatter = (
        _agent_frontmatter(stype)
        if stype in (FABLE_CONSULTANT, FABLE_EXECUTOR) or stype in PRIVILEGED_AGENTS or not tool_input.get("model")
        else None
    )
    pinned_agents = _pinned_agent_hashes()
    agent_path = _agent_resolve(stype)[1] if (pinned_agents is not None and agent_frontmatter) else None
    frontmatter_model = agent_frontmatter.get("model") if agent_frontmatter else None
    fable_sensitive = (
        stype in (FABLE_CONSULTANT, FABLE_EXECUTOR)  # the executor gate below also runs under COST_ROUTER_OFF
        or _requests_fable(tool_input.get("model"))
        or _requests_fable(frontmatter_model)
        or _requests_fable(os.environ.get("CLAUDE_CODE_SUBAGENT_MODEL"))  # the env override beats every model key
        or ((_is_inherit(tool_input.get("model")) or _is_inherit(frontmatter_model)) and _session_is_fable(data))
        # no model in tool_input and none in the agent frontmatter (general-purpose, built-ins, project agents without
        # `model:`): the subagent inherits the session model, exactly like `inherit`
        or (not tool_input.get("model") and not frontmatter_model and _session_is_fable(data))
    )
    if off and not fable_sensitive:
        return 0  # COST_ROUTER_OFF: no routing, no state, no output; only the Fable guard below still runs
    definition_note = None
    if pinned_agents is None:
        if stype in (FABLE_CONSULTANT, FABLE_EXECUTOR):
            print("cost-router: agents.pinned.json is missing (the pinned sha256 of the agent definitions): cannot "
                  f"validate the {stype} definition; blocking spawn (run install.py from the trusted checkout)",
                  file=sys.stderr)
            return 2
        mismatch = ("agents.pinned.json is missing, so the definition of agent "
                    f"{stype!r} cannot be checked") if stype in PRIVILEGED_AGENTS else None
    else:
        mismatch = _definition_mismatch(stype, agent_frontmatter, agent_path, pinned_agents)
        if not mismatch and (stype in PRIVILEGED_AGENTS or fable_sensitive):
            mismatch = _project_override_problem(
                pinned_agents, {stype} | ({FABLE_CONSULTANT} if fable_sensitive else set()))
    if mismatch:
        if fable_sensitive:
            contract = ("; the definition also violates the read-only agent contract"
                        if stype == FABLE_CONSULTANT and not _valid_fable_consultant_frontmatter(agent_frontmatter) else "")
            print(f"cost-router: {mismatch}{contract}; blocking a Fable-sensitive spawn (reinstall cost-router from the "
                  "trusted checkout, or remove the project override)", file=sys.stderr)
            return 2
        definition_note = (f"cost-router: {mismatch}; its body is not trusted: the model stays on the route/Opus floor"
                           " and the installed definition is the reference")
    if stype == FABLE_CONSULTANT and not _valid_fable_consultant_frontmatter(agent_frontmatter):
        print("cost-router: fable-consultant definition is missing or violates the read-only agent contract; blocking spawn",
              file=sys.stderr)
        return 2
    try:
        from route import _load_router, is_shadowed, policy_path  # noqa: E402
        if is_shadowed(Path(__file__)) and _user_agent_hook_is_registered():
            verdict, why = _shadow_verdict(Path(__file__))
            if verdict == "defer":
                return 0
            print(f"cost-router: registered user-scope Agent hook is stale ({why}); run "
                  "`python .claude/skills/cost-router/install.py` from the updated checkout before spawning agents",
                  file=sys.stderr)
            return 2
    except Exception:
        if fable_sensitive:
            print("cost-router: cannot validate Fable routing; blocking spawn", file=sys.stderr)
            return 2
        return 0
    if stype == FABLE_EXECUTOR:
        return _fable_executor_gate(data, tool_input, agent_frontmatter, agent_path, pinned_agents, off)
    try:
        mod = _load_router()
        policy = mod.ClaudeModelPolicy.from_file(policy_path())
        target, d = choose(policy, mod, tool_input, data)
        import route_state  # noqa: E402
        sid = data.get("session_id")
        state = route_state.load(sid) or {}
        session, source = route_state.resolve_session_model_ex(sid, data, data.get("transcript_path"))
        if source not in ("payload", "transcript", "stored", "env"):
            session = None  # MED-C: a settings-file model is a guess -> unknown session, fail closed (R5)
        reviewer = any(m in stype for m in REVIEWER_MARKS)
        target, deny, pnote = apply_session_policy(policy, d, reviewer, target, tool_input, session,
                                                   state.get("escalation_authorized") is True,
                                                   agent_default=_agent_default_model(stype, data),
                                                   stype=stype)
        given = tool_input.get("model")
        if target and given is not None and _resolve_model(policy, given) == target:
            target = given  # same tier: keep the owner's spelling (`opus[1m]`, a dated id), no rewrite
    except Exception:
        if fable_sensitive:
            print("cost-router: Fable routing policy unavailable; blocking spawn", file=sys.stderr)
            return 2
        target, d, deny, pnote = None, None, None, None  # fail open: only the Vault-router gate still applies
        policy, session = None, None
    if deny:
        print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": "deny",
                                                 "permissionDecisionReason": deny}}))
        return 0
    if pnote and pnote.startswith("escalated "):
        try:  # informational only: a receipt is forgeable by the main session and never grants anything
            import check  # noqa: E402
            failed = check.last_failing(data.get("session_id"))
            if failed:
                pnote += f"; after failed check {failed['label'][:40]} exit={failed['exit']}"
        except Exception:
            pass
    if definition_note:
        pnote = "; ".join(x for x in (pnote, definition_note) if x)
    reason = None if off else gate_and_mark(data)  # OFF: no Vault-router gate, no delegation marker
    if reason:
        print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": "deny",
                                                 "permissionDecisionReason": reason}}))
        return 0
    _record_real_spawn(data, stype, policy, target, tool_input, session)
    if d is None:
        return 0
    if target == "fable" and stype == FABLE_CONSULTANT and not tool_input.get("model"):
        # The Agent tool schema accepts only the short "fable" alias. Keep the
        # exact claude-fable-5-2 pin in the consultant's frontmatter instead of
        # injecting an invalid full model ID into updatedInput.model.
        ctx = "\n".join(x for x in (pnote, _ROUTER_NOTE if _verbose() else "") if x)
        if ctx:
            print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse", "additionalContext": ctx}}))
        return 0
    if not target or target == tool_input.get("model"):
        ctx = "\n".join(x for x in (pnote, _ROUTER_NOTE if _verbose() else "") if x)
        if ctx:
            print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse", "additionalContext": ctx}}))
        return 0
    floored = any(r.startswith("risk_floor:") for r in d.applied_rules)
    deferred = any(r.startswith("risk_floor_deferred") for r in d.applied_rules)
    tag = ", risk floor" if floored else (", risk floor on the Opus reviewer, not this worker" if deferred else "")
    before = tool_input.get("model") or "agent default"
    note = f"cost-router: subagent model {before} -> {target} (class {d.task_class}{tag})"
    ctx = note + "." + ("\n" + _ROUTER_NOTE if _ROUTER_NOTE and _verbose() else "") + ("\n" + pnote if pnote else "")
    updated = dict(tool_input)
    updated["model"] = target
    print(json.dumps({"hookSpecificOutput": {
        "hookEventName": "PreToolUse",
        "permissionDecision": "allow",
        "permissionDecisionReason": note + (f"; {_ROUTER_NOTE}" if _ROUTER_NOTE else "") + (f"; {pnote}" if pnote else ""),
        "updatedInput": updated,
        **({"additionalContext": ctx} if (_verbose() or pnote) else {}),  # the policy note is always visible
    }}))
    return 0


def main() -> int:
    try:
        return _main()
    except Exception as exc:
        print(
            f"cost-router: unexpected error while validating Agent spawn ({type(exc).__name__}); blocking spawn",
            file=sys.stderr,
        )
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
