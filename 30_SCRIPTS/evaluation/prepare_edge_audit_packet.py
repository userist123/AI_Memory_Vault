"""prepare_edge_audit_packet.py — Generates stratified 50-sample audit packet for edge proposals.

Requirements:
1. Stratified sampling: 25 strong relations + 25 weak relations.
2. Reproducible sampling with fixed seed (seed=42).
3. Status: PENDING_AUDIT (does NOT claim 100% precision; verdicts left empty for human/independent critic review).
4. Writes:
   - 07_EVALUATION/edge_audit/audit_sample_50.json
   - 07_EVALUATION/edge_audit/audit_packet.md
   - 08_OBSERVABILITY/reports/edge_verification_sample_50.json
"""
from __future__ import annotations

import argparse
import json
import random
from datetime import datetime, timezone
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
PROPOSALS_PATH = REPO_ROOT / "08_OBSERVABILITY" / "reports" / "edge_proposals.json"
AUDIT_DIR = REPO_ROOT / "07_EVALUATION" / "edge_audit"
AUDIT_JSON_PATH = AUDIT_DIR / "audit_sample_50.json"
AUDIT_MD_PATH = AUDIT_DIR / "audit_packet.md"
OBS_SAMPLE_PATH = REPO_ROOT / "08_OBSERVABILITY" / "reports" / "edge_verification_sample_50.json"

STRONG_RELATIONS = frozenset({"depends_on", "supersedes", "applies_to", "verified_by", "caused", "contradicts"})
WEAK_RELATIONS = frozenset({"related_to", "part_of"})


def build_audit_packet(proposals_path: Path = PROPOSALS_PATH, seed: int = 42, target_total: int = 50) -> tuple[dict, str]:
    if not proposals_path.exists():
        raise FileNotFoundError(f"Proposals file not found: {proposals_path}")

    data = json.loads(proposals_path.read_text(encoding="utf-8"))
    proposals = data.get("proposals", [])

    strong_pool = [p for p in proposals if p.get("relation") in STRONG_RELATIONS]
    weak_pool = [p for p in proposals if p.get("relation") in WEAK_RELATIONS]

    rng = random.Random(seed)
    n_strong = min(25, len(strong_pool))
    n_weak = min(target_total - n_strong, len(weak_pool))

    sampled_strong = rng.sample(strong_pool, n_strong) if len(strong_pool) > n_strong else list(strong_pool)
    sampled_weak = rng.sample(weak_pool, n_weak) if len(weak_pool) > n_weak else list(weak_pool)

    # Sort each group deterministically by (relation, source_id, target_id)
    sampled_strong.sort(key=lambda p: (p.get("relation", ""), p.get("source_id", ""), p.get("target_id", "")))
    sampled_weak.sort(key=lambda p: (p.get("relation", ""), p.get("source_id", ""), p.get("target_id", "")))

    all_samples = []
    idx = 1
    for p in sampled_strong:
        sample_item = dict(p)
        sample_item["index"] = idx
        sample_item["rel_tier"] = "strong"
        sample_item["verdict"] = None
        sample_item["rationale"] = None
        sample_item["evaluator"] = None
        sample_item["audit_status"] = "PENDING_AUDIT"
        all_samples.append(sample_item)
        idx += 1

    for p in sampled_weak:
        sample_item = dict(p)
        sample_item["index"] = idx
        sample_item["rel_tier"] = "weak"
        sample_item["verdict"] = None
        sample_item["rationale"] = None
        sample_item["evaluator"] = None
        sample_item["audit_status"] = "PENDING_AUDIT"
        all_samples.append(sample_item)
        idx += 1

    timestamp = datetime.now(timezone.utc).isoformat(timespec="seconds")
    audit_data = {
        "generated": timestamp,
        "status": "PENDING_AUDIT",
        "sample_size": len(all_samples),
        "strong_count": n_strong,
        "weak_count": n_weak,
        "audited": False,
        "valid_count": None,
        "precision": None,
        "precision_str": "AUDIT ÎN AȘTEPTARE (50 relații stratificate nevalidate)",
        "samples": all_samples,
    }

    # Generate Markdown
    lines = [
        "# Pachet de Audit Relații Sinaptice (Eșantion Stratificat de 50 Propuneri)",
        "",
        "> **Status: AUDIT ÎN AȘTEPTARE — necesită evaluare umană/critic independent**  ",
        f"> **Dată Generare**: `{timestamp}`  ",
        f"> **Compoziție Eșantion**: {n_strong} Relații Tari (Strong: `depends_on`, `supersedes`, `applies_to`, `verified_by`, `caused`, `contradicts`) + {n_weak} Relații Slabe (Weak: `related_to`, `part_of`)  ",
        "> **Metodologie**: Eșantionare aleatoare stratificată reproductibilă (seed=42) din `08_OBSERVABILITY/reports/edge_proposals.json`  ",
        "",
        "---",
        "",
        "## Instrucțiuni pentru Evaluator",
        "",
        "1. Pentru fiecare dintre cele 50 de propuneri de mai jos, verificați dacă relația propusă între nota Sursă și nota Țintă este corectă semantic și de domeniu.",
        "2. Bifați `[x] ACCEPT` dacă relația este factuală și justificată de ambele citate verbatim.",
        "3. Bifați `[x] REJECT` dacă relația este spurie, forțată sau dacă citatele nu reflectă o dependență/asociere reală de domeniu.",
        "4. Menționați motivul respingerii sau acceptării în câmpul `Motiv:` și semnați cu ID-ul de evaluator.",
        "",
        "---",
        "",
    ]

    for s in all_samples:
        i = s["index"]
        tier = s["rel_tier"].upper()
        rel = s["relation"]
        src_id = s.get("source_id", "")
        dst_id = s.get("target_id", "")
        src_path = s.get("source_path", "")
        dst_path = s.get("target_path", "")
        conf = s.get("confidence", 0.0)
        weight = s.get("weight", 0.0)
        ents = ", ".join(f"`{e}`" for e in s.get("evidence_entities", []))
        sq = s.get("source_quote", "").replace("\n", " ")
        tq = s.get("target_quote", "").replace("\n", " ")

        lines.extend([
            f"### Propunerea {i:02d}/50 [{tier}] `{rel}`: `{src_id}` $\\to$ `{dst_id}`",
            "",
            f"- **Sursă**: `{src_path}`",
            f"- **Țintă**: `{dst_path}`",
            f"- **Relație Propusă**: `{rel}` (Încredere: `{conf}`, Pondere: `{weight}`)",
            f"- **Entități Evidență**: {ents}",
            "- **Citat Verbatim Sursă**:",
            f"  > \"{sq}\"",
            "- **Citat Verbatim Țintă**:",
            f"  > \"{tq}\"",
            "- **Rubrică de Evaluare**:",
            "  - [ ] ACCEPT",
            "  - [ ] REJECT",
            "  - **Motiv**: __________________________________________________",
            "  - **Evaluator**: ____________________  **Dată**: ______________",
            "",
            "---",
            "",
        ])

    md_content = "\n".join(lines)
    return audit_data, md_content


