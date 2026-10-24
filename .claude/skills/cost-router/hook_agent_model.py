#!/usr/bin/env python3
"""PreToolUse hook (matcher "Agent|Task") for the cost-router skill.

Sets the model of every subagent spawn from the policy route of its brief (description + prompt),
whatever the subagent type is:
  - no model given             -> the route's model;
  - a model given              -> kept between the route's model and one tier above it (a one-notch
                                  escalation is allowed; a model cheaper than the route is raised);
  - risk detected in the brief -> never below the risk floor (Opus);
  - Fable is never a subagent model -> Opus;
  - a reviewer/verifier subagent_type ("review"/"verif" in it) -> never below Opus.
A brief that matches no task class and carries no risk is left alone (the subagent's own
frontmatter model applies) unless that default is above the session tier or UNKNOWN (a plugin `a:b` type, a
type with no project/user agent file that is not a built-in, an unreadable file or an unrecognised `model:`), which
is capped to the session tier with a note. An explicit model the policy does not recognise (alias, exact id, alias
with a `[..]` suffix, longest model_id prefix) is never passed through: on a risky/review brief the floor applies,
on a classified brief the route model, on an unclassified brief the session tier (with a note).
Route state (lib/route_state.py): every spawn sets state.delegated=true, which unlocks the main
session for hook_enforce_route.py. Inside the AI Memory Vault (the project has
03_IMPLEMENTATION/packages/routing/route_cli.py) a spawn for an implement/review/design/frontier
route first runs the Vault router in-hook (`routing.route_cli route`, 5 s): ROUTED/PLANNED records the
receipt and allows; BLOCKED or an unavailable router denies (delegated is not set then). A receipt from
Bash (state.vault_routed, set by hook_enforce_route.py) also passes. "#direct" and
COST_ROUTER_ENFORCE=0 skip that gate. Outside the Vault it is never required.
The `vault-router: ...` / `subagent model x -> y` context line is emitted only with COST_ROUTER_VERBOSE_ROUTE=1 (denials always carry their reason). A read-only subagent type (an agent file whose frontmatter `tools` are all in the read-only allowlist: Read, Grep, Glob, LS, WebFetch, WebSearch, the vault-memory read tools; the project's Explore has Bash and is NOT read-only) is clamped in the explore class (haiku, cap one tier above) only for briefs with no risk rule and a class outside review/design/frontier (COST_ROUTER_READONLY_FLOOR=1 restores the old floor).
Any other error: no output, exit 0; apart from that gate this hook only rewrites `model`.
"""
from __future__ import annotations

import json
import os
import re
import subprocess
import sys
import tempfile
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))

RISK_PREFIXES = ("risk_detected", "risk_floor")
SPAWN_TOOLS = ("Agent", "Task")  # "Task" is the legacy name of the spawn tool
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
_ROUTER_NOTE = ""  # one context line set by gate_and_mark when the in-hook Vault router allowed the spawn


def _goal(tool_input: dict) -> str:
    raw = str(tool_input.get("prompt") or tool_input.get("description") or "").strip()
    return " ".join(raw.split())[:300] or "<unnamed subagent task>"


