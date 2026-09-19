"""Cut committed quotes from copyrighted works down to the quota, keeping them verifiable.

Grounding checks quote the source verbatim, which is how a fabricated concept is
caught. That evidence still has to live in a public repository, so it is kept
short: each quote is truncated at a word boundary to the per-quote limit, and the
full quote's SHA-256 and length are recorded beside it. A truncated quote is
still a prefix of the source sentence, so `verify_agent_submission` keeps
working, and the hash still proves which sentence was seen.

    python 30_SCRIPTS/ingestion/redact_long_quotes.py --check
    python 30_SCRIPTS/ingestion/redact_long_quotes.py --apply
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
POLICY = REPO / "10_DOCUMENTATION" / "procedures" / "Quoting_From_Copyrighted_Sources.md"
QUOTE_FIELDS = ("evidence_quote", "evidence", "quote", "source_quote", "target_quote")
MAX_QUOTE_CHARS = 180


def truncate(text: str, limit: int = MAX_QUOTE_CHARS) -> str:
    """Cut at the last word boundary within the limit, and say it was cut."""
    if len(text) <= limit:
        return text
    head = text[: limit - 1]
    if " " in head:
        head = head[: head.rindex(" ")]
    return head.rstrip(" ,;:.—-") + "…"


def redact_row(row: dict) -> tuple[dict, int]:
    changed = 0
    out = dict(row)
    for field in QUOTE_FIELDS:
        value = row.get(field)
        if not isinstance(value, str) or len(value) <= MAX_QUOTE_CHARS:
            continue
        out[field] = truncate(value)
        out[f"{field}_sha256"] = hashlib.sha256(value.encode("utf-8")).hexdigest()
        out[f"{field}_chars"] = len(value)
        changed += 1
    return out, changed


def over_quota(path: Path) -> list[tuple[int, str, int]]:
    """(row index, field, length) for every committed quote above the limit."""
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return []
    rows = data if isinstance(data, list) else data.get("candidates") or data.get("rows") or []
    found = []
    for i, row in enumerate(rows):
        if not isinstance(row, dict):
            continue
        for field in QUOTE_FIELDS:
            value = row.get(field)
            if isinstance(value, str) and len(value) > MAX_QUOTE_CHARS:
                found.append((i, field, len(value)))
    return found


def redact_file(path: Path) -> int:
    data = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(data, list):
        raise SystemExit(f"{path}: only a top-level list of rows is redacted here")
    rows, changed = [], 0
    for row in data:
        new_row, n = redact_row(row) if isinstance(row, dict) else (row, 0)
        rows.append(new_row)
        changed += n
    if changed:
        path.write_text(json.dumps(rows, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    return changed


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="rewrite the files instead of only reporting")
    ap.add_argument("paths", nargs="*", type=Path)
    args = ap.parse_args()

    paths = [Path(p).resolve() for p in args.paths] or sorted((REPO / "07_EVALUATION").rglob("*candidates*.json"))
    total = 0
    for path in paths:
        hits = over_quota(path)
        if not hits:
            continue
        total += len(hits)
        rel = path.relative_to(REPO)
        print(f"{rel}: {len(hits)} quotes over {MAX_QUOTE_CHARS} chars (longest {max(h[2] for h in hits)})")
        if args.apply:
            print(f"  redacted {redact_file(path)} quotes")
    if not total:
        print(f"No committed quote exceeds {MAX_QUOTE_CHARS} characters.")
        return 0
    if not args.apply:
        print(f"\n{total} quotes exceed the quota. See {POLICY.relative_to(REPO)}. Re-run with --apply.")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