def main() -> int:
    parser = argparse.ArgumentParser(description="Prepare stratified edge audit packet")
    parser.add_argument("--proposals", default=str(PROPOSALS_PATH), help="Path to input proposals JSON")
    parser.add_argument("--out-json", default=str(AUDIT_JSON_PATH), help="Output audit sample JSON")
    parser.add_argument("--out-md", default=str(AUDIT_MD_PATH), help="Output audit packet MD")
    parser.add_argument("--obs-json", default=str(OBS_SAMPLE_PATH), help="Observability sample JSON (optional)")
    parser.add_argument("--seed", type=int, default=42, help="Random seed")
    parser.add_argument("--target-total", type=int, default=50, help="Target total sample size")
    args = parser.parse_args()

    proposals_path = Path(args.proposals)
    out_json = Path(args.out_json)
    out_md = Path(args.out_md)
    obs_json = Path(args.obs_json) if args.obs_json else None

    out_json.parent.mkdir(parents=True, exist_ok=True)
    out_md.parent.mkdir(parents=True, exist_ok=True)

    audit_data, md_content = build_audit_packet(proposals_path=proposals_path, seed=args.seed, target_total=args.target_total)

    out_json.write_text(json.dumps(audit_data, indent=2, ensure_ascii=False), encoding="utf-8")
    out_md.write_text(md_content, encoding="utf-8")
    if obs_json:
        obs_json.parent.mkdir(parents=True, exist_ok=True)
        obs_json.write_text(json.dumps(audit_data, indent=2, ensure_ascii=False), encoding="utf-8")

    print(f"Generated {out_json} ({audit_data['sample_size']} items)")
    print(f"Generated {out_md} ({len(md_content)} bytes)")
    if obs_json:
        print(f"Updated {obs_json}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
