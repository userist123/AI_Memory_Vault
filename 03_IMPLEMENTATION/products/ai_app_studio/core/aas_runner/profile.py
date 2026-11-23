"""Runner profile (owner decision 2026-11-23): "full" (default) or "isolated".

``isolated`` is the DESIGN 6.1 / B7 behaviour: no user settings, no hooks, plugins, skills, agents, CLAUDE.md or
auto-memory, only the ``ai-app-studio`` MCP server (``--strict-mcp-config``) and no built-in tool.

``full`` loads the owner's Claude Code setup into the builder: user settings (CLAUDE.md, hooks, plugins, skills,
agents), the owner's auto-memory (read-only: no tool can write it), the owner's user-scope MCP servers next to
``ai-app-studio``. The run still works in the studio's empty run dir and project files still change only through the
studio MCP tools (plan gate, allow-list, audit):

* built-ins: only the read-only ``Read``, ``Grep``, ``Glob`` plus ``Skill`` and ``Task``/``Agent``; ``Bash``,
  ``PowerShell``, ``Write``, ``Edit``, ``MultiEdit``, ``NotebookEdit``, ``WebFetch``, ``WebSearch`` stay off;
* user MCP tools that give a shell, write or delete outside the project, control processes or reach the network are
  denied by a name-based deny-list (``mcp_denylist.default.json``, overridable by the owner in
  ``<data>/config/runner_mcp_denylist.json``). A mandatory baseline is always merged in; an invalid file fails closed;
* claude.ai connectors (cloud services: every call leaves the PC) are switched off with
  ``ENABLE_CLAUDEAI_MCP_SERVERS=false`` and denied by name as well;
* the raw ``system/init`` is checked (``check_init_full``): ``ai-app-studio`` must be listed, no deny-listed tool and
  no built-in outside the read-only set may be offered, else the run is aborted before anything is recorded.
"""
from __future__ import annotations

import fnmatch
import json
import re
import unicodedata
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from studio_core import audit
from studio_core.aas_models import AppSetting
from studio_core.errors import PolicyDenied, ValidationFailed
from studio_core.ids import utcnow
from studio_core.policy import Actor

from .streamjson import CATALOGUE_TOOLS, SERVER, TOOL_PREFIX, InitViolation, _server_name

PROFILES: tuple[str, ...] = ("full", "isolated")
DEFAULT_PROFILE = "full"  # owner decision 2026-11-23
PROFILE_KEY = "runner_profile"

FULL_TOOLS: tuple[str, ...] = ("Read", "Grep", "Glob", "Skill", "Task", "Agent")  # --tools in full mode
FULL_INIT_BUILTINS = frozenset(FULL_TOOLS)  # the only built-ins system/init may offer in full mode
FULL_DENIED_BUILTINS: tuple[str, ...] = ("Bash", "PowerShell", "Edit", "Write", "MultiEdit", "NotebookEdit",
                                         "WebFetch", "WebSearch")
# Read limits in full mode (Read rules also cover Grep/Glob). CLI rule syntax: `//x` = absolute, `~/x` = home; a bare
# `**/x` is relative to the cwd (the empty run dir) and matches nothing outside it (review M1). Windows paths are
# matched in POSIX form (drive D: -> /d/...). Live probe: tests/runner/test_profile_real_cli.py (AAS_LIVE_RUNNER_PROBE=1).
FULL_READ_DENY: tuple[str, ...] = (
    "Read(~/.ssh/**)", "Read(~/.aws/**)", "Read(~/.azure/**)", "Read(~/.config/gh/**)", "Read(~/.docker/**)",
    "Read(~/.gnupg/**)", "Read(~/.claude/.credentials.json)", "Read(~/.claude.json)",
    "Read(//**/.env)", "Read(//**/.env.*)", "Read(//**/*.pem)", "Read(//**/*.key)", "Read(//**/*.pfx)",
    "Read(//**/*.p12)", "Read(//**/id_rsa*)", "Read(//**/id_ed25519*)", "Read(//**/id_ecdsa*)",
    "Read(//**/api_token)", "Read(//**/*.mcp.json)", "Read(//**/.credentials.json)", "Read(//**/.git-credentials)",
    "Read(//**/.netrc)", "Read(//**/_netrc)", "Read(//**/hosts.yml)",
)

