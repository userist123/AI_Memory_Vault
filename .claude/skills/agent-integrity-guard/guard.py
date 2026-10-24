"""agent-integrity-guard — decision core.

Pure stdlib. Given a Claude Code PreToolUse payload (tool name + input), decide
allow / ask / deny and why. The hook (hook_guard.py) turns a decision into the
permission JSON Claude Code expects; this module has no I/O so it can be tested
directly. See THREAT_MODEL.md for the controls (C1..C9).

Design: allowlist by destination and by write-location, not attack signatures.
A host not on the egress allowlist is `ask`; a secret source plus any egress is
`deny`; a write into a persistence or guard location is `deny`. On doubt about a
call that carries egress or touches a protected path, fail closed (`deny`).
"""
from __future__ import annotations

import json
import os
import re
from dataclasses import dataclass, field
from pathlib import Path

ALLOW, ASK, DENY = "allow", "ask", "deny"
_HERE = Path(__file__).resolve().parent


@dataclass
class Decision:
    action: str          # allow | ask | deny
    reason: str = ""
    control: str = ""    # which control fired (C2, C3, ...)
    matches: list = field(default_factory=list)

    def to_dict(self) -> dict:
        return {"action": self.action, "reason": self.reason, "control": self.control, "matches": self.matches}


def load_allowlist(path: Path | None = None) -> dict:
    p = path or (_HERE / "allowlist.json")
    try:
        return json.loads(p.read_text(encoding="utf-8"))
    except Exception:
        # No readable allowlist: the safe default is to approve nothing external.
        return {"owner_namespaces": [], "egress": [], "mcp_allow_prefixes": []}


# ───────────────────────── host / url handling ─────────────────────────

# A URL or bare host. Deliberately broad; we only use it to pull candidate hosts.
_URL_RE = re.compile(r"""(?xi)
    (?:(?P<scheme>[a-z][a-z0-9+.\-]*)://)?
    (?P<userinfo>[^/\s:@]+(?::[^/\s@]*)?@)?
    (?P<host>
        \[[0-9a-f:]+\] |                         # [::1]
        (?:[a-z0-9_](?:[a-z0-9_\-]*[a-z0-9_])?\.)+[a-z][a-z0-9\-]* |  # dotted name
        localhost |
        \d{1,3}(?:\.\d{1,3}){3}
    )
    (?::(?P<port>\d+))?
""")

# Tokens that mean "this command can reach the network", even without a URL in the line.
_NET_TOKENS = re.compile(r"""(?xi)
    \b(
        curl | wget | lynx | links | w3m | aria2c |
        scp | sftp | rsync | ssh | ftp | tftp | telnet | nc | ncat | netcat | socat |
        certutil | bitsadmin | mshta |
        invoke-webrequest | invoke-restmethod | start-bitstransfer |
        net\.webclient | downloadstring | downloadfile | downloaddata |
        httpclient | system\.net\.sockets | net\.sockets\.tcpclient | net\.sockets\.udpclient
    )\b
    | \biwr\b | \birm\b | \bwget\b
""")

_DATA_IN_URL = re.compile(r"(?xi) [?&][^=\s]{1,40}=.{80,} | (?:[A-Za-z0-9+/]{120,}={0,2}) ")


def _norm_host(h: str) -> str:
    return h.strip().strip(".").lower().strip("[]")


def extract_hosts(text: str) -> list[str]:
    hosts = []
    for m in _URL_RE.finditer(text or ""):
        # a userinfo@ before the host is the real host; the classic https://trusted@evil trick
        # is handled because the regex binds host to what follows userinfo, i.e. evil.
        h = _norm_host(m.group("host"))
        if h:
            hosts.append(h)
    return hosts


def _host_approved(host: str, allow: dict) -> bool:
    host = _norm_host(host)
    for e in allow.get("egress", []):
        if host == _norm_host(e.get("host", "")):
            return True
    return False


def host_owner_scoped(host: str, allow: dict) -> bool:
    return any(_norm_host(e.get("host", "")) == _norm_host(host) and e.get("owner_paths")
               for e in allow.get("egress", []))


# ───────────────────────── protected paths ─────────────────────────

