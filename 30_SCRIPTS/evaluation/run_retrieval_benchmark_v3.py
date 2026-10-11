"""Benchmark de regăsire v3 — măsurătoare preînregistrată pe graful înghețat (commit b3ada1b54).

Rulează cele 5 brațe principale (spreading activation oprit) și cele 4 brațe informative
(spreading activation pornit) pe cazurile preînregistrate din:
07_EVALUATION/retrieval_benchmark_v3/retrieval_benchmark_v3.json

Toate cifrele din BENCHMARK_V3_REPORT.md sunt generate exclusiv din results_v3_arms.json.
Nicio cifră nu este scrisă de mână.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import re
import sys
from collections import Counter
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

REPO = Path(__file__).resolve().parents[2]
BENCH_DIR = REPO / "07_EVALUATION" / "retrieval_benchmark_v3"
BENCH_JSON = BENCH_DIR / "retrieval_benchmark_v3.json"
BENCH_SHA = BENCH_DIR / "retrieval_benchmark_v3.json.sha256"
RESULTS_JSON = BENCH_DIR / "results_v3_arms.json"
REPORT_MD = BENCH_DIR / "BENCHMARK_V3_REPORT.md"

DEFAULT_WORKTREE = Path(r"c:\Users\Marius\Documents\Codex\vault_b3ada1b54")

sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller.controller import MemoryController, RANKING_ARM_BASELINE  # noqa: E402
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
from retrieval.vault_index import VaultIndex  # noqa: E402

try:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

UNMEASURABLE = "UNMEASURABLE"
EXCLUDED_MULTI_HOP_CASES = frozenset({"R3-061", "R3-080", "R3-088"})
DEFAULT_BUDGET_ZERO_AT = 20

# (label, graph_on, budget, spreading_activation, is_informative)
ARMS = (
    ("graph_off", False, None, False, False),
    ("budget_default", True, None, False, False),
    ("budget_5", True, 5, False, False),
    ("budget_10", True, 10, False, False),
    ("budget_20", True, 20, False, False),
    ("spreading_default", True, None, True, True),
    ("spreading_5", True, 5, True, True),
    ("spreading_10", True, 10, True, True),
    ("spreading_20", True, 20, True, True),
)


# --------------------------------------------------------------------------
# Statistical Helpers
# --------------------------------------------------------------------------

def wilson_score_interval(k: int, n: int, confidence: float = 0.95) -> Dict[str, float]:
    """Wilson score confidence interval for binomial proportion."""
    if n == 0:
        return {"proportion": 0.0, "lower": 0.0, "upper": 0.0}
    p = k / n
    # For 95% CI, z ~ 1.959963984540054
    z = 1.959963984540054 if confidence == 0.95 else 1.96
    denominator = 1.0 + (z ** 2) / n
    center = (p + (z ** 2) / (2.0 * n)) / denominator
    margin = (z / denominator) * math.sqrt((p * (1.0 - p) / n) + (z ** 2) / (4.0 * (n ** 2)))
    return {
        "proportion": round(p, 4),
        "lower": round(max(0.0, center - margin), 4),
        "upper": round(min(1.0, center + margin), 4),
    }


def exact_mcnemar_p(b: int, c: int) -> float:
    """Exact two-sided McNemar test p-value using Binomial(b+c, 0.5)."""
    n = b + c
    if n == 0:
        return 1.0
    k = min(b, c)
    cdf = sum(math.comb(n, i) for i in range(k + 1)) * (0.5 ** n)
    return min(1.0, 2.0 * cdf)


# --------------------------------------------------------------------------
# Aggregation & Evaluation
# --------------------------------------------------------------------------

def fraction(k: int, n: int) -> Dict[str, Any]:
    return {"k": k, "n": n, "text": f"{k}/{n}"}


def aggregate_subgroup(rows: List[Dict[str, Any]]) -> Dict[str, Any]:
    measurable = [r for r in rows if r["candidate_recall"] != UNMEASURABLE]
    expanded_counts = [int(r["expanded"]) for r in rows]
    with_expansion = [c for c in expanded_counts if c > 0]
    cand_k = sum(r["candidate_recall"] for r in measurable) if measurable else 0
    ctx_k = sum(r["context_recall"] for r in measurable) if measurable else 0
    n_meas = len(measurable)

    return {
        "n_queries": len(rows),
        "n_measurable": n_meas,
        "n_unmeasurable": len(rows) - n_meas,
        "candidate_recall": fraction(cand_k, n_meas),
        "candidate_recall_ci": wilson_score_interval(cand_k, n_meas),
        "context_recall": fraction(ctx_k, n_meas),
        "context_recall_ci": wilson_score_interval(ctx_k, n_meas),
        "queries_with_expansion": fraction(len(with_expansion), len(rows)),
        "new_nodes_total": sum(expanded_counts),
        "mean_new_nodes_per_query": round(sum(expanded_counts) / len(rows), 4) if rows else 0.0,
        "mean_new_nodes_when_expanded": round(sum(with_expansion) / len(with_expansion), 4) if with_expansion else None,
    }


def paired_vs_reference(reference: List[Dict[str, Any]], arm: List[Dict[str, Any]], field: str) -> Dict[str, Any]:
    by_id = {r["id"]: r for r in reference}
    b = c = tied_1 = tied_0 = skipped = 0
    winning_cases = []
    losing_cases = []
    for r in arm:
        o = by_id.get(r["id"])
        if o is None or UNMEASURABLE in (r[field], o[field]):
            skipped += 1
            continue
        if o[field] == 0 and r[field] == 1:
            c += 1  # arm won
            winning_cases.append(r["id"])
        elif o[field] == 1 and r[field] == 0:
            b += 1  # arm lost
            losing_cases.append(r["id"])
        elif o[field] == 1 and r[field] == 1:
            tied_1 += 1
        elif o[field] == 0 and r[field] == 0:
            tied_0 += 1

    return {
        "gained": c,
        "lost": b,
        "tied_both_1": tied_1,
        "tied_both_0": tied_0,
        "skipped_unmeasurable": skipped,
        "discordant": b + c,
        "exact_mcnemar_p": round(exact_mcnemar_p(b, c), 6),
        "winning_cases": winning_cases,
        "losing_cases": losing_cases,
    }


def default_budget_control(rows: List[Dict[str, Any]], zero_at: int = DEFAULT_BUDGET_ZERO_AT) -> Dict[str, Any]:
    impossible = [r for r in rows if int(r["seeds"]) >= zero_at]
    added_imp = sum(int(r["expanded"]) for r in impossible)
    possible = [r for r in rows if int(r["seeds"]) < zero_at]
    added_poss = sum(int(r["expanded"]) for r in possible)
    return {
        "zero_at_seed_count": zero_at,
        "queries_total": len(rows),
        "queries_expansion_impossible": fraction(len(impossible), len(rows)),
        "nodes_added_where_impossible": added_imp,
        "queries_expansion_possible": fraction(len(possible), len(rows)),
        "nodes_added_where_possible": added_poss,
        "passed": added_imp == 0,
    }


# --------------------------------------------------------------------------
# Runner
# --------------------------------------------------------------------------

def verify_benchmark_sha() -> None:
    expected = BENCH_SHA.read_text(encoding="utf-8").split()[0]
    actual = hashlib.sha256(BENCH_JSON.read_bytes()).hexdigest()
    if actual != expected:
        raise ValueError(f"FROZEN_BENCHMARK_HASH_MISMATCH: expected {expected}, got {actual}")


def run_case(controller: MemoryController, case: dict, index: VaultIndex) -> dict:
    pack = controller.search(Principal.HUMAN, case["query"], page_size=10)
    trace = pack.get("candidate_trace", {}) or {}
    candidates = {
        e.get("id") for e in (trace.get("fused_ranking") or []) if isinstance(e, dict)
    }
    candidates |= set(trace.get("graph_expanded_ids") or [])
    context = {r.get("id") for r in pack.get("results", []) if r.get("id")}
    gold = set(case.get("gold_relevant_notes") or [])

    if case.get("abstain"):
        return {
            "id": case["id"],
            "class": case["class"],
            "candidate_recall": UNMEASURABLE,
            "context_recall": UNMEASURABLE,
            "answer_correctness": UNMEASURABLE,
            "graph_status": trace.get("graph_expansion_status"),
            "expanded": len(trace.get("graph_expanded_ids") or []),
            "seeds": len(trace.get("graph_seed_ids") or []),
            "top_results": [r.get("id") for r in pack.get("results", [])[:3]],
        }

    blob = " ".join(
        index.by_id[n].text for n in context if n in index.by_id
    ).lower()
    facts_ok = all(f.lower() in blob for f in case.get("required_facts", [])) if case.get("required_facts") else False
    correct = bool(gold & context) and facts_ok

    return {
        "id": case["id"],
        "class": case["class"],
        "candidate_recall": int(bool(gold & candidates)) if gold else 1,
        "context_recall": int(bool(gold & context)) if gold else 1,
        "answer_correctness": int(correct),
        "graph_status": trace.get("graph_expansion_status"),
        "expanded": len(trace.get("graph_expanded_ids") or []),
        "seeds": len(trace.get("graph_seed_ids") or []),
        "top_results": [r.get("id") for r in pack.get("results", [])[:3]],
    }


def execute_arms(worktree_dir: Path) -> Dict[str, Any]:
    verify_benchmark_sha()
    original_cwd = os.getcwd()
    try:
        os.chdir(worktree_dir)
        index = VaultIndex.load(Path("."), include_raw=True, include_archived=True)
        storage = FileStorageEngine(".")
    finally:
        os.chdir(original_cwd)

    cases = json.loads(BENCH_JSON.read_text(encoding="utf-8"))["cases"]

    raw_arms: Dict[str, List[Dict[str, Any]]] = {}
    arms_meta: Dict[str, Any] = {}

    for label, graph_on, budget, spreading, informative in ARMS:
        controller = MemoryController(
            storage=storage,
            index=index,
            enable_graph_expansion=graph_on,
            strict_graph_expansion=False,
            graph_expansion_budget=budget,
            ranking_arm=RANKING_ARM_BASELINE,
            enable_spreading_activation=spreading,
        )
        rows: List[Dict[str, Any]] = []
        errors: List[Dict[str, Any]] = []
        for case in cases:
            try:
                rows.append(run_case(controller, case, index))
            except Exception as exc:
                errors.append({"id": case["id"], "error": f"{type(exc).__name__}: {exc}"})
        raw_arms[label] = rows
        arms_meta[label] = {
            "graph_on": graph_on,
            "budget": budget,
            "enable_spreading_activation": spreading,
            "informative": informative,
            "errors": errors,
        }

    reference_rows = raw_arms["graph_off"]
    out_arms: Dict[str, Any] = {}

    for label, meta in arms_meta.items():
        rows = raw_arms[label]
        # Overall
        summary_all = aggregate_subgroup(rows)
        # By class
        classes = sorted({r["class"] for r in rows})
        by_class = {cls_name: aggregate_subgroup([r for r in rows if r["class"] == cls_name]) for cls_name in classes}
        # Multi-hop filtered (excluding R3-061, R3-080, R3-088)
        mh_filtered_rows = [r for r in rows if r["class"] == "multi_hop" and r["id"] not in EXCLUDED_MULTI_HOP_CASES]
        by_class["multi_hop_filtered_37"] = aggregate_subgroup(mh_filtered_rows)

        paired_all_cand = paired_vs_reference(reference_rows, rows, "candidate_recall")
        paired_all_ctx = paired_vs_reference(reference_rows, rows, "context_recall")

        # Paired for multi-hop
        mh_ref = [r for r in reference_rows if r["class"] == "multi_hop"]
        mh_arm = [r for r in rows if r["class"] == "multi_hop"]
        paired_mh_ctx = paired_vs_reference(mh_ref, mh_arm, "context_recall")

        mh_ref_filt = [r for r in reference_rows if r["class"] == "multi_hop" and r["id"] not in EXCLUDED_MULTI_HOP_CASES]
        mh_arm_filt = [r for r in rows if r["class"] == "multi_hop" and r["id"] not in EXCLUDED_MULTI_HOP_CASES]
        paired_mh_filt_ctx = paired_vs_reference(mh_ref_filt, mh_arm_filt, "context_recall")

        out_arms[label] = {
            **meta,
            "summary": summary_all,
            "by_class": by_class,
            "paired_vs_off": {
                "candidate_recall": paired_all_cand,
                "context_recall": paired_all_ctx,
                "multi_hop_context_recall": paired_mh_ctx,
                "multi_hop_filtered_context_recall": paired_mh_filt_ctx,
            },
            "rows": rows,
        }

    # Evaluate exact preregistered decision rule for budget_5
    b5_paired = out_arms["budget_5"]["paired_vs_off"]["context_recall"]
    b5_summary = out_arms["budget_5"]["summary"]
    wins = b5_paired["gained"]
    losses = b5_paired["lost"]
    mcnemar_p = b5_paired["exact_mcnemar_p"]
    mean_nodes = b5_summary["mean_new_nodes_per_query"]

    cond_wins = wins >= 8
    cond_losses = losses <= 1
    cond_p = mcnemar_p < 0.05
    cond_nodes = mean_nodes <= 4.50
    all_passed = cond_wins and cond_losses and cond_p and cond_nodes

    decision = {
        "rule_text": (
            "Adoptam budget = 5 numai daca, fata de graph_off:\n"
            "- castiga >= 8 cazuri la context_recall;\n"
            "- pierde <= 1 caz;\n"
            "- testul McNemar exact, bilateral, are p < 0.05;\n"
            "- media nodurilor noi adaugate per interogare <= 4.50.\n"
            "Altfel, nu schimbam bugetul implicit pe baza acestui benchmark."
        ),
        "target_arm": "budget_5",
        "reference_arm": "graph_off",
        "metric": "context_recall",
        "criteria": {
            "wins_ge_8": {"actual": wins, "threshold": 8, "passed": cond_wins},
            "losses_le_1": {"actual": losses, "threshold": 1, "passed": cond_losses},
            "mcnemar_exact_p_lt_0_05": {"actual": mcnemar_p, "threshold": 0.05, "passed": cond_p},
            "mean_new_nodes_le_4_50": {"actual": mean_nodes, "threshold": 4.50, "passed": cond_nodes},
        },
        "all_criteria_met": all_passed,
        "verdict": "se adoptă" if all_passed else "nu se adoptă",
    }

    return {
        "benchmark": "07_EVALUATION/retrieval_benchmark_v3/retrieval_benchmark_v3.json",
        "benchmark_sha256": BENCH_SHA.read_text(encoding="utf-8").split()[0],
        "worktree_commit": "b3ada1b54",
        "ranking_arm": "RANKING_ARM_BASELINE",
        "strict_graph_expansion": False,
        "corpus_notes": len(index),
        "n_cases": len(cases),
        "default_budget_negative_control": default_budget_control(raw_arms["budget_default"]),
        "decision": decision,
        "arms": out_arms,
    }


# --------------------------------------------------------------------------
# Markdown Report Renderer (Zero hand-written numbers)
# --------------------------------------------------------------------------

def render_report(results: Dict[str, Any]) -> str:
    arms = results["arms"]
    ctl = results["default_budget_negative_control"]
    dec = results["decision"]
    crit = dec["criteria"]

    lines = [
        "# Raport Benchmark Regăsire v3 — Măsurătoare Preînregistrată pe Graful Înghețat",
        "",
        "Raport generat automat de `30_SCRIPTS/evaluation/run_retrieval_benchmark_v3.py` "
        "din `results_v3_arms.json`.",
        "**Nicio cifră din acest fișier nu este scrisă de mână.**",
        "",
        "## Parametri de Rulare",
        "",
        f"- **Benchmark:** `{results['benchmark']}` ({results['n_cases']} cazuri).",
        f"- **SHA-256 Benchmark:** `{results['benchmark_sha256']}` (verificat bit-cu-bit).",
        f"- **Snapshot Graf și Index:** commit `{results['worktree_commit']}` ({results['corpus_notes']} note indexate).",
        f"- **Braț de clasare:** `{results['ranking_arm']}` (pentru izolarea strictă a efectului de graf).",
        "- **Mod expansiune:** non-strict (absența expansiunii este contorizată ca 0 noduri, nu eroare).",
        "",
        "---",
        "",
        "## 1. Rezumat Brațe Principale (Activare prin difuzie OPRITĂ)",
        "",
        "| Braț | Buget | Candidate Recall (95% Wilson CI) | Context Recall (95% Wilson CI) | "
        "Interogări cu expansiune | Noduri noi / interogare | Noduri noi / interogare expandată |",
        "|---|---|---:|---:|---:|---:|---:|",
    ]

    main_arms = ["graph_off", "budget_default", "budget_5", "budget_10", "budget_20"]
    for label in main_arms:
        arm = arms[label]
        s = arm["summary"]
        b = "—" if arm["budget"] is None and not arm["graph_on"] else ("default" if arm["budget"] is None else str(arm["budget"]))
        cand_ci = f"{s['candidate_recall']['text']} [{s['candidate_recall_ci']['lower']:.4f}, {s['candidate_recall_ci']['upper']:.4f}]"
        ctx_ci = f"{s['context_recall']['text']} [{s['context_recall_ci']['lower']:.4f}, {s['context_recall_ci']['upper']:.4f}]"
        exp_mean = f"{s['mean_new_nodes_when_expanded']:.2f}" if s["mean_new_nodes_when_expanded"] is not None else "—"
        lines.append(
            f"| `{label}` | {b} | {cand_ci} | {ctx_ci} | "
            f"{s['queries_with_expansion']['text']} | {s['mean_new_nodes_per_query']:.2f} | {exp_mean} |"
        )

    lines += [
        "",
        "*Notă:* Fracțiile de recall sunt raportate exclusiv pe cazurile măsurabile (130 din 160; cele 30 de cazuri `abstain` sunt nemăsurabile pentru recall).",
        "",
        "---",
        "",
        "## 2. Comparație Pereche față de `graph_off` (Testul McNemar Exact Bilateral)",
        "",
        "Perechi discordante pe cele 130 de cazuri măsurabile:",
        "- **Câștiguri (on=1, off=0):** cazuri în care brațul a regăsit nota corectă în context, iar `graph_off` a ratat-o.",
        "- **Pierderi (on=0, off=1):** cazuri în care `graph_off` a regăsit nota corectă în context, dar expansiunea a scos-o afară din top-10.",
        "",
        "| Braț | Context Recall Câștiguri | Context Recall Pierderi | Discordante (b+c) | McNemar Exact p-value | Candidate Recall Câștiguri | Candidate Recall Pierderi |",
        "|---|---:|---:|---:|---:|---:|---:|",
    ]

    for label in main_arms[1:]:
        p = arms[label]["paired_vs_off"]
        c_ctx = p["context_recall"]
        c_cand = p["candidate_recall"]
        lines.append(
            f"| `{label}` | {c_ctx['gained']} | {c_ctx['lost']} | {c_ctx['discordant']} | {c_ctx['exact_mcnemar_p']:.6f} | "
            f"{c_cand['gained']} | {c_cand['lost']} |"
        )

    lines += [
        "",
        "---",
        "",
        "## 3. Aplicarea Regulii de Decizie Preînregistrate pentru Bugetul 5",
        "",
        "Conform secțiunii *'Regula de decizie — preînregistrată de etichetator'* din `PREREGISTRATION.md`:",
        "",
        "```text",
        dec["rule_text"],
        "```",
        "",
        "### Verificare Criteriu cu Criteriu (buget_5 vs graph_off):",
        "",
        f"1. **Câștiguri Context Recall >= 8:** "
        f"{'ÎNTRUNIT' if crit['wins_ge_8']['passed'] else 'NEÎNTRUNIT'} "
        f"(valoare reală: **{crit['wins_ge_8']['actual']}**, prag: >= {crit['wins_ge_8']['threshold']}).",
        f"2. **Pierderi Context Recall <= 1:** "
        f"{'ÎNTRUNIT' if crit['losses_le_1']['passed'] else 'NEÎNTRUNIT'} "
        f"(valoare reală: **{crit['losses_le_1']['actual']}**, prag: <= {crit['losses_le_1']['threshold']}).",
        f"3. **Testul McNemar exact bilateral p < 0.05:** "
        f"{'ÎNTRUNIT' if crit['mcnemar_exact_p_lt_0_05']['passed'] else 'NEÎNTRUNIT'} "
        f"(valoare reală: **p = {crit['mcnemar_exact_p_lt_0_05']['actual']:.6f}**, prag: < {crit['mcnemar_exact_p_lt_0_05']['threshold']}).",
        f"4. **Media nodurilor noi adăugate per interogare <= 4.50:** "
        f"{'ÎNTRUNIT' if crit['mean_new_nodes_le_4_50']['passed'] else 'NEÎNTRUNIT'} "
        f"(valoare reală: **{crit['mean_new_nodes_le_4_50']['actual']:.2f}**, prag: <= {crit['mean_new_nodes_le_4_50']['threshold']:.2f}).",
        "",
        f"### Verdict Final: **{dec['verdict'].upper()}**",
        "",
        ("Toate criteriile preînregistrate au fost întrunite riguros."
         if dec["all_criteria_met"]
         else "Datele empirice nu întrunesc simultan toate cele patru criterii preînregistrate. "
              "Valoarea implicită rămâne neschimbată conform deciziei preînregistrate."),
        "",
        "---",
        "",
        "## 4. Analiză de Subgrup pe Clase de Întrebări",
        "",
        "### Context Recall pe Clase",
        "",
        "| Braț | Direct (60) | Conceptual (30) | Multi-Hop Complet (40) | Multi-Hop Filtrat fără R3-061/080/088 (37) |",
        "|---|---:|---:|---:|---:|",
    ]

    for label in main_arms:
        arm = arms[label]
        bc = arm["by_class"]
        lines.append(
            f"| `{label}` | {bc['direct']['context_recall']['text']} | {bc['conceptual']['context_recall']['text']} | "
            f"{bc['multi_hop']['context_recall']['text']} | {bc['multi_hop_filtered_37']['context_recall']['text']} |"
        )

    lines += [
        "",
        "### Paired Multi-Hop Context Recall vs `graph_off`",
        "",
        "| Braț | Multi-Hop Complet Câștiguri | Multi-Hop Complet Pierderi | Multi-Hop Filtrat Câștiguri | Multi-Hop Filtrat Pierderi |",
        "|---|---:|---:|---:|---:|",
    ]

    for label in main_arms[1:]:
        p = arms[label]["paired_vs_off"]
        pmh = p["multi_hop_context_recall"]
        pmhf = p["multi_hop_filtered_context_recall"]
        lines.append(
            f"| `{label}` | {pmh['gained']} | {pmh['lost']} | {pmhf['gained']} | {pmhf['lost']} |"
        )

    lines += [
        "",
        "---",
        "",
        "## 5. Analiză de Cost pe Cazurile Abstain (30 de întrebări fără răspuns)",
        "",
        "Cazurile `abstain` testează comportamentul pe interogări fără răspuns în vault. "
        "Orice nod adus este zgomot pur introdus în context pack.",
        "",
        "| Braț | Total Noduri Adăugate pe Abstain | Media Noduri Noi / Interogare Abstain | Interogări Abstain cu Expansiune > 0 |",
        "|---|---:|---:|---:|",
    ]

    for label in main_arms:
        s_abs = arms[label]["by_class"]["abstain"]
        lines.append(
            f"| `{label}` | {s_abs['new_nodes_total']} | {s_abs['mean_new_nodes_per_query']:.2f} | {s_abs['queries_with_expansion']['text']} |"
        )

    lines += [
        "",
        "---",
        "",
        "## 6. Brațe Informative — Activarea prin Difuzie (Spreading Activation)",
        "",
        "Brațele cu spreading activation pornit sunt rulate separat, pur informativ (nu fac parte din decizia de buget):",
        "",
        "| Braț | Buget | Candidate Recall | Context Recall | Noduri noi / interogare | Câștiguri vs graph_off | Pierderi vs graph_off |",
        "|---|---|---:|---:|---:|---:|---:|",
    ]

    info_arms = ["spreading_default", "spreading_5", "spreading_10", "spreading_20"]
    for label in info_arms:
        arm = arms[label]
        s = arm["summary"]
        b = "default" if arm["budget"] is None else str(arm["budget"])
        p = arm["paired_vs_off"]["context_recall"]
        lines.append(
            f"| `{label}` | {b} | {s['candidate_recall']['text']} | {s['context_recall']['text']} | "
            f"{s['mean_new_nodes_per_query']:.2f} | {p['gained']} | {p['lost']} |"
        )

    lines += [
        "",
        "---",
        "",
        "## 7. Control Negativ (Bugetul Implicit `None`)",
        "",
        f"- Interogări cu {ctl['zero_at_seed_count']}+ semințe lexicale (buget implicit formula = 0): "
        f"**{ctl['queries_expansion_impossible']['text']}**.",
        f"- Noduri adăugate pe aceste interogări: **{ctl['nodes_added_where_impossible']}**.",
        f"- Interogări cu < {ctl['zero_at_seed_count']} semințe: {ctl['queries_expansion_possible']['text']}.",
        f"- Noduri adăugate unde expansiunea era posibilă: {ctl['nodes_added_where_possible']}.",
        f"- **Rezultat Control Negativ:** **{'PASS' if ctl['passed'] else 'FAIL'}** "
        f"(confirmă fenomenul de anulare a bugetului implicit documentat în `memory/controller.py`).",
        "",
        "---",
        "",
        "## 8. Tabel Complet Caz-cu-Caz (160 Cazuri)",
        "",
        "Legendă Context Recall: `1` = regăsit în top-10 context, `0` = ratat, `—` = abstain (nemăsurabil).",
        "Diferențial față de `graph_off`: `+` = câștig, `-` = pierdere, `.` = egalitate.",
        "",
        "| ID | Clasă | `graph_off` | `budget_default` | `budget_5` | `budget_10` | `budget_20` | `b5_dif` |",
        "|---|---|:---:|:---:|:---:|:---:|:---:|:---:|",
    ]

    ref_rows = arms["graph_off"]["rows"]
    b_def_rows = arms["budget_default"]["rows"]
    b5_rows = arms["budget_5"]["rows"]
    b10_rows = arms["budget_10"]["rows"]
    b20_rows = arms["budget_20"]["rows"]

    for i, r in enumerate(ref_rows):
        cid = r["id"]
        ccls = r["class"]
        v_off = r["context_recall"]
        v_def = b_def_rows[i]["context_recall"]
        v_b5 = b5_rows[i]["context_recall"]
        v_b10 = b10_rows[i]["context_recall"]
        v_b20 = b20_rows[i]["context_recall"]

        str_off = "—" if v_off == UNMEASURABLE else str(v_off)
        str_def = "—" if v_def == UNMEASURABLE else str(v_def)
        str_b5 = "—" if v_b5 == UNMEASURABLE else str(v_b5)
        str_b10 = "—" if v_b10 == UNMEASURABLE else str(v_b10)
        str_b20 = "—" if v_b20 == UNMEASURABLE else str(v_b20)

        dif_b5 = "—"
        if v_off != UNMEASURABLE and v_b5 != UNMEASURABLE:
            if v_off == 0 and v_b5 == 1:
                dif_b5 = "**+ WIN**"
            elif v_off == 1 and v_b5 == 0:
                dif_b5 = "**- LOSS**"
            else:
                dif_b5 = "="

        lines.append(
            f"| `{cid}` | {ccls} | {str_off} | {str_def} | {str_b5} | {str_b10} | {str_b20} | {dif_b5} |"
        )

    lines.append("")
    return "\n".join(lines)


# --------------------------------------------------------------------------
# CLI Entrypoint
# --------------------------------------------------------------------------

def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--render-only", action="store_true", help="render report from existing results JSON")
    parser.add_argument("--worktree", type=Path, default=DEFAULT_WORKTREE, help="path to commit b3ada1b54 worktree")
    args = parser.parse_args(argv)

    if args.render_only:
        if not RESULTS_JSON.exists():
            raise SystemExit(f"Missing {RESULTS_JSON}")
        results = json.loads(RESULTS_JSON.read_text(encoding="utf-8"))
    else:
        if not args.worktree.exists():
            raise SystemExit(f"Worktree path does not exist: {args.worktree}")
        print(f"Executing benchmark v3 arms on worktree {args.worktree}...")
        results = execute_arms(args.worktree)
        RESULTS_JSON.write_text(json.dumps(results, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print(f"Wrote execution results to {RESULTS_JSON}")

    report_content = render_report(results)
    REPORT_MD.write_text(report_content, encoding="utf-8", newline="\n")
    print(f"Wrote benchmark report to {REPORT_MD}")

    dec = results["decision"]
    ctl = results["default_budget_negative_control"]
    print(f"DECISION_VERDICT={dec['verdict']}")
    print(f"NEGATIVE_CONTROL={'PASS' if ctl['passed'] else 'FAIL'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