# always denied in full mode, whatever the owner's file says (the owner can add, never remove these)
BASELINE_SERVERS: tuple[str, ...] = (
    "plugin_desktop-commander_desktop-commander", "desktop-commander", "claude_ai_Remote_Desktop_Commander",
    "chrome-devtools", "playwright", "fetch", "claude_ai_*",
)
BASELINE_SUBSTRINGS: tuple[str, ...] = ("exec", "shell", "command", "process", "write_file", "move_file", "delete",
                                        "kill")
DEFAULT_DENYLIST = Path(__file__).with_name("mcp_denylist.default.json")
OWNER_DENYLIST_NAME = "runner_mcp_denylist.json"
MAX_ENTRIES = 500

_OWNER_SERVER_NAME_RE = re.compile(r"^[A-Za-z0-9_.:-]{1,120}$")
_SERVER_RE = re.compile(r"^[A-Za-z0-9_*-]{1,120}$")
_TOOL_RE = re.compile(r"^mcp__[A-Za-z0-9_*-]{1,120}__[A-Za-z0-9_*-]{1,120}$")
_SUB_RE = re.compile(r"^[a-z0-9_]{3,40}$")
_KEYS = {"version", "servers", "tools", "substrings", "_comment"}
_STUDIO_TOOLS = tuple(TOOL_PREFIX + t for t in CATALOGUE_TOOLS)


class DenyListInvalid(ValidationFailed):
    code = "runner_denylist_invalid"


# ---------------------------------------------------------------------------------------------- owner setting
def get_profile(studio: Any) -> str:
    """The stored owner choice; no row -> the default ("full"); an unknown stored value -> "isolated" (fail closed)."""
    with studio.session() as s:
        row = s.get(AppSetting, PROFILE_KEY)
        if row is None:
            return DEFAULT_PROFILE
        return row.value if row.value in PROFILES else "isolated"


def set_profile(studio: Any, actor: Actor, profile: str) -> dict[str, Any]:
    if not actor.is_owner:
        raise PolicyDenied("only the authenticated human owner can change the runner profile")
    if profile not in PROFILES:
        raise ValidationFailed("profile must be 'full' or 'isolated'")
    with studio.audit_session() as s:
        row = s.get(AppSetting, PROFILE_KEY)
        if row is None:
            row = AppSetting(key=PROFILE_KEY, value=profile)
            s.add(row)
        row.value = profile
        row.updated_at = utcnow()
        audit.record(s, actor, "settings.runner_profile", profile)
    return {"profile": profile}


# ---------------------------------------------------------------------------------------------- deny-list
@dataclass(frozen=True)
class DenyList:
    servers: tuple[str, ...]
    tools: tuple[str, ...]
    substrings: tuple[str, ...]

    def argv_entries(self) -> list[str]:
        """``--disallowedTools`` entries. A substring that would also hit a studio catalogue tool (``delete``,
        ``write_file``) cannot be a CLI glob (deny wins over allow); it is enforced by ``check_init_full`` only."""
        out = [f"mcp__{s}" for s in self.servers]  # a server name, or a glob such as claude_ai_*
        out += list(self.tools)
        out += [f"mcp__*__*{sub}*" for sub in self.substrings if not any(sub in t.lower() for t in CATALOGUE_TOOLS)]
        return out

    def denies_server(self, name: str) -> bool:
        """True when every tool of the server is denied. Matching is NFKC + case-insensitive + `_` == `-` on the
        CLI tool-prefix form of the name; a name that is not ASCII after NFKC is denied outright (lookalikes)."""
        if _canon(name) is None:
            return True
        key = _canon(_server_key(name)) or ""
        return any(fnmatch.fnmatchcase(key, _canon(s) or "") for s in self.servers)

    def denies(self, tool: str) -> bool:
        """The deny-list is defence in depth: the primary control is ``--permission-mode dontAsk`` with no allow
        entry for any owner MCP tool (only the studio catalogue and the read-only built-ins are pre-allowed)."""
        if _canon(tool) is None:
            return True  # non-ASCII tool name: fail closed
        if tool.startswith(TOOL_PREFIX) or not tool.startswith("mcp__"):
            return False
        server, sep, name = tool[len("mcp__"):].partition("__")
        if not sep:
            return True  # not a well-formed MCP tool name: fail closed
        if self.denies_server(server):
            return True
        ct = _canon(tool) or ""
        if any(fnmatch.fnmatchcase(ct, _canon(t) or "") for t in self.tools):
            return True
        cn = _canon(name) or ""
        return any((_canon(sub) or "") in cn for sub in self.substrings)