# Write destinations that mean persistence or guard-tampering. Checked against
# normalised forward-slash paths, case-insensitive (Windows).
_PERSIST_PATH = re.compile(r"""(?xi)
    /\.git/hooks/ |
    /\.bashrc$ | /\.bash_profile$ | /\.profile$ | /\.zshrc$ |
    /\.config/autostart/ |
    /microsoft/windows/start\ menu/programs/startup/ |
    /appdata/roaming/microsoft/windows/start\ menu/programs/startup/
""")
_GUARD_PATH = re.compile(r"(?i)agent-integrity-guard/|/\.claude/settings(\.local)?\.json$|/\.claude/settings/")

_PERSIST_SHELL = re.compile(r"""(?xi)
    \bschtasks\b .* \b/create\b | \bschtasks\s+/create\b |
    \bcrontab\b | \b(New-ScheduledTask|Register-ScheduledTask)\b |
    \breg(\.exe)?\b .* \b(add|import)\b .* \\run | currentversion\\run |
    \bNew-ItemProperty\b .* \\run | \bSet-ItemProperty\b .* \\run |
    \bsc(\.exe)?\s+create\b | \bnew-service\b |
    register-wmievent | __eventfilter | commandlineeventconsumer |
    \bsetx\b
""")


# ───────────────────────── secret sources ─────────────────────────

_SECRET_SRC = re.compile(r"""(?xi)
    \.env\b | \.env\. | /\.ssh\b | /\.aws\b | /\.gnupg\b |
    id_rsa | id_ed25519 | \.pem\b | \.ppk\b |
    \.npmrc | \.pypirc | \.netrc | _netrc |
    credentials(\.json|\.yml|\.yaml|\.txt)? | secrets(\.json|\.yml|\.yaml)? |
    \bprintenv\b | \benv\b\s*(\||$) | get-childitem\s+env: | \bgci\s+env: | \$env: |
    hmac\.key | \.key\b | \.keystore\b |
    /appdata/.*/(login\ data|cookies|key4\.db|logins\.json) |
    \.config/google-chrome/ | /library/application\ support/
""")

# publish / push to places the owner may not own
_PUBLISH = re.compile(r"""(?xi)
    \bnpm\s+publish\b | \btwine\s+upload\b | \bpip\s+upload\b |
    \bdocker\s+push\b | \bgh\s+release\s+create\b | \bcargo\s+publish\b | \bgem\s+push\b
""")
_GIT_REMOTE = re.compile(r"(?xi)\bgit\s+(?:-[^\s]+\s+)*(?:push|remote\s+(?:add|set-url))\b")
_GH_REPO = re.compile(r"(?xi)\bgh\b.*?(?:-R|--repo)[=\s]+([^\s/]+)/([^\s]+)|\bgh\s+api\s+repos/([^/\s]+)/")
_GITHUB_URL_OWNER = re.compile(r"(?xi)github\.com[:/]+([^/\s]+)/")


def _reads_secret(cmd: str) -> bool:
    return bool(_SECRET_SRC.search(cmd))


def _has_egress(cmd: str) -> bool:
    # A network call needs a network tool OR a real URL scheme. A bare dotted word
    # like README.md or script.py is a filename, not a destination, so it is not egress.
    return bool(_NET_TOKENS.search(cmd) or re.search(r"[a-z][a-z0-9+.\-]*://", cmd, re.I))


# ───────────────────────── command decision ─────────────────────────

