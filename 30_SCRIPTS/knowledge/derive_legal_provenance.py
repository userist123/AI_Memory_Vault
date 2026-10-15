"""Derive a `provenance` mapping for the legal notes that have none.

Why: the pack builder rejects at egress any result without a provenance
mapping (the egress contract requires provenance on every model-facing
result, and the builder never fabricates it). 27 floor-passing notes --
the 8 legal acts, their 8 indexes, 10 atomic obligations and one MOC --
carried the source identity in ad-hoc keys (`official_url`, `source_file`,
`sha256_hash`, `source_act`) but no `provenance` block, so they could
never be shown to an agent at any rank. Measured in
07_EVALUATION/reranker_envelope/DEVIATIONS.md (D-2).

What is derived, and from what (nothing is invented):

* legal_source   -> source_type `official`, source_ref = the note's own
                    `official_url`, source_date = `publication_date`,
                    original_path = `source_file`. provenance_status is
                    `complete` only if that source file exists on disk and
                    its sha256 equals the declared `sha256_hash`.
* legal_index,
  legal_atomic_obligation
                 -> source_type `ai` (agent-written analyses of the act,
                    `status: requires_legal_review`), source_ref = the
                    parent act's `official_url`, resolved through the
                    `source_act` wikilink; original_path = the parent note.
* moc (navigation map) -> source_type `user`, source_ref = the commit that
                    added it (owner-authored, see `git log --diff-filter=A`).

The block is inserted textually after the `verification:` line (after
`category:` for the MOC), so every other frontmatter byte is preserved.
The result is re-parsed and checked against the lifecycle schema
(required keys, enums, no extra keys). Dry-run by default; `--apply` writes.
"""
from __future__ import annotations

import argparse
import hashlib
import re
import sys
from pathlib import Path

import yaml

REPO = Path(__file__).resolve().parents[2]
LEGAL = REPO / "01_ARCHITECTURE" / "knowledge" / "legal"
PRIMARY, INDEXES, ATOMIC = LEGAL / "primary", LEGAL / "legal_indexes", LEGAL / "atomic"
MOC = REPO / "01_ARCHITECTURE" / "graphs" / "02 Memory Knowledge Map.md"
MOC_ADDED_IN = "git:2612ee92d"  # refactor: complete core tree migration, 2026-09-04, owner

SOURCE_TYPES = {"user", "official", "execution", "experience", "ai", "inference", "import", "unknown"}
ALLOWED_KEYS = {"source_type", "source_ref", "source_date", "original_path", "extraction_date", "redaction", "provenance_status"}
DATE = re.compile(r"^\d{4}-\d{2}-\d{2}$")
WIKILINK = re.compile(r"^\[\[([^\]|]+)")
ORDER = ("source_type", "source_ref", "source_date", "original_path", "redaction", "provenance_status")
BARE = {"source_type", "redaction", "provenance_status"}


def split_frontmatter(text: str) -> tuple[str, str]:
    if not text.startswith("---"):
        raise ValueError("no frontmatter")
    _, fm, body = text.split("---", 2)
    return fm, body


def quoted(value: object) -> str:
    return '"' + str(value).replace('"', '\\"') + '"'


def render(prov: dict) -> list[str]:
    lines = ["provenance:"]
    for key in ORDER:
        if key in prov:
            value = prov[key] if key in BARE else quoted(prov[key])
            lines.append(f"  {key}: {value}")
    return lines


def check(prov: dict) -> None:
    extra = set(prov) - ALLOWED_KEYS
    if extra:
        raise ValueError(f"extra provenance keys {extra}")
    if not {"source_type", "source_ref"} <= set(prov):
        raise ValueError("source_type and source_ref are required")
    if prov["source_type"] not in SOURCE_TYPES:
        raise ValueError(f"bad source_type {prov['source_type']}")
    if "source_date" in prov and not DATE.match(str(prov["source_date"])):
        raise ValueError(f"bad source_date {prov['source_date']}")
    if prov.get("provenance_status") not in (None, "complete", "incomplete"):
        raise ValueError("bad provenance_status")


