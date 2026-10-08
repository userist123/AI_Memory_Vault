"""Stable error codes shared by every adapter (MCP, CLI, Ollama, Telegram, export)."""
from __future__ import annotations

from enum import Enum


class ErrorCode(str, Enum):
    OK = "OK"
    NOT_FOUND = "NOT_FOUND"                      # no such route, or no such section
    AMBIGUOUS = "AMBIGUOUS"                      # several candidates; see `candidates`
    INVALID_URI = "INVALID_URI"                  # malformed vault:// URI or forbidden characters
    INVALID_ARGUMENT = "INVALID_ARGUMENT"        # bad parameter (range, limit, type)
    DENIED_POLICY = "DENIED_POLICY"              # domain not allowed for the principal
    DENIED_CLASSIFICATION = "DENIED_CLASSIFICATION"  # classification above effective clearance
    DENIED_TRUST = "DENIED_TRUST"                # trust level not allowed (untrusted, archived)
    OUT_OF_ROOT = "OUT_OF_ROOT"                  # resolved path escapes its base, or a link
    STALE_REF = "STALE_REF"                      # ?v=<sha12> no longer matches the file
    TOO_LARGE = "TOO_LARGE"                      # file over the routing limit
    DECODE_ERROR = "DECODE_ERROR"                # not valid UTF-8; nothing is guessed
    BACKEND_UNAVAILABLE = "BACKEND_UNAVAILABLE"  # memory search backend not available
    CONTEXT_TRUNCATED = "CONTEXT_TRUNCATED"      # local model context overflowed
    UNVERIFIED_ANSWER = "UNVERIFIED_ANSWER"      # model answer failed citation verification
    INTERNAL = "INTERNAL"                        # details only in the audit log

    @property
    def exit_code(self) -> int:
        if self is ErrorCode.OK:
            return 0
        if self in (ErrorCode.NOT_FOUND, ErrorCode.AMBIGUOUS):
            return 2
        if self.name.startswith("DENIED") or self is ErrorCode.OUT_OF_ROOT:
            return 3
        return 1


class VaultAccessError(Exception):
    def __init__(self, code: ErrorCode, message: str, **extra):
        super().__init__(message)
        self.code = code
        self.message = message
        self.extra = extra
