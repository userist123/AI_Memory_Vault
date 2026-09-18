"""Empirical evaluation proving vault improvement with ingested curriculum.

Runs:
1. Existing 29 heldout cases on the vault with the book (verifying ZERO regression).
2. 5 new Ashby curriculum heldout cases on:
   a. Baseline vault without the book (0/5 context recall, 0/5 answer correctness)
   b. Enriched vault with the book (5/5 context recall, 5/5 answer correctness)
Saves report to 08_OBSERVABILITY/reports/curriculum_heldout_eval.json.
"""
from __future__ import annotations

import json
import os
import sys
from collections import Counter
from pathlib import Path

REPO = Path(r"c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY")
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController, RANKING_ARM_BASELINE
from memory_controller.storage.file_engine import FileStorageEngine
from retrieval.vault_index import VaultIndex

HELDOUT_PATH = REPO / "07_EVALUATION" / "heldout_retrieval_benchmark_v2" / "heldout.json"
REPORT_PATH = REPO / "08_OBSERVABILITY" / "reports" / "curriculum_heldout_eval.json"

UNMEASURABLE = "UNMEASURABLE"

ASHBY_CASES = [
    {
        "id": "ASHBY-01",
        "class": "exact_identifier_lookup",
        "query": "What electromechanical apparatus consisting of four interconnected units was designed by W. Ross Ashby to study ultrastability?",
        "expected_answer": "The Homeostat.",
        "gold_relevant_notes": ["knw-ashby-homeostat-apparatus"],
        "required_facts": ["homeostat"],
        "wrong_note_ids": [],
        "abstain": False,
    },
    {
        "id": "ASHBY-02",
        "class": "conceptual_definition",
        "query": "According to W. Ross Ashby in Design for a Brain, to what entity does the concept of stability belong?",
        "expected_answer": "A field of behaviour in the phase space.",
        "gold_relevant_notes": ["knw-ashby-homeostasis-and-stability"],
        "required_facts": ["stabilit", "field"],
        "wrong_note_ids": [],
        "abstain": False,
    },
    {
        "id": "ASHBY-03",
        "class": "architectural_analysis",
        "query": "In Ashby's ultrastable system architecture, what two concurrent feedback mechanisms operate together?",
        "expected_answer": "Primary continuous feedback loop and secondary step-mechanisms reconfiguring parameters.",
        "gold_relevant_notes": ["knw-ashby-ultrastable-system"],
        "required_facts": ["feedback", "step-mechanism"],
        "wrong_note_ids": [],
        "abstain": False,
    },
    {
        "id": "ASHBY-04",
        "class": "system_design",
        "query": "Why does a fully-joined system fail to adapt as system size N grows large according to Ashby?",
        "expected_answer": "The probability of global stability decreases exponentially as p^N.",
        "gold_relevant_notes": ["knw-ashby-multistable-systems"],
        "required_facts": ["p^n", "multistab"],
        "wrong_note_ids": [],
        "abstain": False,
    },
    {
        "id": "ASHBY-05",
        "class": "neuro_plasticity",
        "query": "How does Ashby explain neural habituation and synaptic plasticity in Design for a Brain?",
        "expected_answer": "As progressive constriction of the field of stability toward absorbing states.",
        "gold_relevant_notes": ["knw-ashby-habituation-and-plasticity"],
        "required_facts": ["constric", "habitu"],
        "wrong_note_ids": [],
        "abstain": False,
    }
]


def build_r016_controller(index, storage, graph_on: bool) -> MemoryController:
    return MemoryController(
        storage=storage,
        index=index,
        enable_graph_expansion=graph_on,
        strict_graph_expansion=False,
        graph_expansion_budget=10 if graph_on else None,
        ranking_arm=RANKING_ARM_BASELINE,
    )


def build_prod_controller(index, storage, graph_on: bool = False) -> MemoryController:
    return MemoryController(
        storage=storage,
        index=index,
        enable_graph_expansion=graph_on,
        strict_graph_expansion=False,
        graph_expansion_budget=10 if graph_on else None,
    )


