#!/usr/bin/env python3
"""Validate a proposed Council context against the sparse-runtime contract.

Dependency-free validator. It checks structural limits, duplicate payloads,
forbidden broad-context flags, and an optional serialized byte budget.
"""

from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path

MAX_AGENTS = 3
MAX_PRIMARY_AGENTS = 1
MAX_SKILLS_PER_AGENT = 2
MAX_MEMORY_RESULTS = 5
MAX_GRAPH_HOPS = 1
MAX_SPECIALIST_OUTPUT = 600
MAX_SYNTHESIS_INPUT = 2500
MAX_TOTAL_SKILLS = 4
DEFAULT_HARD_CONTEXT_BYTES = 32768

# These are policy/navigation artifacts and should never be injected as full
# runtime context merely because a skill or memory item links to them.
FORBIDDEN_BROAD_CONTEXT_FLAGS = {
    "whole_vault": "whole-vault context is disabled",
    "load_all_assigned_skills": "loading all assigned skills is disabled",
    "recursive_council": "recursive council is disabled",
    "load_global_registry": "loading the global registry is disabled",
    "load_council_map": "loading the Council map is disabled",
    "load_knowledge_graph": "loading the Knowledge Graph home/full graph is disabled",
    "load_progress": "progress artifacts are excluded from runtime context",
    "load_handoff": "handoff artifacts are excluded from runtime context",
    "load_briefing": "briefing artifacts are excluded from runtime context",
    "load_dispatch": "dispatch artifacts are excluded from runtime context",
}


def _fingerprint(value: object) -> str:
    raw = json.dumps(value, ensure_ascii=False, sort_keys=True, default=str, separators=(",", ":"))
    return hashlib.sha256(raw.encode("utf-8")).hexdigest()


def _serialized_size(data: dict) -> int:
    return len(json.dumps(data, ensure_ascii=False, default=str, separators=(",", ":")).encode("utf-8"))


def validate(data: dict, hard_context_bytes: int = DEFAULT_HARD_CONTEXT_BYTES) -> list[str]:
    errors: list[str] = []
    agents = data.get("agents", [])
    if not isinstance(agents, list):
        return ["agents must be a list"]

    if len(agents) > MAX_AGENTS:
        errors.append(f"too many agents: {len(agents)} > {MAX_AGENTS}")

    primary = sum(1 for a in agents if isinstance(a, dict) and a.get("role") == "primary")
    if primary > MAX_PRIMARY_AGENTS:
        errors.append(f"too many primary agents: {primary} > {MAX_PRIMARY_AGENTS}")

    total_skills = 0
    skill_ids: list[str] = []
    memory_fingerprints: list[str] = []

    for agent in agents:
        if not isinstance(agent, dict):
            errors.append("each agent must be an object")
            continue
        skills = agent.get("skills", [])
        if not isinstance(skills, list):
            errors.append(f"skills must be a list for {agent.get('id', '<unknown>')}")
            continue
        total_skills += len(skills)
        if len(skills) > MAX_SKILLS_PER_AGENT:
            errors.append(
                f"too many skills for {agent.get('id', '<unknown>')}: "
                f"{len(skills)} > {MAX_SKILLS_PER_AGENT}"
            )
        for skill in skills:
            if isinstance(skill, str):
                skill_ids.append(skill)
            elif isinstance(skill, dict):
                skill_id = skill.get("id")
                if skill_id:
                    skill_ids.append(str(skill_id))

        memory = agent.get("memory_results", [])
        if isinstance(memory, list):
            memory_fingerprints.extend(_fingerprint(item) for item in memory)

    if total_skills > MAX_TOTAL_SKILLS:
        errors.append(f"too many selected skills: {total_skills} > {MAX_TOTAL_SKILLS}")

    duplicates = sorted({sid for sid in skill_ids if skill_ids.count(sid) > 1})
    if duplicates:
        errors.append("duplicate selected skills: " + ", ".join(duplicates))

    memory = data.get("memory_results", [])
    if not isinstance(memory, list):
        errors.append("memory_results must be a list")
        memory = []
    if len(memory) > MAX_MEMORY_RESULTS:
        errors.append(f"too many memory results: {len(memory)} > {MAX_MEMORY_RESULTS}")

    all_memory_fingerprints = memory_fingerprints + [_fingerprint(item) for item in memory]
    if len(all_memory_fingerprints) != len(set(all_memory_fingerprints)):
        errors.append("duplicate memory/evidence payloads detected")

    hops = data.get("graph_hops", 0)
    if not isinstance(hops, int) or hops < 0:
        errors.append("graph_hops must be a non-negative integer")
    elif hops > MAX_GRAPH_HOPS:
        errors.append(f"graph expansion too deep: {hops} > {MAX_GRAPH_HOPS}")

    specialist_output = data.get("specialist_output_tokens", 0)
    if specialist_output > MAX_SPECIALIST_OUTPUT:
        errors.append(f"specialist output too large: {specialist_output} > {MAX_SPECIALIST_OUTPUT}")

    synthesis_input = data.get("synthesis_input_tokens", 0)
    if synthesis_input > MAX_SYNTHESIS_INPUT:
        errors.append(f"synthesis input too large: {synthesis_input} > {MAX_SYNTHESIS_INPUT}")

    for flag, message in FORBIDDEN_BROAD_CONTEXT_FLAGS.items():
        if data.get(flag, False):
            errors.append(message)

    requested_hard_bytes = data.get("hard_context_bytes", hard_context_bytes)
    if not isinstance(requested_hard_bytes, int) or requested_hard_bytes <= 0:
        errors.append("hard_context_bytes must be a positive integer")
    else:
        usage = _serialized_size(data)
        if usage > requested_hard_bytes:
            errors.append(f"serialized context too large: {usage} > {requested_hard_bytes} bytes")

    return errors


def main() -> int:
    if len(sys.argv) not in (2, 3):
        print("usage: python Council_Context_Validator.py <context.json> [hard_bytes]")
        return 2

    path = Path(sys.argv[1])
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
        hard_bytes = int(sys.argv[2]) if len(sys.argv) == 3 else DEFAULT_HARD_CONTEXT_BYTES
    except (OSError, json.JSONDecodeError, ValueError) as exc:
        print(f"ERROR: cannot read context manifest: {exc}")
        return 2

    errors = validate(data, hard_context_bytes=hard_bytes)
    if errors:
        print("COUNCIL CONTEXT: FAIL")
        for error in errors:
            print(f"- {error}")
        return 1

    print("COUNCIL CONTEXT: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
