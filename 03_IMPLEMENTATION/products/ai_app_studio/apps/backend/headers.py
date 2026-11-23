"""API response hygiene (security audit O6, O7).

- ``SecurityHeadersMiddleware``: every HTTP response of the API (success, studio error, boundary reject, 404, 422)
  gets ``X-Content-Type-Options: nosniff``, ``Cache-Control: no-store``, ``Referrer-Policy: no-referrer`` and
  ``Content-Security-Policy: default-src 'none'; frame-ancestors 'none'``. A header a route already set is kept.
  WebSocket scopes pass through unchanged.
- ``validation_error``: a request that fails schema validation gets a generic 422 with the offending field names only;
  the rejected input (pydantic's ``input`` / ``ctx``) is never echoed back.
"""
from __future__ import annotations

from typing import Any

from fastapi.exceptions import RequestValidationError
from starlette.requests import Request
from starlette.responses import JSONResponse
from starlette.types import ASGIApp, Message, Receive, Scope, Send

SECURITY_HEADERS: tuple[tuple[bytes, bytes], ...] = (
    (b"x-content-type-options", b"nosniff"),
    (b"cache-control", b"no-store"),
    (b"referrer-policy", b"no-referrer"),
    (b"content-security-policy", b"default-src 'none'; frame-ancestors 'none'"),
)
MAX_FIELDS = 20
MAX_FIELD_CHARS = 64


class SecurityHeadersMiddleware:
    def __init__(self, app: ASGIApp) -> None:
        self.app = app

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return

        async def send_with_headers(message: Message) -> None:
            if message["type"] == "http.response.start":
                headers = list(message.get("headers", []))
                present = {k.lower() for k, _ in headers}
                headers += [(k, v) for k, v in SECURITY_HEADERS if k not in present]
                message = {**message, "headers": headers}
            await send(message)

        await self.app(scope, receive, send_with_headers)


def _field(loc: Any) -> str | None:
    """The field name of a pydantic error location (``("body", "name")`` -> ``name``); plain identifiers only."""
    parts = [p for p in (loc or ()) if isinstance(p, str) and p not in ("body", "query", "path", "header")]
    if not parts:
        return None
    name = parts[0][:MAX_FIELD_CHARS]
    return name if name.replace("_", "").isalnum() and name.isascii() else "?"


async def validation_error(_r: Request, e: RequestValidationError) -> JSONResponse:
    fields: list[str] = []
    for err in e.errors():
        f = _field(err.get("loc"))
        if f and f not in fields:
            fields.append(f)
        if len(fields) >= MAX_FIELDS:
            break
    return JSONResponse({"error": "validation_failed", "message": "request validation failed",
                         "details": {"fields": fields}}, status_code=422)


def install(app: Any) -> None:
    """Register the 422 handler and add the header middleware. Call last: the middleware is then outermost, so the
    boundary's own rejects (401/403/421) carry the headers too."""
    app.add_exception_handler(RequestValidationError, validation_error)
    app.add_middleware(SecurityHeadersMiddleware)