def decide_command(cmd: str, allow: dict) -> Decision:
    """Decide a shell command (Bash or PowerShell)."""
    if not cmd or not cmd.strip():
        return Decision(ALLOW)
    low = cmd

    # C6/C7 persistence and guard tampering via shell
    if _PERSIST_SHELL.search(low):
        return Decision(DENY, "command sets up autostart/persistence", "C6")

    # C4 publish / foreign remote
    if _PUBLISH.search(low):
        return Decision(DENY, "publishes to an external registry", "C4")
    mg = _GH_REPO.search(low)
    if mg:
        owner = mg.group(1) or mg.group(3)
        if owner and owner.lower() not in [o.lower() for o in allow.get("owner_namespaces", [])]:
            return Decision(ASK, f"gh targets a repo outside the owner namespace: {owner}", "C4")
    if _GIT_REMOTE.search(low):
        owners = _GITHUB_URL_OWNER.findall(low)
        foreign = [o for o in owners if o.lower() not in [n.lower() for n in allow.get("owner_namespaces", [])]
                   and o.lower() not in ("", "git")]
        if foreign:
            return Decision(ASK, f"git remote/push to a non-owner namespace: {foreign[0]}", "C4")

    egress = _has_egress(low)
    hosts = extract_hosts(low)

    # C3 secret + egress (deny even to an approved host)
    if egress and _reads_secret(low):
        return Decision(DENY, "reads a secret source and sends data out in the same command", "C3")

    # C2 egress to an unapproved host
    if egress:
        unapproved = []
        for h in hosts:
            if _host_approved(h, allow):
                continue
            unapproved.append(h)
        if unapproved:
            return Decision(ASK, f"network egress to unapproved host(s): {', '.join(sorted(set(unapproved)))}",
                            "C2", sorted(set(unapproved)))
        if not hosts:
            # a net tool with no parseable host: cannot confirm the destination → fail closed
            return Decision(ASK, "network command with no identifiable destination", "C2")
    return Decision(ALLOW)


# ───────────────────────── file-write decision ─────────────────────────

def _norm_path(p: str) -> str:
    return (p or "").replace("\\", "/").lower()


def decide_write(path: str, allow: dict) -> Decision:
    np = _norm_path(path)
    if _GUARD_PATH.search(np):
        return Decision(DENY, "write would modify the guard or the settings that load it", "C7")
    if _PERSIST_PATH.search(np):
        return Decision(DENY, "write lands in an autostart/persistence location", "C6")
    return Decision(ALLOW)


# ───────────────────────── url (WebFetch) / mcp ─────────────────────────

def decide_url(url: str, allow: dict) -> Decision:
    hosts = extract_hosts(url)
    host = hosts[0] if hosts else ""
    if host and not _host_approved(host, allow):
        return Decision(ASK, f"fetch to unapproved host: {host}", "C2", [host])
    if _DATA_IN_URL.search(url or ""):
        return Decision(ASK, "outbound URL carries a large/opaque payload", "C2")
    return Decision(ALLOW)


def decide_mcp(tool_name: str, allow: dict) -> Decision:
    prefixes = allow.get("mcp_allow_prefixes", [])
    if any(tool_name.startswith(p) for p in prefixes):
        return Decision(ALLOW)
    return Decision(ASK, f"MCP tool not on the local allowlist: {tool_name}", "C5")


# ───────────────────────── dispatch ─────────────────────────

_SETTINGS_KEYS_DENY = {"disableAllHooks"}


def decide(tool_name: str, tool_input: dict, allow: dict | None = None) -> Decision:
    allow = allow if allow is not None else load_allowlist()
    ti = tool_input or {}
    try:
        if tool_name in ("Bash", "PowerShell"):
            return decide_command(str(ti.get("command", "")), allow)
        if tool_name in ("Edit", "Write", "NotebookEdit"):
            d = decide_write(str(ti.get("file_path", "")), allow)
            if d.action != ALLOW:
                return d
            # a settings write that disables hooks is tampering even if the path check passed
            blob = json.dumps(ti).lower()
            if "disableallhooks" in blob and ".claude" in _norm_path(str(ti.get("file_path", ""))):
                return Decision(DENY, "write would disable Claude Code hooks", "C7")
            return d
        if tool_name in ("WebFetch", "WebSearch"):
            return decide_url(str(ti.get("url", "") or ti.get("prompt", "")), allow)
        if tool_name.startswith("mcp__"):
            return decide_mcp(tool_name, allow)
        return Decision(ALLOW)
    except Exception as e:  # C8: never crash-open on a call that looks dangerous
        probe = json.dumps(ti).lower() if isinstance(ti, dict) else str(ti).lower()
        if any(t in probe for t in ("http://", "https://", "curl", "wget", "ssh", "scp", "\\run", "schtasks")):
            return Decision(DENY, f"guard error on a call that looks like egress/persistence: {e}", "C8")
        return Decision(ALLOW, f"guard error, no risky shape detected: {e}", "C8")
