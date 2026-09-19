"""Make one real proposal through the MCP server: what the real work questions showed.

The note body is built from the computed summary (summarize_worklist.py), so its numbers are the
report's numbers. The proposal goes through the real vault-memory server as an MCP client:
memory_propose, then memory_search for it, then memory_get. The proof is written next to the
other evidence. The candidate is REVIEW / unverified; only the owner can attest it.

    python 30_SCRIPTS/evaluation/propose_worklist_findings.py [--force]
"""
from __future__ import annotations

import argparse
import asyncio
import json
import os
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "30_SCRIPTS" / "evaluation"))
import summarize_worklist as sw  # noqa: E402

PROOF = sw.DIR / "proposal_proof.json"
SERVER = REPO / "03_IMPLEMENTATION" / "packages" / "interfaces" / "memory_mcp_server.py"
TITLE = "Prima măsurare a căutării de producție pe întrebări reale de lucru"


def build_body(s) -> str:
    lifecycles = ", ".join(f"{k} {v}" for k, v in sorted(s["returned_by_lifecycle"].items()))
    return (
        "Am pus 20 de întrebări de lucru, luate din sarcinile deschise din `00_GOVERNANCE/coordination/`, "
        "prin serverul MCP `vault-memory` (`memory_search`, setările implicite de producție). "
        "Rezultatele complete sunt în `07_EVALUATION/memory_usage/` (raport generat: `WORKLIST_RELEVANCE.md`).\n\n"
        f"- Din primele trei rezultate ale fiecărei întrebări, {s['relevant_results']} din {s['judged_results']} "
        f"au fost relevante pentru sarcină (judecată manuală, un singur evaluator); "
        f"{s['questions_with_a_relevant_top3']} din {s['questions']} întrebări au avut cel puțin un rezultat relevant; "
        f"{s['questions_with_zero_results']} din {s['questions']} au întors zero rezultate.\n"
        f"- Fișierele de coordonare din care vin sarcinile nu sunt note indexate: "
        f"{s['sources_indexed']} din {s['sources_total']} pot fi întoarse de memorie. Sarcinile deschise sunt deci "
        "invizibile pentru căutare.\n"
        f"- Cele {s['returned_total']} rezultate au avut ciclul de viață: {lifecycles}; "
        f"{s['returned_not_active_or_verified']} din {s['returned_total']} nu sunt ACTIVE sau VERIFIED.\n"
        "- Note juridice mari (GDPR, DORA, MiCA, AI Act) apar în primele rezultate pentru întrebări formulate cu "
        "cuvinte uzuale românești, fără legătură cu subiectul.\n"
        "- Notele cu `lifecycle: raw` scris cu litere mici sau fără `lifecycle` trec de excluderea RAW, "
        "care compară șirul exact `RAW`.\n"
        "- `memory_get` pe o notă foarte mare întoarce conținut comprimat (`[PARTIAL]`), din cauza bugetului de context.\n\n"
        "Nu s-a schimbat nimic în căutare: aceasta e doar măsurătoarea. Decizia despre ce se corectează e a proprietarului."
    )


async def run() -> dict:
    from mcp import ClientSession, StdioServerParameters
    from mcp.client.stdio import stdio_client
    from mcp.types import Implementation

    results = json.loads((sw.DIR / "work_session_results.json").read_text(encoding="utf-8"))["results"]
    judgements = json.loads((sw.DIR / "work_session_judgements.json").read_text(encoding="utf-8"))["judgements"]
    body = build_body(sw.summarise(results, judgements, sw.indexed_sources(results)))
    env = {k: v for k, v in os.environ.items() if k != "MEMORY_CONTROLLER_HMAC_SECRET"}
    params = StdioServerParameters(command=sys.executable, args=[str(SERVER)], env=env, cwd=str(REPO))
    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write, client_info=Implementation(name="claude-sonnet-mcp-sdk-driver", version="1")) as session:
            await session.initialize()
            proposed = await session.call_tool("memory_propose", {
                "title": TITLE, "body": body, "type": "knowledge",
                "provenance": {"source_type": "ai", "source_ref": "07_EVALUATION/memory_usage/WORKLIST_RELEVANCE.md"}})
            assert not proposed.isError, proposed.content
            p = proposed.structuredContent.get("result", proposed.structuredContent)
            found = await session.call_tool("memory_search", {"query": TITLE, "limit": 5})
            rows = found.structuredContent.get("result", found.structuredContent)["query_results"]
            note = await session.call_tool("memory_get", {"note_id": p["id"]})
            n = note.structuredContent.get("result", note.structuredContent)
    rank = next((i for i, r in enumerate(rows, 1) if r["id"] == p["id"]), None)
    return {
        "proposal": {"id": p["id"], "path": p["path"], "lifecycle": p["lifecycle"], "verification": p["verification"]},
        "file_exists": (REPO / p["path"]).is_file(),
        "found_by_memory_search": rank is not None, "search_rank": rank,
        "search_query_is_the_title": True,
        "memory_get": {"lifecycle": n["lifecycle"], "unverified": n["unverified"], "content_chars": len(n["content"])},
    }


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--force", action="store_true", help="propose again even if a proof already exists")
    args = parser.parse_args(argv)
    if PROOF.exists() and not args.force:
        raise SystemExit(f"{PROOF.name} already exists: the proposal was made; use --force to make another")
    proof = asyncio.run(run())
    PROOF.write_text(json.dumps(proof, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps(proof, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
