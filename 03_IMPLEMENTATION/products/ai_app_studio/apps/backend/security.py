"""HTTP boundary for the loopback REST API (pure ASGI middleware, runs before routing).

Controls (see docs/SECURITY.md):
  * Host allowlist  - exact "127.0.0.1:<port>" / "localhost:<port>" (DNS-rebinding guard)
  * Origin allowlist - absent (non-browser client) or an exact desktop origin; "null" rejected
  * Owner token     - Authorization: Bearer <token> (or X-AAS-Token) on every /api/v1 route except
                      /health; for WebSocket the token travels as subprotocol "aas.token.<token>"
                      (AAS identity: the AWS header/subprotocol are not accepted; token >= 32 chars)
  * Body limit      - Content-Length checked before reading; streamed bodies counted
"""

from __future__ import annotations

import hmac
import json
from collections.abc import Awaitable, Callable, Iterable
from typing import Any

Scope = dict[str, Any]
Receive = Callable[[], Awaitable[dict[str, Any]]]
Send = Callable[[dict[str, Any]], Awaitable[None]]
ASGIApp = Callable[[Scope, Receive, Send], Awaitable[None]]

PUBLIC_PATHS = {"/api/v1/health"}
MIN_TOKEN_CHARS = 32


def _headers(scope: Scope) -> dict[str, str]:
    return {k.decode("latin-1").lower(): v.decode("latin-1") for k, v in scope.get("headers", [])}


class BoundaryMiddleware:
    def __init__(self, app: ASGIApp, *, token: str, allowed_hosts: Iterable[str],
                 allowed_origins: Iterable[str], max_body: int, body_overrides: dict[str, int] | None = None) -> None:
        if len(token) < MIN_TOKEN_CHARS:
            raise ValueError(f"owner token must be at least {MIN_TOKEN_CHARS} characters")
        self.app = app
        self.body_overrides = dict(body_overrides or {})
        self.token = token.encode()
        self.hosts = set(allowed_hosts)
        self.origins = set(allowed_origins)
        self.max_body = max_body

    async def _reject(self, scope: Scope, send: Send, status: int, code: str) -> None:
        if scope["type"] == "websocket":
            await send({"type": "websocket.close", "code": 4400 + (status % 100)})
            return
        body = json.dumps({"error": code}).encode()
        await send({"type": "http.response.start", "status": status,
                    "headers": [(b"content-type", b"application/json"), (b"content-length", str(len(body)).encode()),
                                (b"cache-control", b"no-store"), (b"x-content-type-options", b"nosniff")]})
        await send({"type": "http.response.body", "body": body})

    def _token_ok(self, h: dict[str, str], scope: Scope) -> bool:
        candidates = []
        auth = h.get("authorization", "")
        if auth.lower().startswith("bearer "):
            candidates.append(auth[7:].strip())
        if "x-aas-token" in h:
            candidates.append(h["x-aas-token"].strip())
        if scope["type"] == "websocket":
            for proto in h.get("sec-websocket-protocol", "").split(","):
                proto = proto.strip()
                if proto.startswith("aas.token."):
                    candidates.append(proto[len("aas.token."):])
        return any(hmac.compare_digest(c.encode(), self.token) for c in candidates)

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        if scope["type"] not in ("http", "websocket"):
            return await self.app(scope, receive, send)
        h = _headers(scope)
        names = [k.decode("latin-1").lower() for k, _ in scope.get("headers", [])]
        if names.count("host") > 1 or names.count("origin") > 1:
            return await self._reject(scope, send, 400, "duplicate_host_or_origin")
        if h.get("host", "") not in self.hosts:
            return await self._reject(scope, send, 421, "host_not_allowed")
        origin = h.get("origin")
        if origin is not None and origin not in self.origins:
            return await self._reject(scope, send, 403, "origin_not_allowed")
        path = scope.get("path", "")
        if scope["type"] == "http" and scope.get("method") == "OPTIONS" and origin is not None:
            return await self.app(scope, receive, send)  # CORS preflight carries no token
        if path not in PUBLIC_PATHS and not self._token_ok(h, scope):
            return await self._reject(scope, send, 401, "unauthorized")
        if scope["type"] == "http":
            try:
                declared = int(h.get("content-length", "0") or 0)
            except ValueError:
                return await self._reject(scope, send, 400, "bad_content_length")
            limit = self.body_overrides.get(path, self.max_body)  # exact path match only
            if declared > limit:
                return await self._reject(scope, send, 413, "body_too_large")
            seen = 0

            async def limited_receive() -> dict[str, Any]:
                nonlocal seen
                msg = await receive()
                if msg["type"] == "http.request":
                    seen += len(msg.get("body", b""))
                    if seen > limit:
                        raise BodyTooLarge
                return msg
            try:
                return await self.app(scope, limited_receive, send)
            except BodyTooLarge:
                return await self._reject(scope, send, 413, "body_too_large")
        return await self.app(scope, receive, send)


class BodyTooLarge(Exception):
    pass