def for_legal_source(fm: dict) -> dict:
    url = fm.get("official_url")
    if not url:
        raise ValueError("legal_source without official_url")
    prov = {"source_type": "official", "source_ref": url, "redaction": "none"}
    if DATE.match(str(fm.get("publication_date", ""))):
        prov["source_date"] = str(fm["publication_date"])
    src = fm.get("source_file")
    status = "incomplete"
    if src:
        prov["original_path"] = src
        p = REPO / src
        if p.is_file():
            digest = hashlib.sha256(p.read_bytes()).hexdigest()
            status = "complete" if digest == str(fm.get("sha256_hash", "")).lower() else "incomplete"
    prov["provenance_status"] = status
    return prov


def for_derived(fm: dict, parents: dict[str, tuple[Path, dict]]) -> dict:
    m = WIKILINK.match(str(fm.get("source_act", "")))
    if not m or m.group(1) not in parents:
        raise ValueError(f"source_act {fm.get('source_act')!r} does not resolve to a legal_source note")
    ppath, pfm = parents[m.group(1)]
    return {
        "source_type": "ai",
        "source_ref": pfm["official_url"],
        "original_path": ppath.relative_to(REPO).as_posix(),
        "redaction": "none",
        "provenance_status": "complete",
    }


def insert_block(text: str, block: list[str], after_key: str) -> str:
    fm, body = split_frontmatter(text)
    if re.search(r"^provenance:", fm, re.M):
        raise ValueError("already has provenance")
    lines = fm.split("\n")
    for i, line in enumerate(lines):
        if line.startswith(after_key + ":"):
            j = i + 1
            while j < len(lines) and (lines[j].startswith(" ") or lines[j].startswith("-")):
                j += 1  # step over a nested block or list under the anchor key
            lines[j:j] = block
            return "---" + "\n".join(lines) + "---" + body
    raise ValueError(f"no `{after_key}:` line to anchor on")


def build_plan() -> list[tuple[Path, dict, str]]:
    parents: dict[str, tuple[Path, dict]] = {}
    for p in sorted(PRIMARY.glob("*.md")):
        parents[p.stem] = (p, yaml.safe_load(split_frontmatter(p.read_text(encoding="utf-8"))[0]) or {})
    plan: list[tuple[Path, dict, str]] = []
    for p in sorted(PRIMARY.glob("*.md")):
        plan.append((p, for_legal_source(parents[p.stem][1]), "verification"))
    for p in sorted(list(INDEXES.glob("*.md")) + list(ATOMIC.glob("*.md"))):
        fm = yaml.safe_load(split_frontmatter(p.read_text(encoding="utf-8"))[0]) or {}
        plan.append((p, for_derived(fm, parents), "verification"))
    plan.append((MOC, {"source_type": "user", "source_ref": MOC_ADDED_IN, "redaction": "none",
                       "provenance_status": "complete"}, "category"))
    return plan


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="write the files (default: dry-run)")
    args = ap.parse_args()

    written = 0
    plan = build_plan()
    for path, prov, anchor in plan:
        check(prov)
        text = path.read_text(encoding="utf-8")
        new_text = insert_block(text, render(prov), anchor)
        before = yaml.safe_load(split_frontmatter(text)[0]) or {}
        after = yaml.safe_load(split_frontmatter(new_text)[0]) or {}
        if after.get("provenance") != prov:
            raise RuntimeError(f"{path}: re-parsed provenance differs from the plan")
        if {k: v for k, v in after.items() if k != "provenance"} != before:
            raise RuntimeError(f"{path}: a field other than provenance changed")
        print(f"{'WRITE' if args.apply else 'PLAN '} {after.get('id'):40} {prov['source_type']:8} "
              f"{prov['provenance_status']:10} {prov['source_ref'][:70]}")
        if args.apply:
            path.write_bytes(new_text.encode("utf-8"))
            written += 1
    print(f"{len(plan)} notes planned, {written} written ({'apply' if args.apply else 'dry-run'})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
