#!/usr/bin/env python3
"""Verify that controller lifecycle transitions stay aligned with canonical policy.

This check is intentionally read-only. It compares the transition tuples encoded
in MemoryController._validate_note against the canonical lifecycle policy and
reports compatibility transitions that have not yet been wired to named policy
mutations.
"""
from __future__ import annotations

import argparse
import ast
from pathlib import Path

from memory_controller.lifecycle_policy import Mutation, allowed_targets


POLICY_TRANSITIONS = {
    (source, target, mutation)
    for mutation in Mutation
    for source in {
        "RAW", "CLASSIFIED", "NORMALIZED", "REVIEW", "VERIFIED",
        "ACTIVE", "RECONSOLIDATING", "SUPERSEDED", "ARCHIVED",
    }
    for target in allowed_targets(source, mutation=mutation)
    if source != target
}

COMPATIBILITY_TRANSITIONS = {
    ("RAW", "CLASSIFIED"),
    ("CLASSIFIED", "NORMALIZED"),
    ("REVIEW", "VERIFIED"),
    ("VERIFIED", "ACTIVE"),
}


def _lifecycle_name(node: ast.AST) -> str | None:
    if isinstance(node, ast.Attribute) and isinstance(node.value, ast.Name) and node.value.id == "Lifecycle":
        return node.attr
    return None


def extract_controller_transitions(controller_text: str) -> set[tuple[str, str]]:
    tree = ast.parse(controller_text)
    found: set[tuple[str, str]] = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Set):
            for item in node.elts:
                if not isinstance(item, ast.Tuple) or len(item.elts) != 2:
                    continue
                source = _lifecycle_name(item.elts[0])
                target = _lifecycle_name(item.elts[1])
                if source and target:
                    found.add((source, target))
        if isinstance(node, ast.Dict):
            for key in node.keys:
                if not isinstance(key, ast.Tuple) or len(key.elts) != 2:
                    continue
                source = _lifecycle_name(key.elts[0])
                target = _lifecycle_name(key.elts[1])
                if source and target:
                    found.add((source, target))
    return found


def audit(controller_path: Path) -> tuple[set[tuple[str, str]], set[tuple[str, str]]]:
    text = controller_path.read_text(encoding="utf-8")
    transitions = extract_controller_transitions(text)
    canonical_pairs = {(source, target) for source, target, _ in POLICY_TRANSITIONS}
    missing = canonical_pairs - transitions
    return transitions, missing


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--controller", type=Path, default=Path("memory_controller/controller.py"))
    args = parser.parse_args()

    transitions, missing = audit(args.controller.resolve())
    print(f"CONTROLLER={args.controller.resolve()}")
    print(f"POLICY_TRANSITIONS={len(POLICY_TRANSITIONS)}")
    print(f"CONTROLLER_TRANSITION_PAIRS={len(transitions)}")
    print(f"MISSING_CANONICAL_PAIRS={len(missing)}")
    for source, target in sorted(missing):
        status = "LEGACY_COMPATIBILITY" if (source, target) in COMPATIBILITY_TRANSITIONS else "UNACCOUNTED"
        print(f"{source}->{target}:{status}")
    return 1 if missing - COMPATIBILITY_TRANSITIONS else 0


if __name__ == "__main__":
    raise SystemExit(main())
