"""Static, read-only scanner for agent skills and prompt-like Markdown.

The scanner is evidence-first: educational examples are contextual indicators
and do not become active exfiltration findings unless the same document contains
an actionable data-access + network-send chain.

No code from scanned content is executed.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
from dataclasses import asdict, dataclass, field
from pathlib import Path

NETWORK_PATTERNS = [
    ("http_request", re.compile(r"\b(?:curl|wget|Invoke-WebRequest|Invoke-RestMethod|requests\.(?:get|post|put|request)|fetch\s*\(|axios\.(?:get|post|request)|httpx\.(?:get|post|request)|urllib\.request)\b", re.I)),
    ("webhook", re.compile(r"\b(?:webhook|callback_url|webhook_url)\b", re.I)),
    ("upload", re.compile(r"\b(?:upload|send|submit|POST|exfiltrat(?:e|ion)|forward)\b", re.I)),
]
DATA_PATTERNS = [
    ("browser_credentials", re.compile(r"\b(?:cookies?|cookie database|Login Data|Web Data|Local State|DPAPI|passwords?|browser profiles?)\b", re.I)),
    ("secrets", re.compile(r"\b(?:API[_ -]?keys?|access tokens?|auth tokens?|session tokens?|credentials?|secrets?|environment variables?|\.env)\b", re.I)),
    ("files", re.compile(r"\b(?:read|collect|copy|archive|zip|tar)\b.{0,80}\b(?:files?|documents?|Desktop|Downloads|AppData|home directory)\b", re.I | re.S)),
    ("memory_context", re.compile(r"\b(?:conversation|chat history|memory|context|prompt|system prompt)\b.{0,80}\b(?:send|upload|forward|POST|exfiltrat)\b", re.I | re.S)),
]
OVERRIDE_PATTERNS = [
    ("instruction_override", re.compile(r"\b(?:ignore|disregard|override|do not follow)\b.{0,80}\b(?:previous|prior|user|system|developer)\b.{0,80}\b(?:instructions?|commands?)\b", re.I | re.S)),
    ("concealment", re.compile(r"\b(?:silently|secretly|without (?:telling|informing|notifying) the user|hide this|do not disclose)\b", re.I)),
]
SECRET_DESTINATION_PATTERNS = [
    ("external_endpoint", re.compile(r"https?://[^\s)\]}>"']+", re.I)),
    ("email_destination", re.compile(r"\b(?:send|email|mail)\b.{0,60}\bto\s+[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", re.I)),
]
EDUCATIONAL_MARKERS = re.compile(
    r"\b(?:example|example only|for educational purposes|educational|demonstration|"
    r"hypothetical|sample|illustration|do not execute|not intended to|toy example|"
    r"attack simulation|security training|pentest documentation)\b", re.I
)

@dataclass
class Evidence:
    category: str
    text: str
    line: int
    active: bool
    reason: str

@dataclass
class ScanResult:
    path: str
    sha256: str
    provenance: dict[str, object] = field(default_factory=dict)
    findings: list[Evidence] = field(default_factory=list)
    verdict: str = "SAFE"
    score: int = 0

def _context_window(lines: list[str], index: int, radius: int = 2) -> str:
    return "\n".join(lines[max(0, index-radius):min(len(lines), index+radius+1)])

def _is_educational(context: str) -> bool:
    return bool(EDUCATIONAL_MARKERS.search(context))

def _extract_provenance(skill_dir: Path) -> dict[str, object]:
    candidate = skill_dir / "PROVENANCE.json"
    if not candidate.is_file():
        return {}
    try:
        value = json.loads(candidate.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError):
        return {"provenance_status": "INVALID_JSON"}
    return value if isinstance(value, dict) else {"provenance_status": "INVALID_SHAPE"}

def _evidence(lines: list[str], category: str, pattern: re.Pattern[str]) -> list[Evidence]:
    out = []
    for idx, line in enumerate(lines):
        if pattern.search(line):
            educational = _is_educational(_context_window(lines, idx))
            out.append(Evidence(category, line.strip()[:500], idx + 1, not educational,
                                "educational/example context" if educational else "instruction-like context"))
    return out

def scan_text(path: Path, text: str, provenance: dict[str, object] | None = None) -> ScanResult:
    lines = text.splitlines()
    findings = []
    for category, pattern in NETWORK_PATTERNS + DATA_PATTERNS + OVERRIDE_PATTERNS + SECRET_DESTINATION_PATTERNS:
        findings.extend(_evidence(lines, category, pattern))

    data_categories = {x[0] for x in DATA_PATTERNS}
    network_categories = {x[0] for x in NETWORK_PATTERNS + SECRET_DESTINATION_PATTERNS}
    override_categories = {x[0] for x in OVERRIDE_PATTERNS}
    active_data = {e.category for e in findings if e.active and e.category in data_categories}
    active_network = {e.category for e in findings if e.active and e.category in network_categories}
    active_override = {e.category for e in findings if e.active and e.category in override_categories}

    if active_data and active_network:
        verdict, score = "BLOCK", 90
    elif active_override and active_network:
        verdict, score = "REVIEW", 75
    elif active_override:
        verdict, score = "REVIEW", 55
    elif active_network or active_data:
        verdict, score = "REVIEW", 35
    else:
        verdict, score = "SAFE", 0

    return ScanResult(str(path), hashlib.sha256(text.encode("utf-8")).hexdigest(),
                      provenance or {}, findings, verdict, score)

def scan_path(path: Path) -> list[ScanResult]:
    if path.is_file():
        candidates = [path] if path.suffix.lower() in {".md", ".markdown", ".txt", ".prompt"} else []
    else:
        candidates = sorted(
            p for p in path.rglob("*")
            if p.is_file() and p.suffix.lower() in {".md", ".markdown", ".txt", ".prompt"}
            and ".git" not in p.parts and "node_modules" not in p.parts
        )
    results = []
    for candidate in candidates:
        try:
            text = candidate.read_text(encoding="utf-8")
        except (OSError, UnicodeDecodeError):
            continue
        results.append(scan_text(candidate, text, _extract_provenance(candidate.parent)))
    return results

def main() -> int:
    parser = argparse.ArgumentParser(description="Read-only skill/prompt exfiltration scanner")
    parser.add_argument("path", type=Path)
    parser.add_argument("--json", action="store_true", dest="json_output")
    args = parser.parse_args()
    results = scan_path(args.path)
    payload = [asdict(result) for result in results]
    if args.json_output:
        print(json.dumps(payload, ensure_ascii=False, indent=2))
    else:
        for result in results:
            print(f"{result.verdict:6} {result.score:3} {result.path} {result.sha256}")
            for finding in result.findings:
                marker = "ACTIVE" if finding.active else "CONTEXT"
                print(f"  [{marker}] L{finding.line} {finding.category}: {finding.text}")
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
