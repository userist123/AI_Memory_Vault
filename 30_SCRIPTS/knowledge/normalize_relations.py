"""Normalize `relations:` frontmatter into the form the runtime actually reads.

`SynapseStore.from_index()` accepts a relation only when it is a mapping with a
`target_id` that resolves to a note id in the index:

    target = rel.get("target_id")
    if not target or target not in index.by_id: continue

Much of this vault instead carries bare strings holding file paths, e.g.

    relations:
      - "00_CORE/Identity.md"

Those are invisible to the runtime twice over: they are not mappings, and the
paths do not exist (`00_CORE` is not a directory in this repository). This
script rewrites each resolvable string into

    relations:
      - type: related_to
        target_id: <resolved note id>

resolving by file name through `VaultIndex`. Entries that cannot be resolved
are LEFT UNTOUCHED and reported, never deleted: some point at notes that exist
on disk outside the four roots `VaultIndex` indexes, so dropping them would
destroy valid links rather than clean up false ones.

Run with --apply to write; default is a dry run.
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "03_IMPLEMENTATION" / "packages"))
from retrieval.vault_index import VaultIndex, DEFAULT_ROOTS  # noqa: E402

REL_BLOCK = re.compile(r"^relations:[ \t]*\n((?:[ \t]*-[^\n]*\n|[ \t]{2,}[^\n]*\n)*)", re.M)
DICT_KEY = re.compile(r"^(relation|type|target|target_id|weight|note|source|evidence)\s*:", re.I)
WIKILINK_REF = re.compile(r"^\[\[([^\]\|#]+)")
STRING_ITEM = re.compile(r"^[ \t]*-[ \t]*[\"']?([^\"'\n#][^\"'\n]*?)[\"']?[ \t]*$")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=".")
    ap.add_argument("--apply", action="store_true")
    args = ap.parse_args()

    root = Path(args.root).resolve()
    index = VaultIndex.load(root, include_raw=True, include_archived=True)

    files = converted = dropped = already = skipped_dict = 0
    for rel_root in DEFAULT_ROOTS:
        base = root / rel_root
        if not base.exists():
            continue
        for path in sorted(base.rglob("*.md")):
            try:
                text = path.read_text(encoding="utf-8", errors="ignore")
            except OSError:
                continue
            m = REL_BLOCK.search(text)
            if not m:
                continue
            body, indent_out, changed = m.group(1), "  ", False
            new_items: list[str] = []
            for line in body.splitlines():
                if "target_id" in line or not line.strip():
                    if line.strip():
                        new_items.append(line)
                        already += 1
                    continue
                sm = STRING_ITEM.match(line)
                if not sm:
                    new_items.append(line)
                    continue
                ref = sm.group(1).strip()
                # `- relation: implements` is a mapping key, not a string entry.
                # Such relations are already dicts; they simply use the legacy
                # `relation:`/`target:` spelling instead of `type:`/`target_id:`
                # and are handled separately, not rewritten here.
                if DICT_KEY.match(ref):
                    new_items.append(line)
                    skipped_dict += 1
                    continue
                # Refs are sometimes written as wikilinks inside the block.
                wl = WIKILINK_REF.match(ref)
                if wl:
                    ref = wl.group(1).strip()
                stem = Path(ref).stem if ("/" in ref or ref.endswith(".md")) else ref
                note = index.resolve(stem)
                if note is None:
                    new_items.append(line)   # keep: unresolved here != nonexistent
                    dropped += 1
                    continue
                new_items.append(f"{indent_out}- type: related_to")
                new_items.append(f"{indent_out}  target_id: {note.id}")
                converted += 1
                changed = True
            if not changed:
                continue
            files += 1
            if args.apply:
                block = "relations:\n" + ("\n".join(new_items) + "\n" if new_items else "")
                if not new_items:
                    block = "relations: []\n"
                path.write_text(text[:m.start()] + block + text[m.end():], encoding="utf-8")

    mode = "APPLIED" if args.apply else "DRY RUN"
    print(f"[{mode}] fisiere atinse: {files} | convertite: {converted} | "
          f"nerezolvate (pastrate): {dropped} | deja corecte: {already} | "
          f"dict legacy (relation:/target:): {skipped_dict}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
