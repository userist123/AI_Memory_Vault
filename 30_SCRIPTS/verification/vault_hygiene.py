#!/usr/bin/env python3
"""30_SCRIPTS/verification/vault_hygiene.py — Vault Corpus Hygiene Auditor and Dry-Run Tool.

Evaluates canonical Vault notes for quality, structural health, and taxonomy compliance.
Produces:
  - total nodes
  - lifecycle distribution
  - type distribution
  - verification distribution
  - relation count
  - resolvable target count
  - duplicate count
  - boilerplate count
  - stub count
  - low-information count
  - orphan-review count
  - keep count

Categories:
  - boilerplate
  - duplicate
  - stub
  - low_information
  - orphan_review
  - keep

pply is reversible and writes only:
  lifecycle: ARCHIVED
  archived_reason: <reason>

Supports:
  python 30_SCRIPTS/verification/vault_hygiene.py report
  python 30_SCRIPTS/verification/vault_hygiene.py apply --dry-run
"""
from __future__ import annotations

import argparse
import hashlib
import os
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any, Dict, List, Set, Tuple

import yaml

REPO_ROOT = Path(__file__).resolve().parents[2]

CANONICAL_FOLDERS = [
    "00_GOVERNANCE",
    "01_ARCHITECTURE",
    "02_PRODUCT",
    "04_CONFIG",
    "10_DOCUMENTATION",
]

EXCLUDE_DIRS = {
    ".git",
    ".obsidian",
    "06_INBOX",
    "07_EVALUATION",
    "08_OBSERVABILITY",
    "20_TESTS",
    "80_ARCHIVE",
    "projects",
    "xau_kinetic",
    "XAU_Kinetic_Standalone",
    "XAU_Kinetic.Desktop",
    "AI_Memory_Vault_OBSIDIAN",
    "node_modules",
    "__pycache__",
    "Artifacts",
}

BOILERPLATE_PATTERNS = [
    re.compile(r"^\s*#\s+Template\b", re.I),
    re.compile(r"Fill out this template", re.I),
    re.compile(r"<!--\s*insert content here\s*-->", re.I),
    re.compile(r"\[TODO:\s*add description\]", re.I),
]

def parse_frontmatter(content: str) -> Tuple[Dict[str, Any], str]:
    match = re.match(r'^---\r?\n(.*?)\r?\n---\r?\n?(.*)', content, re.DOTALL)
    if not match:
        return {}, content
    yaml_text = match.group(1)
    body = match.group(2)
    try:
        data = yaml.safe_load(yaml_text)
        if isinstance(data, dict):
            return data, body
    except Exception:
        pass
    return {}, content

class NodeRecord:
    def __init__(self, path: Path, rel_path: Path, frontmatter: Dict[str, Any], body: str):
        self.path = path
        self.rel_path = rel_path
        self.fm = frontmatter
        self.body = body.strip()
        self.node_id = str(frontmatter.get("id") or rel_path.stem)
        self.lifecycle = str(frontmatter.get("lifecycle") or "UNKNOWN").upper()
        self.type = str(frontmatter.get("type") or "unknown").lower()
        self.verification = str(frontmatter.get("verification") or "unverified").lower()
        
        # Relations: frontmatter links + inline wikilinks
        self.out_relations: List[str] = []
        rel_field = frontmatter.get("relations")
        if isinstance(rel_field, list):
            for r in rel_field:
                if isinstance(r, dict) and "target" in r:
                    self.out_relations.append(str(r["target"]))
                elif isinstance(r, str):
                    self.out_relations.append(r)
        
        for match in re.finditer(r'\[\[([^\]|#]+)(?:[|#][^\]]*)?\]\]', body):
            self.out_relations.append(match.group(1).strip())
            
        self.category = "keep"
        self.category_reason = ""