def _canon(s: str) -> str | None:
    """NFKC, lower case, `_` == `-`. None when the text is not ASCII after NFKC (fail closed)."""
    n = unicodedata.normalize("NFKC", s)
    return n.lower().replace("_", "-") if n.isascii() else None


def _server_key(name: str) -> str:
    """A configured server name in the CLI's tool-prefix form ("claude.ai Vercel" -> "claude_ai_Vercel")."""
    return re.sub(r"[^A-Za-z0-9_-]", "_", unicodedata.normalize("NFKC", name))


def parse_denylist(obj: Any) -> DenyList:
    if not isinstance(obj, dict) or set(obj) - _KEYS or obj.get("version") != 1:
        raise DenyListInvalid("deny-list must be an object with version 1 and only servers/tools/substrings")
    lists: dict[str, list[str]] = {}
    for key, rx in (("servers", _SERVER_RE), ("tools", _TOOL_RE), ("substrings", _SUB_RE)):
        v = obj.get(key, [])
        if not isinstance(v, list) or len(v) > MAX_ENTRIES or not all(isinstance(x, str) and rx.match(x) for x in v):
            raise DenyListInvalid(f"deny-list '{key}' has an invalid entry")
        lists[key] = v
    for s in lists["servers"]:
        if fnmatch.fnmatchcase(SERVER, s) or s.strip("*") == "":
            raise DenyListInvalid("a deny-list server entry would deny the studio's own server")
    for t in lists["tools"]:
        if any(fnmatch.fnmatchcase(st, t) for st in _STUDIO_TOOLS):
            raise DenyListInvalid("a deny-list tool entry would deny a studio tool")
    servers = tuple(dict.fromkeys([*BASELINE_SERVERS, *lists["servers"]]))
    subs = tuple(dict.fromkeys([*BASELINE_SUBSTRINGS, *lists["substrings"]]))
    return DenyList(servers=servers, tools=tuple(dict.fromkeys(lists["tools"])), substrings=subs)


def load_denylist(config_dir: Path | None = None) -> DenyList:
    """The owner's file in ``<data>/config`` when present, else the committed default. Unreadable or invalid ->
    ``DenyListInvalid`` (the run is refused)."""
    owner = config_dir / OWNER_DENYLIST_NAME if config_dir is not None else None
    path = owner if owner is not None and owner.is_file() else DEFAULT_DENYLIST
    try:
        raw = path.read_text(encoding="utf-8")
        if len(raw) > 200_000:
            raise DenyListInvalid("deny-list file too large")
        obj = json.loads(raw)
    except (OSError, ValueError) as e:
        raise DenyListInvalid(f"deny-list {path.name} cannot be read") from e
    return parse_denylist(obj)


# ---------------------------------------------------------------------------------------------- init check
def check_init_full(obj: dict[str, Any], deny: DenyList) -> None:
    servers = obj.get("mcp_servers")
    if not isinstance(servers, list):
        raise InitViolation("init has no mcp_servers list")
    names = [_server_name(s) for s in servers]
    if SERVER not in names:
        raise InitViolation(f"{SERVER!r} is not among the init mcp_servers")
    bad_servers = [n for n in names if n != SERVER and (not isinstance(n, str) or deny.denies_server(n))]
    if bad_servers:
        raise InitViolation(f"init lists denied MCP servers: {[str(b)[:60] for b in bad_servers][:5]}")
    tools = obj.get("tools")
    if not isinstance(tools, list):
        raise InitViolation("init has no tools list")
    studio = set(_STUDIO_TOOLS)
    bad: list[str] = []
    for t in tools:
        if not isinstance(t, str):
            bad.append(str(t))
        elif t.startswith(TOOL_PREFIX):
            if t not in studio:
                bad.append(t)
        elif t.startswith("mcp__"):
            if deny.denies(t):
                bad.append(t)
        elif t not in FULL_INIT_BUILTINS:
            bad.append(t)
    if bad:
        raise InitViolation(f"init offers denied tools: {[b[:80] for b in bad][:5]}")


