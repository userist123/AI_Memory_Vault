"""agent-integrity-guard — decision core.

Pure stdlib. Given a Claude Code tool call (name + input), return allow / ask /
deny and why. The hook (hook_guard.py) turns a decision into permission JSON;
this module has no I/O so it can be tested directly. See THREAT_MODEL.md for
the controls (C1..C9).

Principle: allowlist by destination and by write-location, not attack
signatures. A host not on the egress allowlist is `ask`; a secret source plus
egress is `deny`; a write (by any tool OR by a shell redirection/verb) into a
guard or persistence location is `deny`. On doubt about a call that carries
egress or touches a protected path, fail closed.
"""
from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from pathlib import Path
from urllib.parse import urlsplit

ALLOW, ASK, DENY = "allow", "ask", "deny"
_HERE = Path(__file__).resolve().parent


@dataclass
class Decision:
    action: str
    reason: str = ""
    control: str = ""
    matches: list = field(default_factory=list)

    def to_dict(self) -> dict:
        return {"action": self.action, "reason": self.reason, "control": self.control, "matches": self.matches}


def load_allowlist(path: Path | None = None) -> dict:
    p = path or (_HERE / "allowlist.json")
    try:
        return json.loads(p.read_text(encoding="utf-8"))
    except Exception:
        return {"owner_namespaces": [], "egress": [], "mcp_allow_prefixes": []}


# ───────────────────────── destinations ─────────────────────────

_URL = re.compile(r"(?i)\b([a-z][a-z0-9+.\-]*)://([^\s'\"<>`|;)]+)")
# scp / git-ssh form  user@host:path  (host must look like a domain or ip)
_SCP = re.compile(r"(?i)(?<![\w/:.])(?:[\w.\-]+@)?((?:[a-z0-9][a-z0-9\-]*\.)+[a-z]{2,}|\d{1,3}(?:\.\d{1,3}){3}):(?=[\w.\-~/])")
# UNC path  \\host\share
_UNC = re.compile(r"\\\\([a-z0-9][a-z0-9.\-]*)\\", re.I)
# network-capable command words (leading boundary excludes .ssh / foo_ssh)
_NET = re.compile(r"""(?ix)
    (?<![\w.])(
        curl | wget | lynx | links | w3m | aria2c | httpie | http | https |
        scp | sftp | rsync | ssh | ftp | tftp | telnet | nc | ncat | netcat | socat |
        dig | nslookup | host | ping | traceroute | tracert |
        certutil | bitsadmin | mshta |
        invoke-webrequest | invoke-restmethod | start-bitstransfer | wget |
        net\.webclient | downloadstring | downloadfile | downloaddata |
        system\.net\.sockets | net\.sockets\.tcpclient
    )(?![\w])
    | (?<![\w.])(iwr|irm|curl)(?![\w])
""")  # git remotes are judged separately (C4); scp-form in a git/scp command counts via _SCP
_DATA_IN_URL = re.compile(r"(?xi) [?&][^=\s]{1,40}=.{80,} | (?:[A-Za-z0-9+/]{120,}={0,2}) ")


def _norm_host(h: str) -> str:
    return (h or "").strip().strip("[]").rstrip(".").lower()


def extract_targets(text: str) -> list[tuple[str, str]]:
    """(host, path) for every URL, scp-form and UNC target in the text."""
    out: list[tuple[str, str]] = []
    for scheme, rest in _URL.findall(text or ""):
        parts = urlsplit(f"{scheme}://{rest}")
        if parts.hostname:
            out.append((_norm_host(parts.hostname), parts.path or "/"))
    for m in _SCP.finditer(text or ""):
        out.append((_norm_host(m.group(1)), "/"))
    for h in _UNC.findall(text or ""):
        out.append((_norm_host(h), "/"))
    return out


def host_approved(host: str, path: str, allow: dict) -> bool:
    host = _norm_host(host)
    owners = [o.lower() for o in allow.get("owner_namespaces", [])]
    for e in allow.get("egress", []):
        if host != _norm_host(e.get("host", "")):
            continue
        if not e.get("owner_paths"):
            return True
        # owner_paths: the first path segment must be an owner namespace
        seg = [s for s in (path or "").split("/") if s]
        if seg and seg[0].lower() in owners:
            return True
        # api.github.com/repos/<owner>/...
        if len(seg) >= 2 and seg[0].lower() in ("repos", "users", "orgs") and seg[1].lower() in owners:
            return True
        return False
    return False


def _has_egress(cmd: str) -> bool:
    return bool(_NET.search(cmd) or re.search(r"[a-z][a-z0-9+.\-]*://", cmd, re.I)
                or _UNC.search(cmd) or _SCP.search(cmd))


