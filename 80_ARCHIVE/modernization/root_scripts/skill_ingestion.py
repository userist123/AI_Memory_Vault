#!/usr/bin/env python3
"""AI Memory Vault skill ingestion, promotion and agent routing.

Safe-by-default: this module reads and classifies external skills. It never
executes imported code. Promotion requires an explicit --promote operation and
creates operational copies only after validation checks pass.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Iterable

ROOT = Path(__file__).resolve().parents[1]
RAW = ROOT / "06_INBOX" / "RAW_IMPORTS" / "skills"
OPS = ROOT / ".agents" / "skills"
REGISTRY = RAW / "_OPERATIONAL_SKILL_REGISTRY.json"
ROUTING = RAW / "_AGENT_SKILL_ROUTING.json"

AGENT_KEYWORDS = {
    "backend_systems_engineer": "backend api rest graphql grpc node express nestjs django flask rails spring fastapi postgres redis oauth rbac cqrs",
    "polyglot_systems_architect": "rust go python typescript javascript c c++ csharp java kotlin scala swift backend compiler architecture",
    "compiler_and_tooling_engineer": "compiler language parser ast lexer runtime tooling build syntax language lsp ide",
    "site_reliability_and_devops_architect": "devops sre kubernetes docker terraform ansible helm argocd prometheus grafana observability cloud deployment",
    "system_architecture_agent": "architecture enterprise distributed microservices security scalability cloud infrastructure",
    "secops_auditor": "security secops devsecops owasp sast dast zero trust pki secrets audit compliance",
    "threat_hunting_analyst": "threat hunting dfir forensic incident malware detection mitre siem security",
    "wpf_engineer": "wpf xaml winui desktop .net csharp mvvm windows",
    "web_creative_developer": "threejs webgl gsap shader cobejs vanta matterjs motion creative 3d",
    "web_design_engineer_agent": "web design editorial grid linear apple stripe vercel supabase design system landing",
    "web_quality_engineer": "web quality performance lighthouse core web vitals accessibility wcag seo optimization",
    "ui_sensei_architect": "ui visual design glass skeuomorphic minimal layout interface design system",
    "frontend_saas_engineer": "frontend react nextjs vite tailwind zustand tanstack storybook playwright typescript saas",
    "game_engineer": "game unity unreal webgl arpg combat enemy ai vfx inventory camera threejs",
    "quant_developer": "trading quant finance algorithmic strategy risk backtest portfolio market",
    "local_ai_engineer": "llm ai ollama langchain llamaindex vllm embeddings rag pydantic fine tuning guardrails",
    "content_strategist": "copywriting content email presentation brand voice marketing",
    "agentic_workflow_orchestrator": "agent workflow orchestration mcp copilot automation tool calling multi agent",
    "ui_ux_designer": "ux ui dashboard data visualization motion brand email presentation",
    "database_and_persistence_engineer": "database postgres mysql sqlite redis mongodb duckdb clickhouse elasticsearch qdrant neo4j vector sql",
    "memory_controller_architect": "memory vault provenance lifecycle knowledge retrieval audit obsidian cognition",
}

@dataclass
class SkillRecord:
    skill_id: str
    name: str
    source_path: str
    sha256: str
    status: str
    compatible_agents: list[str]
    description: str = ""


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def parse_frontmatter(text: str) -> dict[str, str]:
    if not text.startswith("---"):
        return {}
    end = text.find("\n---", 3)
    if end < 0:
        return {}
    data: dict[str, str] = {}
    for line in text[4:end].splitlines():
        if ":" not in line:
            continue
        k, v = line.split(":", 1)
        data[k.strip()] = v.strip().strip('"\'')
    return data


def iter_skill_files() -> Iterable[Path]:
    if not RAW.exists():
        return []
    return (p for p in RAW.rglob("SKILL.md") if p.is_file() and "node_modules" not in p.parts)


def compatible_agents(text: str) -> list[str]:
    haystack = text.lower()
    scored: list[tuple[int, str]] = []
    for agent, words in AGENT_KEYWORDS.items():
        score = sum(1 for word in words.split() if re.search(r"(?<![a-z0-9])" + re.escape(word) + r"(?![a-z0-9])", haystack))
        if score:
            scored.append((score, agent))
    scored.sort(reverse=True)
    return [agent for _, agent in scored[:5]]


def scan() -> list[SkillRecord]:
    records: list[SkillRecord] = []
    for path in iter_skill_files():
        text = path.read_text(encoding="utf-8", errors="replace")
        meta = parse_frontmatter(text)
        name = meta.get("name") or path.parent.name
        description = meta.get("description", "")
        agents = compatible_agents(f"{name} {description} {text[:12000]}")
        records.append(SkillRecord(
            skill_id=re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-") or path.parent.name,
            name=name,
            source_path=path.relative_to(ROOT).as_posix(),
            sha256=sha256_file(path),
            status="raw_external",
            compatible_agents=agents,
            description=description,
        ))
    return records


def write_json(path: Path, payload: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def sync_registry(records: list[SkillRecord]) -> None:
    write_json(REGISTRY, {"version": 1, "generated_by": "scripts/skill_ingestion.py", "skills": [asdict(r) for r in records]})
    routing: dict[str, list[str]] = {agent: [] for agent in AGENT_KEYWORDS}
    for record in records:
        for agent in record.compatible_agents:
            routing.setdefault(agent, []).append(record.skill_id)
    write_json(ROUTING, {"version": 1, "generated_by": "scripts/skill_ingestion.py", "routing": routing})


def promote(records: list[SkillRecord], skill_ids: set[str], verified: bool) -> int:
    if not verified:
        raise SystemExit("Promotion blocked: pass --verified after provenance/validation review.")
    promoted = 0
    seen_hashes: set[str] = set()
    for record in records:
        if record.skill_id not in skill_ids:
            continue
        if record.sha256 in seen_hashes:
            continue
        source = ROOT / record.source_path
        destination = OPS / record.skill_id / "SKILL.md"
        destination.parent.mkdir(parents=True, exist_ok=True)
        if destination.exists() and sha256_file(destination) == record.sha256:
            record.status = "operational"
            continue
        shutil.copy2(source, destination)
        record.status = "operational"
        seen_hashes.add(record.sha256)
        promoted += 1
    return promoted


def main() -> None:
    parser = argparse.ArgumentParser(description="AI Memory Vault skill ingestion pipeline")
    parser.add_argument("command", choices=["scan", "promote", "match"])
    parser.add_argument("--skill", action="append", default=[], help="skill id; repeat for multiple skills")
    parser.add_argument("--verified", action="store_true", help="explicitly confirm validation/provenance review")
    args = parser.parse_args()

    records = scan()
    sync_registry(records)

    if args.command == "scan":
        print(json.dumps([asdict(r) for r in records], indent=2, ensure_ascii=False))
        return
    if args.command == "match":
        for r in records:
            print(f"{r.skill_id}: {', '.join(r.compatible_agents) or 'manual-review'}")
        return
    if not args.skill:
        raise SystemExit("promote requires --skill <skill-id>")
    count = promote(records, set(args.skill), args.verified)
    sync_registry(records)
    print(f"Promoted {count} skill(s) to .agents/skills/")


if __name__ == "__main__":
    main()