# ---------------------------------------------------------------------------------------------- settings / memory
def home_memory_dir(env: dict[str, str]) -> Path | None:
    """The owner's auto-memory of the home directory (``<config>/projects/<home-key>/memory``): the run's own cwd is
    a fresh run dir whose auto-memory would always be empty. None when it does not exist."""
    low = {k.upper(): v for k, v in env.items()}
    home = low.get("USERPROFILE") or low.get("HOME")
    if not home:
        return None
    cfg = Path(low["CLAUDE_CONFIG_DIR"]) if low.get("CLAUDE_CONFIG_DIR") else Path(home) / ".claude"
    d = cfg / "projects" / re.sub(r"[^A-Za-z0-9]", "-", home) / "memory"
    return d if d.is_dir() else None


def rule_path(p: Path) -> str:
    """An absolute path in the CLI's permission-rule form: `//` + POSIX path, a Windows drive as `/d/...`."""
    s = str(p).replace(chr(92), "/")
    m = re.match(r"^([A-Za-z]):/?(.*)$", s)
    if m:
        return f"//{m.group(1).lower()}/{m.group(2)}".rstrip("/")
    return "//" + s.strip("/")


def _env_reads(data_dir: Path | None, env: dict[str, str] | None) -> list[str]:
    low = {k.upper(): v for k, v in (env or {}).items()}
    out: list[str] = []
    if data_dir is not None:  # the studio's own state: token, run grants, run configs, audit, every workspace
        out.append(f"Read({rule_path(data_dir)}/**)")
    if low.get("CLAUDE_CONFIG_DIR"):
        out.append(f"Read({rule_path(Path(low['CLAUDE_CONFIG_DIR']))}/.credentials.json)")
    if low.get("APPDATA"):
        out.append(f"Read({rule_path(Path(low['APPDATA']) / 'GitHub CLI')}/**)")
    return out


def full_settings(memory_dir: Path | None, *, data_dir: Path | None = None,
                  env: dict[str, str] | None = None) -> dict[str, Any]:
    s: dict[str, Any] = {
        "enableAllProjectMcpServers": False,
        "permissions": {"defaultMode": "dontAsk",
                        "deny": [*FULL_DENIED_BUILTINS, *FULL_READ_DENY, *_env_reads(data_dir, env)]},
    }
    if memory_dir is not None:
        s["autoMemoryDirectory"] = str(memory_dir)
    return s


# ---------------------------------------------------------------------------------------------- owner MCP servers
def owner_mcp_servers(env: dict[str, str], deny: DenyList) -> dict[str, dict[str, Any]]:
    """The owner's user-scope MCP servers (``mcpServers`` of ``<CLAUDE_CONFIG_DIR>/.claude.json``, else
    ``~/.claude.json``) that the deny-list does not fully deny. The runner passes them in its own ``--mcp-config``
    with ``--strict-mcp-config`` (review L3), so a denied server is never launched; plugin and claude.ai servers are
    never passed. An unreadable file gives none; a server named ``ai-app-studio`` or with a non-ASCII / odd name is
    dropped."""
    low = {k.upper(): v for k, v in env.items()}
    if low.get("CLAUDE_CONFIG_DIR"):
        path = Path(low["CLAUDE_CONFIG_DIR"]) / ".claude.json"
    elif low.get("USERPROFILE") or low.get("HOME"):
        path = Path(low.get("USERPROFILE") or low.get("HOME") or "") / ".claude.json"
    else:
        return {}
    try:
        obj = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}
    servers = obj.get("mcpServers") if isinstance(obj, dict) else None
    if not isinstance(servers, dict):
        return {}
    out: dict[str, dict[str, Any]] = {}
    for name, cfg in servers.items():
        if (isinstance(name, str) and isinstance(cfg, dict) and _OWNER_SERVER_NAME_RE.match(name)
                and _canon(name) != SERVER and not deny.denies_server(name)):
            out[name] = cfg
    return out