# ───────────────────────── protected paths ─────────────────────────

# .claude/settings matched as a prefix (not anchored to `.json$`), so settings.json,
# settings.local.json, settings/ and quote-split names all match once quotes are stripped.
_GUARD_PATH = re.compile(r"(?i)agent-integrity-guard[/\\]|(?:^|[/\\])\.claude[/\\]settings")
_PERSIST_PATH = re.compile(r"""(?ix)
    [/\\]\.git[/\\]hooks[/\\] |
    (?:^|[/\\])\.(bashrc|bash_profile|bash_logout|profile|zshrc|zprofile|zlogin)(\.d)? $ |
    [/\\]\.bashrc\.d[/\\] |
    [/\\]\.config[/\\]autostart[/\\] |
    [/\\]\.config[/\\]systemd[/\\] |
    [/\\]\.config[/\\]fish[/\\] |
    [/\\]library[/\\]launchagents[/\\] | [/\\]library[/\\]launchdaemons[/\\] |
    [/\\]etc[/\\](cron|init|systemd|profile\.d|rc\.local) |
    start\ menu[/\\]programs[/\\]startup[/\\] |
    image\ file\ execution\ options
""")


def _strip_quotes(s: str) -> str:
    # a shell collapses set""tings.json and set''tings.json to settings.json
    return (s or "").replace('"', "").replace("'", "")

_PERSIST_SHELL = re.compile(r"""(?ix)
    \bschtasks\b[^\n]*\/create | \bschtasks\s+\/create |
    \bcrontab\b | (?<![\w.])at\s+(now|noon|midnight|teatime|\d) |
    (?<![\w.])reg(\.exe)?\b[^\n]*\b(add|import)\b[^\n]*(\\run\b|\\runonce\b|image\ file\ execution\ options) |
    new-itemproperty[^\n]*\\run | set-itemproperty[^\n]*\\run |
    \bsc(\.exe)?\s+create\b | \bnew-service\b |
    register-wmievent | __eventfilter | commandlineeventconsumer |
    \bsetx\b |
    systemctl\s+(--user\s+)?enable | \blaunchctl\s+(load|bootstrap) |
    new-scheduledtask | register-scheduledtask
""")

# shell write-sinks: paths this command writes to / deletes
_REDIR = re.compile(r"(?:\d*>>?|&>>?)\s*(?:\"([^\"]+)\"|'([^']+)'|([^\s;&|<>]+))")
_WRITE_VERB = re.compile(r"""(?ix)
    \btee\b | \bsed\b[^\n]*-i | \b(cp|mv|install|ln|dd|rsync|rm|unlink|shred|truncate)\b |
    \bchmod\b | \bchattr\b | \battrib\b |
    set-content | add-content | out-file | \bni\b | new-item | copy-item | move-item | remove-item |
    open\s*\([^\n]*['\"][wax]\+? | \.write(text|bytes)?\s*\( | >\s*
""")


def _shell_write_targets(cmd: str) -> list[str]:
    """Paths the command appears to write to or delete. Over-collects on purpose:
    only used to check against guard/persistence locations, where over-collection is safe.
    Each target is returned raw and quote-stripped, because a shell collapses
    set""tings.json / set''tings.json to settings.json before the write happens."""
    raw: list[str] = []
    for m in _REDIR.finditer(cmd):
        raw.append(next(g for g in m.groups() if g))
    if _WRITE_VERB.search(cmd):
        for tok in re.findall(r"(?:\"[^\"]+\"|'[^']+'|[^\s;&|<>]+)", cmd):
            t = tok.strip("\"'")
            if "/" in t or "\\" in t or '"' in tok or "'" in tok:
                raw.append(t)
    targets = []
    for t in raw:
        targets.append(t)
        sq = _strip_quotes(t)
        if sq != t:
            targets.append(sq)
    return targets


# ───────────────────────── secret sources ─────────────────────────

_SECRET_SRC = re.compile(r"""(?ix)
    \.env\b | \.env\. | [/\\]\.ssh\b | [/\\]\.aws\b | [/\\]\.gnupg\b |
    \bid_rsa\b | \bid_ed25519\b | \.pem\b | \.ppk\b |
    \.npmrc\b | \.pypirc\b | \.netrc\b | \b_netrc\b |
    \bcredentials?(\.json|\.yml|\.yaml|\.txt)?\b | \bsecrets?(\.json|\.yml|\.yaml)?\b |
    \bprintenv\b | get-childitem\s+env: | \bgci\s+env: | \$env: |
    \bhmac\.key\b | \.keystore\b |
    login\ data | key4\.db | logins\.json | cookies\.sqlite
""")
_SECRET_READ_CMD = re.compile(r"(?ix)\b(cat|type|less|more|head|tail|cp|copy|mv|move|scp|rsync|get-content|gc)\b")