def _run_vault_router(route_state, data: dict, goal: str):
    """Run `routing.route_cli route` in-hook. Returns (decision dict, None) or (None, short cause)."""
    root = route_state.project_dir(data)
    env = dict(os.environ)
    pkgs = "03_IMPLEMENTATION/packages"
    env["PYTHONPATH"] = pkgs + (os.pathsep + env["PYTHONPATH"] if env.get("PYTHONPATH") else "")
    # argv list (never a shell), one `--goal=<goal>` element so a dash-leading goal is not an option; stdout goes
    # to a temp file, not a pipe, so a grandchild holding the handle cannot outlive the timeout.
    cmd = [sys.executable, "-m", "routing.route_cli", "route", f"--goal={goal}"]
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
                         agent_default=None):
    """Owner escalation policy (R1-R5). Returns (target, deny_reason_or_None, note_or_None). Downward routing
    (session above the target) is unchanged; a model choice in tool_input is never an authorization."""
    s_tier = _tier(policy, session)
    note = None
    if s_tier is None:
        s_tier = min(policy.models, key=policy.rank)  # R5: unknown session -> cheapest tier (fail closed)
        note = "session model unknown; set ANTHROPIC_MODEL"
    s_name = session if session and _tier(policy, session) else "unknown"
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
    if target and policy.rank(target) > policy.rank(s_tier):
        if authorized:
            return target, None, f"escalated {s_name} -> {target} (authorized by #escalate)" + (f"; {note}" if note else "")
        cap = f"not escalated: session {s_name}; #escalate allows {target}"
        return s_tier, None, cap + (f"; {note}" if note else "")
    if unknown_explicit:  # R2-2: never passed through; #escalate cannot authorize a model the policy cannot rank
        out = target or s_tier
        cap = f"unrecognised model {_model_label(raw)}; capped to {out}"
        return out, None, cap + (f"; {note}" if note else "")
    if not target and raw is None:
        if agent_default is UNKNOWN_DEFAULT:  # R2-1: plugin / unresolvable agent default -> the session tier
            cap = f"agent default unknown; capped to session tier {s_tier}"
            return s_tier, None, cap + (f"; {note}" if note else "")
        if agent_default is not None:
            dt = _resolve_model(policy, agent_default)  # unclassified brief: the agent default must not be dearer
            if dt is None:  # F3: an unrankable default is capped even with #escalate (like an explicit model)
                cap = f"agent default {_model_label(agent_default)} unrecognised; capped to session tier {s_tier}"
                return s_tier, None, cap + (f"; {note}" if note else "")
            if not authorized and policy.rank(dt) > policy.rank(s_tier):
                cap = f"not escalated: agent default {dt} above session {s_name}; #escalate allows {dt}"
                return s_tier, None, cap + (f"; {note}" if note else "")
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
    stype = str(tool_input.get("subagent_type") or "").lower()
    reviewer = any(m in stype for m in REVIEWER_MARKS)
    role = "reviewer" if reviewer else ("implementer" if any(m in stype for m in WORKER_MARKS) else None)
    try:
        d = mod.route(policy, brief, for_subagent=True, role=role)
    except TypeError:  # a router copy older than the role parameter
        d = mod.route(policy, brief, for_subagent=True)
    risky = any(r.startswith(RISK_PREFIXES) for r in d.applied_rules)
    explicit = _alias(policy, tool_input.get("model"))
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


def _verbose() -> bool:
    return os.environ.get("COST_ROUTER_VERBOSE_ROUTE", "").strip() == "1"


def main() -> int:
    try:  # COST_ROUTER_OFF=1: equal-arm benchmarks / owner kill switch; no output, no state
        import route_state as _rs  # noqa: E402
        if _rs.disabled_by_env():
            return 0
    except Exception:
        pass
    try:
        data = json.load(sys.stdin)
    except Exception:
        return 0
    if not isinstance(data, dict) or data.get("tool_name") not in SPAWN_TOOLS:
        return 0
    tool_input = data.get("tool_input")
    if not isinstance(tool_input, dict):
        return 0
    try:
        from route import _load_router, is_shadowed, policy_path  # noqa: E402
        if is_shadowed(Path(__file__)):
            return 0
    except Exception:
        return 0
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
        stype_l = str(tool_input.get("subagent_type") or "").lower()
        reviewer = any(m in stype_l for m in REVIEWER_MARKS)
        target, deny, pnote = apply_session_policy(policy, d, reviewer, target, tool_input, session,
                                                   state.get("escalation_authorized") is True,
                                                   agent_default=_agent_default_model(stype_l, data))
        given = tool_input.get("model")
        if target and given is not None and _resolve_model(policy, given) == target:
            target = given  # same tier: keep the owner's spelling (`opus[1m]`, a dated id), no rewrite
    except Exception:
        target, d, deny, pnote = None, None, None, None  # fail open: only the Vault-router gate still applies
    if deny:
        print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": "deny",
                                                 "permissionDecisionReason": deny}}))
        return 0
    reason = gate_and_mark(data)
    if reason:
        print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": "deny",
                                                 "permissionDecisionReason": reason}}))
        return 0
    if d is None:
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


if __name__ == "__main__":
    raise SystemExit(main())