class CorpusHygieneAuditor:
    def __init__(self, root: Path = REPO_ROOT):
        self.root = root
        self.nodes: Dict[str, NodeRecord] = {}
        self.path_to_node: Dict[Path, NodeRecord] = {}
        self.id_to_nodes = defaultdict(list)
        self.content_hashes = defaultdict(list)
        
    def scan(self) -> None:
        for folder in CANONICAL_FOLDERS:
            folder_path = self.root / folder
            if not folder_path.exists():
                continue
            for p in folder_path.rglob("*.md"):
                if any(ex in p.parts for ex in EXCLUDE_DIRS):
                    continue
                try:
                    text = p.read_text(encoding="utf-8", errors="ignore")
                    fm, body = parse_frontmatter(text)
                    rel = p.relative_to(self.root)
                    rec = NodeRecord(p, rel, fm, body)
                    
                    self.nodes[rec.node_id] = rec
                    self.path_to_node[p] = rec
                    self.id_to_nodes[rec.node_id].append(rec)
                    
                    # Content hash of normalized body for duplicate detection
                    norm_body = re.sub(r'\s+', ' ', body.strip())
                    if len(norm_body) > 30:
                        h = hashlib.sha256(norm_body.encode("utf-8")).hexdigest()
                        self.content_hashes[h].append(rec)
                except Exception:
                    pass

    def categorize(self) -> None:
        # 1. Identify duplicates
        for h, recs in self.content_hashes.items():
            if len(recs) > 1:
                # Keep first, mark others as duplicate
                for dup in recs[1:]:
                    if dup.category == "keep":
                        dup.category = "duplicate"
                        dup.category_reason = f"Duplicate content of {recs[0].rel_path}"

        for rec in self.path_to_node.values():
            if rec.category != "keep":
                continue
                
            body = rec.body
            norm_body = re.sub(r'#+\s*', '', body).strip()
            
            # 2. Boilerplate check
            if any(p.search(body) for p in BOILERPLATE_PATTERNS):
                rec.category = "boilerplate"
                rec.category_reason = "Contains template boilerplate markers"
                continue
                
            # 3. Stub check (< 50 chars of meaningful body)
            if len(norm_body) < 50:
                rec.category = "stub"
                rec.category_reason = f"Stub content ({len(norm_body)} chars < 50 chars)"
                continue
                
            # 4. Low-information check (< 120 chars without substantive facts)
            if len(norm_body) < 120 and len(rec.out_relations) == 0:
                rec.category = "low_information"
                rec.category_reason = f"Low information density ({len(norm_body)} chars with 0 relations)"
                continue
                
            # 5. Orphan review check (lifecycle: REVIEW with 0 in/out relations)
            if rec.lifecycle == "REVIEW" and len(rec.out_relations) == 0:
                rec.category = "orphan_review"
                rec.category_reason = "Unlinked node in REVIEW lifecycle with zero inbound/outbound edges"
                continue

    def get_summary(self) -> Dict[str, Any]:
        total_nodes = len(self.path_to_node)
        lifecycles = Counter(r.lifecycle for r in self.path_to_node.values())
        types = Counter(r.type for r in self.path_to_node.values())
        verifications = Counter(r.verification for r in self.path_to_node.values())
        categories = Counter(r.category for r in self.path_to_node.values())
        
        all_target_names = set(self.nodes.keys())
        all_target_names.update(p.stem for p in self.path_to_node.keys())
        all_target_names.update(p.name for p in self.path_to_node.keys())
        
        total_relations = 0
        resolvable_relations = 0
        for r in self.path_to_node.values():
            for target in r.out_relations:
                total_relations += 1
                clean_target = target.replace(".md", "").strip()
                if clean_target in all_target_names:
                    resolvable_relations += 1
                    
        return {
            "total_nodes": total_nodes,
            "lifecycles": dict(lifecycles.most_common()),
            "types": dict(types.most_common()),
            "verifications": dict(verifications.most_common()),
            "categories": dict(categories.most_common()),
            "relation_count": total_relations,
            "resolvable_target_count": resolvable_relations,
            "duplicate_count": categories.get("duplicate", 0),
            "boilerplate_count": categories.get("boilerplate", 0),
            "stub_count": categories.get("stub", 0),
            "low_information_count": categories.get("low_information", 0),
            "orphan_review_count": categories.get("orphan_review", 0),
            "keep_count": categories.get("keep", 0),
        }

    def print_report(self) -> None:
        summary = self.get_summary()
        print("=" * 80)
        print("                  VAULT CORPUS HYGIENE REPORT (P0.3)                    ")
        print("================================================================================")
        print(f"Total Nodes Analyzed:          {summary['total_nodes']}")
        print(f"Total Relations / Edges:       {summary['relation_count']}")
        print(f"Resolvable Target Count:       {summary['resolvable_target_count']} ({summary['resolvable_target_count']/max(summary['relation_count'],1)*100:.1f}%)")
        print("-" * 80)
        print("LIFECYCLE DISTRIBUTION:")
        for k, v in summary["lifecycles"].items():
            print(f"  {k:<20} {v:>5} ({v/summary['total_nodes']*100:5.1f}%)")
        print("-" * 80)
        print("TYPE DISTRIBUTION (Top 10):")
        for k, v in list(summary["types"].items())[:10]:
            print(f"  {k:<20} {v:>5} ({v/summary['total_nodes']*100:5.1f}%)")
        print("-" * 80)
        print("VERIFICATION DISTRIBUTION:")
        for k, v in summary["verifications"].items():
            print(f"  {k:<20} {v:>5} ({v/summary['total_nodes']*100:5.1f}%)")
        print("-" * 80)
        print("QUALITY HYGIENE CATEGORIZATION:")
        for cat in ["keep", "duplicate", "boilerplate", "stub", "low_information", "orphan_review"]:
            cnt = summary["categories"].get(cat, 0)
            print(f"  {cat:<20} {cnt:>5} ({cnt/summary['total_nodes']*100:5.1f}%)")
        print("=" * 80)

    def print_dry_run(self) -> None:
        print("================================================================================")
        print("               VAULT CORPUS HYGIENE — APPLY (DRY-RUN ONLY)                      ")
        print("================================================================================")
        print("SIMULATION MODE: No files modified on disk.")
        print("Plan: Target nodes will have frontmatter updated with:")
        print("  lifecycle: ARCHIVED")
        print("  archived_reason: <reason>")
        print("-" * 80)
        
        archived_candidates = [r for r in self.path_to_node.values() if r.category != "keep"]
        print(f"Total Candidate Nodes for Archival: {len(archived_candidates)}")
        print(f"Total Candidate Nodes to Retain:    {len(self.path_to_node) - len(archived_candidates)}")
        print("-" * 80)
        
        for r in archived_candidates[:30]:
            print(f"[{r.category.upper()}] {r.rel_path}")
            print(f"    Action: lifecycle '{r.lifecycle}' -> 'ARCHIVED'")
            print(f"    Reason: {r.category_reason}")
            
        if len(archived_candidates) > 30:
            print(f"    ... and {len(archived_candidates) - 30} more candidate nodes.")
        print("=" * 80)

def main():
    parser = argparse.ArgumentParser(description="Vault Corpus Hygiene Diagnostic & Dry-Run Tool")
    parser.add_argument("action", choices=["report", "apply"], help="Action to execute")
    parser.add_argument("--dry-run", action="store_true", help="Execute in dry-run mode (no file mutations)")
    args = parser.parse_args()
    
    auditor = CorpusHygieneAuditor()
    auditor.scan()
    auditor.categorize()
    
    if args.action == "report":
        auditor.print_report()
    elif args.action == "apply":
        if not args.dry_run:
            print("ERROR: Real apply is strictly prohibited by operating contract. Use --dry-run.", file=sys.stderr)
            sys.exit(1)
        auditor.print_dry_run()

if __name__ == "__main__":
    main()