_PUBLISH = re.compile(r"""(?ix)
    \bnpm\s+publish\b | \btwine\s+upload\b | \bpip\s+upload\b |
    \bdocker\s+push\b | \bgh\s+release\s+create\b | \bcargo\s+publish\b | \bgem\s+push\b
""")
_GIT_REMOTEISH = re.compile(r"(?ix)\bgit\b(?:\s+-[^\s]+)*\s+(?:push|remote\s+(?:add|set-url))\b")
_GH_REPO = re.compile(r"(?ix)\bgh\b.*?(?:-R|--repo)[=\s]+([^\s/]+)/|\bgh\s+api\s+repos/([^/\s]+)/")
_GITHUB_OWNER = re.compile(r"(?ix)github\.com[:/]+([^/\s]+)/")


def _reads_secret(cmd: str) -> bool:
    return bool(_SECRET_SRC.search(cmd))


# ───────────────────────── command decision ─────────────────────────

def decide_command(cmd: str, allow: dict) -> Decision:
    if not cmd or not cmd.strip():
        return Decision(ALLOW)
    low = cmd

    # C7/C6: a shell write/delete whose target is a guard or persistence location
    writes = _shell_write_targets(low)
    for tgt in writes:
        n = tgt.replace("\\", "/")
        if _GUARD_PATH.search(n) or _GUARD_PATH.search(tgt):
            return Decision(DENY, f"shell command writes/deletes a guard or settings file: {tgt}", "C7")
        if _PERSIST_PATH.search(n) or _PERSIST_PATH.search(tgt):
            return Decision(DENY, f"shell command writes into a persistence location: {tgt}", "C6")

    # C7 content: a write that would turn the hooks off, even if the sink path was obscured
    if writes and "disableallhooks" in _strip_quotes(low).lower():
        return Decision(DENY, "shell command writes content that disables Claude Code hooks", "C7")

    # C6: persistence mechanisms invoked directly
    if _PERSIST_SHELL.search(low):
        return Decision(DENY, "command sets up autostart/persistence", "C6")

    # C4: publish / foreign remote
    if _PUBLISH.search(low):
        return Decision(DENY, "publishes to an external registry", "C4")
    mg = _GH_REPO.search(low)
    if mg:
        owner = mg.group(1) or mg.group(2)
        if owner and owner.lower() not in [o.lower() for o in allow.get("owner_namespaces", [])]:
            return Decision(ASK, f"gh targets a repo outside the owner namespace: {owner}", "C4")
    if _GIT_REMOTEISH.search(low):
        foreign = [o for o in _GITHUB_OWNER.findall(low)
                   if o.lower() not in [n.lower() for n in allow.get("owner_namespaces", [])] and o.lower() not in ("", "git")]
        if foreign:
            return Decision(ASK, f"git remote/push to a non-owner namespace: {foreign[0]}", "C4")

    egress = _has_egress(low)
    targets = extract_targets(low)

    # C3: secret source + egress in one command (deny even to an approved host)
    if egress and _reads_secret(low):
        return Decision(DENY, "reads a secret source and sends data out in the same command", "C3")

    # C3b: staging a secret to another location (visible step before a later send)
    if _reads_secret(low) and _SECRET_READ_CMD.search(low) and re.search(r">\s*|(?:cp|copy|mv|move|scp|rsync)\b", low, re.I):
        # only ask when the command also names a destination path/redirect
        if _shell_write_targets(low):
            return Decision(ASK, "copies a secret source to another location", "C3")

    # C2: egress to an unapproved destination
    if egress:
        unapproved = [h for (h, p) in targets if not host_approved(h, p, allow)]
        if unapproved:
            return Decision(ASK, f"network egress to unapproved host(s): {', '.join(sorted(set(unapproved)))}",
                            "C2", sorted(set(unapproved)))
        if not targets:
            return Decision(ASK, "network command with no identifiable destination", "C2")
    return Decision(ALLOW)


# ───────────────────────── write / url / mcp ─────────────────────────

def _norm_path(p: str) -> str:
    return (p or "").replace("\\", "/").lower()


