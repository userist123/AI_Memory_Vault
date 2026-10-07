"""Telegram front-end for the local vault assistant (long polling; no webhook is exposed).

    python -m cognitive_core.telegram_vault_bot --check     # startup checks only
    python -m cognitive_core.telegram_vault_bot --ask "citește VAULT_STATE.md"   # one local turn, no Telegram
    python -m cognitive_core.telegram_vault_bot             # run

Security model
  * Default-deny: only numeric Telegram user ids in `telegram.allowed_user_ids`
    (04_CONFIG/access_policy.yaml) or VAULT_TELEGRAM_ALLOWED_IDS are answered; with an empty
    allowlist the bot refuses to start. Only private chats; forwards, files and edits are ignored.
  * The token comes from VAULT_TELEGRAM_TOKEN or from `telegram.token` in the per-user state
    directory (outside the repository, readable by the user only). It is never logged.
  * The bot is principal `telegram.bot` (channel `telegram`, ceiling INTERNAL): messages transit
    Telegram's cloud even though the model is local, so SENSITIVE content is never served here.
  * Replies are plain text (no parse_mode), split under Telegram's 4096-character limit.
  * Per-user rate limit; `/debug on|off` adds the [ROUTER]/[TOOL_CALL]/[TOOL_RESULT] trace
    (steps, codes and hashes — never content).
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import time
import urllib.parse
import urllib.request
from collections import defaultdict, deque
from pathlib import Path
from typing import Any, Callable, Deque, Dict, List, Optional, Set

_PACKAGES = Path(__file__).resolve().parents[1]
if str(_PACKAGES) not in sys.path:
    sys.path.insert(0, str(_PACKAGES))

TOKEN_ENV = "VAULT_TELEGRAM_TOKEN"
ALLOWED_ENV = "VAULT_TELEGRAM_ALLOWED_IDS"
PRINCIPAL = "telegram.bot"
MAX_MESSAGE = 4000

Api = Callable[[str, Dict[str, Any]], Dict[str, Any]]


def log(line: str) -> None:
    sys.stderr.write(line + "\n")
    sys.stderr.flush()


def tg_len(text: str) -> int:
    """Telegram counts message length in UTF-16 code units (an emoji is 2)."""
    return len(text.encode("utf-16-le")) // 2


def _cut(text: str, limit: int) -> int:
    """Largest prefix length (in code points) whose UTF-16 length is <= limit."""
    size = 0
    for i, ch in enumerate(text):
        size += 2 if ord(ch) > 0xFFFF else 1
        if size > limit:
            return i
    return len(text)


def split_message(text: str, limit: int = MAX_MESSAGE) -> List[str]:
    parts, current = [], ""
    for line in text.split("\n"):
        while tg_len(line) > limit:
            if current:
                parts.append(current)
                current = ""
            cut = max(1, _cut(line, limit))
            parts.append(line[:cut])
            line = line[cut:]
        if tg_len(current) + tg_len(line) + 1 > limit:
            parts.append(current)
            current = line
        else:
            current = f"{current}\n{line}" if current else line
    if current:
        parts.append(current)
    return parts or [""]


def read_token() -> str:
    token = os.environ.get(TOKEN_ENV, "").strip()
    if token:
        return token
    from interfaces import vault_runtime
    path = vault_runtime.state_dir() / "telegram.token"
    if not path.is_file():
        raise SystemExit(f"[TOKEN] missing: set {TOKEN_ENV} or create telegram.token in the per-user state directory")
    if os.name != "nt" and path.stat().st_mode & 0o077:
        raise SystemExit("[TOKEN] telegram.token is readable by other users; chmod 600 it")
    return path.read_text(encoding="utf-8").strip()


def allowed_ids(policy) -> Set[int]:
    ids = {int(x) for x in (policy.telegram.get("allowed_user_ids") or [])}
    env = os.environ.get(ALLOWED_ENV, "")
    ids |= {int(x) for x in env.replace(";", ",").split(",") if x.strip().lstrip("-").isdigit()}
    return ids


def telegram_api(token: str) -> Api:
    base = f"https://api.telegram.org/bot{token}/"

    def call(method: str, params: Dict[str, Any]) -> Dict[str, Any]:
        data = urllib.parse.urlencode({k: (json.dumps(v) if isinstance(v, (dict, list)) else v)
                                       for k, v in params.items()}).encode()
        timeout = float(params.get("timeout", 0)) + 15
        with urllib.request.urlopen(urllib.request.Request(base + method, data=data), timeout=timeout) as resp:  # noqa: S310
            return json.loads(resp.read().decode("utf-8"))
    return call


class VaultTelegramBot:
    def __init__(self, assistant, allowed: Set[int], api: Api, *, allowed_chat_types=("private",),
                 per_minute: int = 10, clock: Callable[[], float] = time.monotonic):
        self.assistant = assistant
        self.allowed = set(allowed)
        self.api = api
        self.allowed_chat_types = set(allowed_chat_types)
        self.per_minute = per_minute
        self.clock = clock
        self.debug: Set[int] = set()
        self.hits: Dict[int, Deque[float]] = defaultdict(deque)
        self.offset = 0

    def _rate_ok(self, user_id: int) -> bool:
        now = self.clock()
        window = self.hits[user_id]
        while window and now - window[0] > 60:
            window.popleft()
        if len(window) >= self.per_minute:
            return False
        window.append(now)
        return True

    def handle_update(self, update: Dict[str, Any]) -> Optional[List[str]]:
        """The reply chunks for one update, or None when the update is ignored."""
        msg = update.get("message")
        if not isinstance(msg, dict):
            return None                                   # edits, callbacks, channel posts: ignored
        chat = msg.get("chat") or {}
        user = msg.get("from") or {}
        user_id = user.get("id")
        if chat.get("type") not in self.allowed_chat_types or user.get("is_bot"):
            return None
        if user_id not in self.allowed:
            log(f"[DENY] user_id={user_id} not in allowlist")
            return None
        if any(k in msg for k in ("forward_origin", "forward_from", "forward_from_chat", "document", "photo")):
            return ["Ignor mesajele redirecționate și fișierele. Scrie o întrebare sau o comandă."]
        text = msg.get("text")
        if not isinstance(text, str):
            return None
        if not self._rate_ok(user_id):
            return ["Prea multe cereri; încearcă peste un minut."]
        lowered = text.strip().lower()
        if lowered in ("/debug on", "/debug off"):
            (self.debug.add if lowered.endswith("on") else self.debug.discard)(chat["id"])
            return [f"Proof-mode {'activ' if chat['id'] in self.debug else 'oprit'}."]
        reply = self.assistant.handle(text)
        body = reply.text
        if chat["id"] in self.debug and reply.trace:
            body += "\n\n" + "\n".join(reply.trace)
        return split_message(body)

    def poll_once(self, timeout: int = 30) -> int:
        resp = self.api("getUpdates", {"offset": self.offset, "timeout": timeout,
                                       "allowed_updates": ["message"]})
        handled = 0
        for update in resp.get("result", []):
            self.offset = max(self.offset, int(update.get("update_id", 0)) + 1)
            chunks = self.handle_update(update)
            if chunks:
                chat_id = update["message"]["chat"]["id"]
                for chunk in chunks:
                    self.api("sendMessage", {"chat_id": chat_id, "text": chunk,
                                             "disable_web_page_preview": True})
                handled += 1
        return handled


def startup_checks(access, allowed: Set[int]) -> List[str]:
    lines = [f"[VAULT_ROOT] {access.repo_root}",
             f"[VAULT_ROOT_EXISTS] {access.repo_root.is_dir()}",
             f"[VAULT_GOVERNANCE_EXISTS] {(access.repo_root / '00_GOVERNANCE').is_dir()}",
             f"[PRINCIPAL] {access.principal.name} channel={access.principal.channel} "
             f"clearance={access.policy.effective_clearance(access.principal)}",
             f"[ALLOWLIST] {len(allowed)} user id(s)"]
    return lines


def ask_once(assistant, text: str) -> int:
    """One turn on the console: the reply the bot would send, then its trace and citations.

    Exit 0 when the reply is grounded (verified answer, verbatim read, listing, search, help),
    3 otherwise (refused, not found, fallback to raw fragments, model error).
    """
    reply = assistant.handle(text)
    log(f"[REPLY] mode={reply.mode} code={reply.code}")
    for line in reply.trace:
        log(line)
    for citation in reply.citations:
        log(f"[CITE] {citation}")
    print(reply.text, flush=True)
    grounded = reply.code == "OK" and reply.mode in ("answer", "extractive", "list", "search", "help")
    return 0 if grounded else 3


def main(argv: Optional[List[str]] = None) -> int:
    ap = argparse.ArgumentParser(prog="python -m cognitive_core.telegram_vault_bot")
    ap.add_argument("--check", action="store_true", help="run the startup checks and exit")
    ap.add_argument("--ask", metavar="TEXT",
                    help="answer one message locally through the same pipeline (no Telegram, no token) and exit")
    args = ap.parse_args(argv)
    from vault_access.core import VaultAccess
    from vault_access.ollama_assistant import VaultAssistant
    from interfaces.vault_cli import _note_eligibility, _search_backend
    access = VaultAccess(PRINCIPAL, "ollama", search_backend=_search_backend, note_eligibility=_note_eligibility)
    allowed = allowed_ids(access.policy)
    for line in startup_checks(access, allowed):
        log(line)
    if not (access.repo_root / "00_GOVERNANCE").is_dir():
        log("[ABORT] vault root has no 00_GOVERNANCE")
        return 1
    if args.ask is not None:
        return ask_once(VaultAssistant(access), args.ask)
    if not allowed:
        log(f"[ABORT] empty allowlist: add your numeric Telegram user id to telegram.allowed_user_ids "
            f"in 04_CONFIG/access_policy.yaml or set {ALLOWED_ENV}")
        return 1
    if args.check:
        return 0
    bot = VaultTelegramBot(VaultAssistant(access), allowed, telegram_api(read_token()),
                           allowed_chat_types=tuple(access.policy.telegram.get("allowed_chat_types") or ("private",)),
                           per_minute=int(access.policy.telegram.get("max_requests_per_minute") or 10))
    log("[BOT] polling")
    while True:
        try:
            bot.poll_once()
        except KeyboardInterrupt:
            return 0
        except Exception as exc:  # noqa: BLE001 - keep polling; never print the token
            log(f"[BOT] error={type(exc).__name__}")
            time.sleep(5)


if __name__ == "__main__":
    raise SystemExit(main())
