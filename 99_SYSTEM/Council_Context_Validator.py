#!/usr/bin/env python3
"""Validate a proposed Council context against the sparse-runtime contract.

This validator is intentionally dependency-free. It validates a JSON context
manifest rather than attempting to count model-specific tokens.
"""

from __future__ import annotations

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


def validate(data: dict) -> list[str]:
    errors: list[str] = []
    agents = data.get("agents", [])
    if not isinstance(agents, list):
        return ["agents must be a list"]

    if len(agents) > MAX_AGENTS:
        errors.append(f"too many agents: {len(agents)} > {MAX_AGENTS}")

    primary = sum(1 for a in agents if a.get("role") == "primary")
    if primary > MAX_PRIMARY_AGENTS:
        errors.append(f"too many primary agents: {primary} > {MAX_PRIMARY_AGENTS}")

    total_skills = 0
    for agent in agents:
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

    if total_skills > MAX_TOTAL_SKILLS:
        errors.append(f"too many selected skills: {total_skills} > {MAX_TOTAL_SKILLS}")

    memory = data.get("memory_results", [])
    if len(memory) > MAX_MEMORY_RESULTS:
        errors.append(f"too many memory results: {len(memory)} > {MAX_MEMORY_RESULTS}")

    hops = data.get("graph_hops", 0)
    if hops > MAX_GRAPH_HOPS:
        errors.append(f"graph expansion too deep: {hops} > {MAX_GRAPH_HOPS}")

    specialist_output = data.get("specialist_output_tokens", 0)
    if specialist_output > MAX_SPECIALIST_OUTPUT:
        errors.append(
            f"specialist output too large: {specialist_output} > {MAX_SPECIALIST_OUTPUT}"
        )

    synthesis_input = data.get("synthesis_input_tokens", 0)
    if synthesis_input > MAX_SYNTHESIS_INPUT:
        errors.append(
            f"synthesis input too large: {synthesis_input} > {MAX_SYNTHESIS_INPUT}"
        )

    if data.get("recursive_council", False):
        errors.append("recursive council is disabled")
    if data.get("whole_vault", False):
        errors.append("whole-vault context is disabled")
    if data.get("load_all_assigned_skills", False):
        errors.append("loading all assigned skills is disabled")

    return errors


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: python Council_Context_Validator.py <context.json>")
        return 2

    path = Path(sys.argv[1])
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        print(f"ERROR: cannot read context manifest: {exc}")
        return 2

    errors = validate(data)
    if errors:
        print("COUNCIL CONTEXT: FAIL")
        for error in errors:
            print(f"- {error}")
        return 1

    print("COUNCIL CONTEXT: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