def decide_write(path: str, blob: str, allow: dict) -> Decision:
    for cand in (path or "", _strip_quotes(path or "")):
        np = _norm_path(cand)
        if _GUARD_PATH.search(np) or _GUARD_PATH.search(cand):
            return Decision(DENY, "write would modify the guard or the settings that load it", "C7")
        if _PERSIST_PATH.search(np) or _PERSIST_PATH.search(cand):
            return Decision(DENY, "write lands in an autostart/persistence location", "C6")
    if "disableallhooks" in (blob or "").lower() and ".claude" in np:
        return Decision(DENY, "write would disable Claude Code hooks", "C7")
    return Decision(ALLOW)


def decide_url(url: str, allow: dict) -> Decision:
    targets = extract_targets(url)
    if not targets and url:
        # a bare host with no scheme in a fetch field
        h = _norm_host(url.split("/")[0])
        targets = [(h, "/" + "/".join(url.split("/")[1:]))] if h else []
    for host, path in targets:
        if not host_approved(host, path, allow):
            return Decision(ASK, f"fetch to unapproved host: {host}", "C2", [host])
    if _DATA_IN_URL.search(url or ""):
        return Decision(ASK, "outbound URL carries a large/opaque payload", "C2")
    return Decision(ALLOW)


def decide_mcp(tool_name: str, allow: dict) -> Decision:
    if any(tool_name.startswith(p) for p in allow.get("mcp_allow_prefixes", [])):
        return Decision(ALLOW)
    return Decision(ASK, f"MCP tool not on the local allowlist: {tool_name}", "C5")


# ───────────────────────── dispatch ─────────────────────────

# tools that only read / plan; anything else that is unrecognised and carries a
# protected path or egress is treated conservatively (C8).
_READONLY_TOOLS = {"Read", "Grep", "Glob", "TodoWrite", "Task", "Agent", "BashOutput",
                   "ListMcpResources", "ReadMcpResource"}
_WRITE_TOOLS = {"Edit", "Write", "NotebookEdit", "MultiEdit"}


def _write_paths(tool_input: dict) -> list[str]:
    paths = []
    for k in ("file_path", "notebook_path", "path"):
        if isinstance(tool_input.get(k), str):
            paths.append(tool_input[k])
    for e in tool_input.get("edits", []) or []:
        if isinstance(e, dict) and isinstance(e.get("file_path"), str):
            paths.append(e["file_path"])
    return paths


def decide(tool_name: str, tool_input: dict, allow: dict | None = None) -> Decision:
    allow = allow if allow is not None else load_allowlist()
    ti = tool_input if isinstance(tool_input, dict) else {}
    try:
        if tool_name in ("Bash", "PowerShell", "BashShell"):
            return decide_command(str(ti.get("command", "")), allow)
        if tool_name in _WRITE_TOOLS:
            blob = json.dumps(ti)
            paths = _write_paths(ti)
            if not paths:
                # a write tool with no recognisable path, but content that disables hooks
                if "disableallhooks" in blob.lower():
                    return Decision(DENY, "write would disable Claude Code hooks", "C7")
                return Decision(ALLOW)
            for p in paths:
                d = decide_write(p, blob, allow)
                if d.action != ALLOW:
                    return d
            return Decision(ALLOW)
        if tool_name in ("WebFetch", "WebSearch"):
            return decide_url(str(ti.get("url", "") or ti.get("prompt", "")), allow)
        if tool_name.startswith("mcp__"):
            return decide_mcp(tool_name, allow)
        if tool_name in _READONLY_TOOLS:
            return Decision(ALLOW)
        # C8: an unrecognised tool. If its input names a protected path or a destination, be careful.
        blob = json.dumps(ti)
        for p in _write_paths(ti) + [blob]:
            np = _norm_path(p)
            if _GUARD_PATH.search(np) or _PERSIST_PATH.search(np):
                return Decision(DENY, f"unrecognised tool {tool_name} touches a protected path", "C8")
        if _has_egress(blob):
            unapproved = [h for (h, pth) in extract_targets(blob) if not host_approved(h, pth, allow)]
            if unapproved:
                return Decision(ASK, f"unrecognised tool {tool_name} with unapproved egress: {unapproved}", "C8", unapproved)
        return Decision(ALLOW)
    except Exception as e:
        probe = (json.dumps(ti) if isinstance(ti, dict) else str(ti)).lower()
        if any(t in probe for t in ("://", "curl", "wget", "ssh", "scp", "\\run", "schtasks", "settings.json",
                                    "agent-integrity-guard", "crontab", ".env", "id_rsa")):
            return Decision(DENY, f"guard error on a call that looks like egress/persistence/tampering: {e}", "C8")
        return Decision(ALLOW, f"guard error, no risky shape detected: {e}", "C8")
