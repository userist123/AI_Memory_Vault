import os
import re
import json
import sys
from pathlib import Path
from collections import defaultdict

VAULT_ROOT = Path(".")
EXCLUDE_DIRS = {".git", ".obsidian", "10_ARCHIVE", "node_modules", "__pycache__", "RAW_IMPORTS"}
MAX_LINKS_PER_NOTE = 8

GENERIC_STEMS = {"readme", "index", "original_request", "project", "test_infra", "test_ready"}

def find_markdown_files():
    files = []
    for root, dirs, filenames in os.walk(VAULT_ROOT):
        dirs[:] = [d for d in dirs if d not in EXCLUDE_DIRS and not d.startswith(".")]
        for fn in filenames:
            if fn.endswith(".md"):
                files.append(Path(root) / fn)
    return files

def extract_frontmatter_tags(content):
    tags = set()
    fm_match = re.match(r"^---\n(.*?)\n---", content, re.DOTALL)
    if fm_match:
        fm = fm_match.group(1)
        tag_match = re.search(r"tags:\s*\[(.*?)\]", fm)
        if tag_match:
            tags.update(t.strip().strip('"\'').lower() for t in tag_match.group(1).split(",") if t.strip())
    inline_tags = re.findall(r"(?:^|\s)#([a-zA-Z0-9_-]+)", content)
    tags.update(t.lower() for t in inline_tags if len(t) > 2)
    return tags

def extract_title_keywords(filename):
    name = filename.stem.lower()
    parts = re.split(r"[-_\s]+", name)
    stop_words = {"this", "that", "with", "from", "your", "what", "when", "where", "which", "about", "into", "over", "after", "readme", "index"}
    return set(p for p in parts if len(p) > 3 and p not in stop_words)

def existing_links(content):
    return set(re.findall(r"\[\[([^\]|#]+)", content))

def build_index(files):
    index = {}
    for f in files:
        try:
            content = f.read_text(encoding="utf-8", errors="ignore")
        except Exception:
            continue
        index[f] = {
            "tags": extract_frontmatter_tags(content),
            "keywords": extract_title_keywords(f),
            "folder": str(f.parent),
            "existing_links": existing_links(content),
            "content": content,
        }
    return index

def score_relation(a, b):
    score = 0
    shared_tags = a["tags"] & b["tags"]
    score += len(shared_tags) * 3
    shared_keywords = a["keywords"] & b["keywords"]
    score += len(shared_keywords) * 2
    if a["folder"] == b["folder"] and a["folder"] != ".":
        score += 1
    return score

def propose_links(index):
    proposals = defaultdict(list)
    files = list(index.keys())
    
    # Invert index for fast candidate retrieval
    tag_to_files = defaultdict(set)
    kw_to_files = defaultdict(set)
    folder_to_files = defaultdict(set)
    
    for f, data in index.items():
        for t in data["tags"]:
            tag_to_files[t].add(f)
        for kw in data["keywords"]:
            kw_to_files[kw].add(f)
        folder_to_files[data["folder"]].add(f)
        
    for fa in files:
        data_a = index[fa]
        candidates = set()
        for t in data_a["tags"]:
            candidates.update(tag_to_files[t])
        for kw in data_a["keywords"]:
            candidates.update(kw_to_files[kw])
        candidates.update(folder_to_files[data_a["folder"]])
        candidates.discard(fa)
        
        scored = []
        for fb in candidates:
            # Skip if already linked
            if fb.stem in data_a["existing_links"]:
                continue
            # Skip same stem or generic stems matching
            if fb.stem.lower() == fa.stem.lower() or fb.stem.lower() in GENERIC_STEMS:
                continue
            s = score_relation(data_a, index[fb])
            if s >= 2:
                scored.append((s, fb))
                
        scored.sort(key=lambda x: -x[0])
        
        # Deduplicate targets by stem to never have duplicate [[Link]] in same note
        unique_targets = []
        seen_stems = set()
        for _, fb in scored:
            if fb.stem not in seen_stems:
                seen_stems.add(fb.stem)
                unique_targets.append(fb.stem)
                if len(unique_targets) >= MAX_LINKS_PER_NOTE:
                    break
                    
        proposals[fa] = unique_targets
        
    return proposals

def write_report(proposals, out_path="scripts/auto_link_report.md"):
    lines = ["# Auto-Link Proposal Report\n"]
    total_links = 0
    active_files = 0
    for f, targets in proposals.items():
        if not targets:
            continue
        active_files += 1
        lines.append(f"## {f}")
        for t in targets:
            lines.append(f"- [[{t}]]")
            total_links += 1
        lines.append("")
    lines.insert(1, f"**Total files analyzed:** {len(proposals)}\n**Files with proposed links:** {active_files}\n**Total proposed links:** {total_links}\n")
    Path(out_path).parent.mkdir(parents=True, exist_ok=True)
    Path(out_path).write_text("\n".join(lines), encoding="utf-8")
    print(f"Report written to {out_path} ({total_links} proposed links across {active_files} files)")

def apply_links(index, proposals):
    applied_count = 0
    for f, targets in proposals.items():
        if not targets:
            continue
        content = index[f]["content"]
        if "## Related" in content:
            continue  # do not duplicate if already has a Related section
        related_block = "\n\n## Related\n" + "\n".join(f"- [[{t}]]" for t in targets) + "\n"
        f.write_text(content.rstrip() + related_block, encoding="utf-8")
        applied_count += 1
    print(f"Applied links to {applied_count} files")

if __name__ == "__main__":
    files = find_markdown_files()
    print(f"Found {len(files)} markdown files in vault (excluding archive & raw imports).")
    index = build_index(files)
    proposals = propose_links(index)
    write_report(proposals)
    if "--apply" in sys.argv:
        apply_links(index, proposals)
        print("APPLIED. Commit these changes explicitly with a clear message.")
    else:
        print("DRY RUN ONLY. Review scripts/auto_link_report.md, then re-run with --apply.")