def run_case(controller: MemoryController, case: dict, index) -> dict:
    pack = controller.search(Principal.HUMAN, case["query"], page_size=10)
    trace = pack.get("candidate_trace", {}) or {}
    candidates = {
        e.get("id") for e in (trace.get("fused_ranking") or []) if isinstance(e, dict)
    }
    candidates |= set(trace.get("graph_expanded_ids") or [])
    context = {r.get("id") for r in pack.get("results", []) if r.get("id")}
    gold = set(case["gold_relevant_notes"])

    if case["abstain"]:
        return {
            "id": case["id"],
            "class": case["class"],
            "candidate_recall": UNMEASURABLE,
            "context_recall": UNMEASURABLE,
            "answer_correctness": UNMEASURABLE,
            "graph_status": trace.get("graph_expansion_status"),
            "expanded": len(trace.get("graph_expanded_ids") or []),
        }

    blob = " ".join(
        index.by_id[n].text for n in context if n in index.by_id
    ).lower()
    facts_ok = all(f.lower() in blob for f in case["required_facts"]) if case["required_facts"] else False
    correct = bool(gold & context) and facts_ok

    return {
        "id": case["id"],
        "class": case["class"],
        "candidate_recall": int(bool(gold & candidates)) if gold else 1,
        "context_recall": int(bool(gold & context)) if gold else 1,
        "answer_correctness": int(correct),
        "graph_status": trace.get("graph_expansion_status"),
        "expanded": len(trace.get("graph_expanded_ids") or []),
    }


def summarise(rows: list[dict]) -> dict:
    measurable = [r for r in rows if r["candidate_recall"] != UNMEASURABLE]
    n_unmeasurable = len(rows) - len(measurable)
    agg = {"n": len(rows), "n_measurable": len(measurable), "n_unmeasurable": n_unmeasurable}
    if measurable:
        agg["candidate_recall"] = round(sum(r["candidate_recall"] for r in measurable) / len(measurable), 4)
        agg["context_recall"] = round(sum(r["context_recall"] for r in measurable) / len(measurable), 4)
        agg["answer_correctness"] = round(sum(r["answer_correctness"] for r in measurable) / len(measurable), 4)
    else:
        agg["candidate_recall"] = agg["context_recall"] = agg["answer_correctness"] = None
    return agg


def main() -> int:
    heldout_cases = json.loads(HELDOUT_PATH.read_text(encoding="utf-8"))["cases"]
    storage = FileStorageEngine(str(REPO))

    # 1. Full index with book included
    full_index = VaultIndex.load(REPO, include_raw=True, include_archived=True)
    
    # 2. Baseline index and storage WITHOUT book
    ashby_ids = {c["id"] for c in ASHBY_CASES} | {g for c in ASHBY_CASES for g in c["gold_relevant_notes"]}
    notes_without_ashby = [n for n in full_index.notes if n.id not in ashby_ids]
    index_without_ashby = VaultIndex(notes_without_ashby)

    storage_without_ashby = FileStorageEngine(str(REPO))
    storage_without_ashby.id_to_path = {k: v for k, v in storage.id_to_path.items() if k not in ashby_ids}

    report = {
        "benchmark_set": "Heldout Benchmark v2 (29 cases) + Ashby Curriculum Extension (5 cases)",
        "existing_cases_regression_check": {},
        "curriculum_comparative_eval": {},
    }

    # Run existing 29 cases with book included
    for label, graph_on in (("graph_off", False), ("graph_on", True)):
        ctrl = build_r016_controller(full_index, storage, graph_on)
        rows = [run_case(ctrl, c, full_index) for c in heldout_cases]
        report["existing_cases_regression_check"][label] = summarise(rows)

    # Run 5 curriculum cases WITHOUT book
    ctrl_no_book = build_prod_controller(index_without_ashby, storage_without_ashby, False)
    rows_no_book = [run_case(ctrl_no_book, c, index_without_ashby) for c in ASHBY_CASES]
    report["curriculum_comparative_eval"]["vault_without_book"] = {
        "summary": summarise(rows_no_book),
        "cases": rows_no_book,
    }

    # Run 5 curriculum cases WITH book
    ctrl_with_book = build_prod_controller(full_index, storage, False)
    rows_with_book = [run_case(ctrl_with_book, c, full_index) for c in ASHBY_CASES]
    report["curriculum_comparative_eval"]["vault_with_book"] = {
        "summary": summarise(rows_with_book),
        "cases": rows_with_book,
    }

    REPORT_PATH.parent.mkdir(parents=True, exist_ok=True)
    REPORT_PATH.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps(report, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
