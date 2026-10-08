"""Last-line redaction of secrets in text leaving the vault. Counts are audited, text is not."""
from __future__ import annotations

import re
from typing import Dict, Tuple

_PATTERNS = (
    ("private_key", re.compile(r"-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z0-9 ]*PRIVATE KEY-----")),
    ("aws_key", re.compile(r"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b")),
    ("github_token", re.compile(r"\b(?:gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{60,})\b")),
    ("openai_key", re.compile(r"\bsk-(?:proj-|ant-)?[A-Za-z0-9_-]{24,}\b")),
    ("slack_token", re.compile(r"\bxox[abposr]-[A-Za-z0-9-]{10,}\b")),
    ("telegram_token", re.compile(r"\b\d{8,10}:[A-Za-z0-9_-]{35}\b")),
    ("jwt", re.compile(r"\beyJ[A-Za-z0-9_-]{8,}\.eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b")),
    ("iban_ro", re.compile(r"\bRO\d{2}[A-Z]{4}[0-9A-Z]{16}\b")),
    ("assignment", re.compile(r"(?i)\b(password|passwd|api[_-]?key|secret|token)\s*[:=]\s*['\"]?[^\s'\"]{12,}")),
)
_CNP = re.compile(r"\b[1-8]\d{12}\b")
_CNP_WEIGHTS = (2, 7, 9, 1, 4, 6, 3, 5, 8, 2, 7, 9)


def _valid_cnp(value: str) -> bool:
    total = sum(int(d) * w for d, w in zip(value[:12], _CNP_WEIGHTS))
    check = total % 11
    return (1 if check == 10 else check) == int(value[12])


def redact(text: str) -> Tuple[str, Dict[str, int]]:
    counts: Dict[str, int] = {}
    for name, pattern in _PATTERNS:
        def _sub(match, _name=name):
            counts[_name] = counts.get(_name, 0) + 1
            if _name == "assignment":
                return f"{match.group(1)}=[REDACTED:{_name}]"
            return f"[REDACTED:{_name}]"
        text = pattern.sub(_sub, text)

    def _cnp(match):
        if _valid_cnp(match.group(0)):
            counts["cnp"] = counts.get("cnp", 0) + 1
            return "[REDACTED:cnp]"
        return match.group(0)
    text = _CNP.sub(_cnp, text)
    return text, counts
