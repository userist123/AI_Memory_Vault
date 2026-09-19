"""General Transfer Benchmark Runner for Curriculum Modules.

Supports:
1. Complete profile-driven evaluation (control arm vs treatment arm).
2. Clean separation of model reading from judgment logic:
   - Raw responses saved to 07_EVALUATION/curriculum/raw_responses/<module_id>_responses.json
   - Pure offline judgment running on saved raw responses with zero model calls.
3. Paired statistical inference:
   - Wilson score 95% confidence intervals for proportion of supported answers in each arm.
   - Exact McNemar test on discordant pairs between control and treatment on identical questions.
   - Explicit determination of whether treatment gains are distinguishable from noise.
4. Generates both structured JSON report and Markdown results table.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple, Union

REPO = Path(__file__).resolve().parents[2]
PACKAGES_DIR = REPO / "03_IMPLEMENTATION" / "packages"
KNOWLEDGE_SCRIPTS = REPO / "30_SCRIPTS" / "knowledge"
for p in (str(REPO), str(PACKAGES_DIR), str(KNOWLEDGE_SCRIPTS)):
    if p not in sys.path:
        sys.path.insert(0, p)

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController, Lifecycle
from memory_controller.storage.file_engine import FileStorageEngine
from retrieval.vault_index import VaultIndex
from validate_curriculum_profile import validate_curriculum_profile

RAW_RESPONSES_DIR = REPO / "07_EVALUATION" / "curriculum" / "raw_responses"
REPORTS_DIR = REPO / "08_OBSERVABILITY" / "reports"
EVAL_RESULTS_DIR = REPO / "07_EVALUATION" / "curriculum" / "reports"

MAX_NOTES = 5
READER_MODEL = "gemini-3.1-flash-lite"


def wilson_score_interval(successes: int, total: int, confidence: float = 0.95) -> Tuple[float, float, float]:
    """Calculates Wilson score interval for binomial proportions.
    Returns (p_hat, lower_bound, upper_bound).
    """
    if total <= 0:
        return 0.0, 0.0, 0.0
    p_hat = successes / total
    z = 1.959963984540054  # 95% two-sided normal quantile
    denom = 1.0 + (z ** 2) / total
    center = (p_hat + (z ** 2) / (2.0 * total)) / denom
    margin = (z / denom) * math.sqrt((p_hat * (1.0 - p_hat) / total) + ((z ** 2) / (4.0 * (total ** 2))))
    lower = 0.0 if successes == 0 else max(0.0, center - margin)
    upper = 1.0 if successes == total else min(1.0, center + margin)
    return p_hat, lower, upper


def exact_mcnemar_test(b: int, c: int) -> Tuple[int, int, float, bool]:
    """Calculates exact two-sided McNemar test for paired binary data.
    b: control = FAIL, treatment = SUCCESS
    c: control = SUCCESS, treatment = FAIL
    Returns (b, c, p_value, is_significant_at_05).
    """
    n_d = b + c
    if n_d == 0:
        return b, c, 1.0, False
    k = max(b, c)
    tail_prob = sum(math.comb(n_d, i) * (0.5 ** n_d) for i in range(k, n_d + 1))
    p_val = min(1.0, 2.0 * tail_prob)
    return b, c, p_val, p_val < 0.05


def verify_evidence_quote(evidence_quote: Optional[str], notes_text: str) -> bool:
    if not evidence_quote:
        return False
    norm_quote = " ".join(evidence_quote.split())
    norm_text = " ".join(notes_text.split())
    return norm_quote in norm_text


def judge_answer(
    selected_choice: str,
    evidence_quote: Optional[str],
    is_trap: bool,
    choices: List[str],
    correct_answer: str,
    quote_verified: bool,
) -> str:
    valid_options = set(choices) | {"INSUFFICIENT"}
    if selected_choice not in valid_options:
        return "READER_FORMAT_ERROR"

    if is_trap:
        return "TRAP_PASS" if selected_choice == "INSUFFICIENT" else "TRAP_FAIL"
    else:
        if selected_choice == correct_answer:
            return "CORRECT_SUPPORTED" if quote_verified else "CORRECT_UNSUPPORTED"
        elif selected_choice == "INSUFFICIENT":
            return "ABSTAIN"
        else:
            return "WRONG"


def build_reader_prompt(question: str, choices: List[str], notes_text: str) -> str:
    choices_json = json.dumps(choices, indent=2, ensure_ascii=False)
    return (
        "You are an objective evaluation reader. Answer the multiple-choice question based EXCLUSIVELY "
        "on the Memory Notes below.\n\n"
        f"Memory Notes:\n{notes_text}\n\n"
        f"Question:\n{question}\n\n"
        f"Choices (select EXACTLY the text of one choice, or INSUFFICIENT):\n{choices_json}\n\n"
        "Rules:\n"
        "1. If the Memory Notes contain factual evidence proving one choice, output that choice text "
        "and the verbatim sentence from the Memory Notes as evidence_quote.\n"
        "2. If the Memory Notes do NOT contain sufficient factual evidence, output "
        'selected_choice = "INSUFFICIENT" and evidence_quote = null.\n'
        "3. Never use pre-trained knowledge. Only use what is in the Memory Notes.\n"
        "4. Output ONLY valid JSON:\n"
        '{"selected_choice": "<choice text or INSUFFICIENT>", "evidence_quote": "<verbatim sentence or null>"}'
    )


def call_reader(prompt: str, model: Any, max_retries: int = 5) -> Dict[str, Any]:
    from google.api_core.exceptions import ResourceExhausted

    for attempt in range(max_retries):
        try:
            resp = model.generate_content(
                prompt,
                generation_config={"response_mime_type": "application/json", "temperature": 0.0},
            )
            usage = resp.usage_metadata
            try:
                result = json.loads(resp.text)
            except Exception:
                result = {"selected_choice": "READER_FORMAT_ERROR", "evidence_quote": None}
            return {
                "selected_choice": result.get("selected_choice", "READER_FORMAT_ERROR"),
                "evidence_quote": result.get("evidence_quote"),
                "prompt_tokens": getattr(usage, "prompt_token_count", 0) if usage else 0,
                "completion_tokens": getattr(usage, "candidates_token_count", 0) if usage else 0,
                "total_tokens": getattr(usage, "total_token_count", 0) if usage else 0,
            }
        except ResourceExhausted as exc:
            wait = 30 * (2 ** attempt)
            print(f"\n  [quota] 429 on attempt {attempt + 1}/{max_retries}, waiting {wait}s... ({exc})", flush=True)
            time.sleep(wait)
        except Exception as e:
            time.sleep(3)
            if attempt == max_retries - 1:
                return {"selected_choice": f"ERROR: {e}", "evidence_quote": None, "total_tokens": 0}

    return {"selected_choice": "QUOTA_ERROR", "evidence_quote": None, "total_tokens": 0}


def verify_quote_in_module(evidence_quote: Optional[str], module_id: str, repo_root: Path = REPO) -> bool:
    if not evidence_quote:
        return False
    knowledge_dir = repo_root / "01_ARCHITECTURE" / "knowledge"
    # Match notes by module_id prefix or openstax prefix
    prefix = module_id.split("-v")[0].replace("-", "_")
    matched_files = list(knowledge_dir.glob(f"*{prefix}*.md"))
    if not matched_files and "psychology" in module_id:
        matched_files = list(knowledge_dir.glob("openstax_psy2e_8_*.md"))
    if not matched_files:
        matched_files = list(knowledge_dir.glob(f"*{module_id}*.md"))

    norm_quote = " ".join(evidence_quote.split())
    for f in matched_files:
        txt = f.read_text(encoding="utf-8")
        norm_txt = " ".join(txt.split())
        if norm_quote in norm_txt:
            return True
    return False


def evaluate_raw_responses(
    raw_responses_data: Dict[str, Any],
    frozen_test_set: Dict[str, Any],
) -> Dict[str, Any]:
    """Pure offline evaluation function: grades reader responses without any model calls."""
    q_map = {q["id"]: q for q in frozen_test_set["questions"]}
    responses = raw_responses_data["responses"]
    module_id = raw_responses_data.get("module_id", "curriculum-module")

    control_eval: List[Dict[str, Any]] = []
    treatment_eval: List[Dict[str, Any]] = []

    for r in responses:
        qid = r["question_id"]
        q_spec = q_map.get(qid, {})
        is_trap = q_spec.get("unanswerable", False) or (r.get("type") == "unanswerable_trap")
        correct_answer = q_spec.get("correct_answer", r.get("correct_answer", ""))
        choices = q_spec.get("choices", r.get("choices", []))

        selected = r.get("selected_choice", "")
        evidence_quote = r.get("evidence_quote")

        # Quote verification
        quote_verified = False
        notes_text = r.get("notes_text", "")
        if notes_text and evidence_quote:
            quote_verified = verify_evidence_quote(evidence_quote, notes_text)
        elif "quote_verified" in r:
            quote_verified = bool(r["quote_verified"])
        elif evidence_quote:
            quote_verified = verify_quote_in_module(evidence_quote, module_id)

        verdict = judge_answer(
            selected_choice=selected,
            evidence_quote=evidence_quote,
            is_trap=is_trap,
            choices=choices,
            correct_answer=correct_answer,
            quote_verified=quote_verified,
        )

        item = {
            "id": qid,
            "type": r.get("type", q_spec.get("type", "unknown")),
            "question": (r.get("question") or q_spec.get("question", ""))[:80] + "...",
            "correct_answer": correct_answer,
            "selected_choice": selected,
            "verdict": verdict,
            "evidence_quote": evidence_quote,
            "quote_verified": quote_verified,
            "notes_retrieved_count": r.get("notes_retrieved_count", 0),
            "module_notes_in_retrieved": r.get("openstax_notes_in_retrieved") or r.get("module_notes_in_retrieved", 0),
            "tokens_used": r.get("tokens_used", 0),
        }

        if r.get("arm") == "control":
            control_eval.append(item)
        else:
            treatment_eval.append(item)

    def summarize_arm(arm_label: str, results: List[Dict[str, Any]]) -> Dict[str, Any]:
        rq = [x for x in results if x["type"] == "author_review"]
        tq = [x for x in results if x["type"] == "unanswerable_trap"]

        n_rq = len(rq)
        n_tq = len(tq)

        def count(lst, pred):
            return sum(1 for x in lst if pred(x))

        c_supp = count(rq, lambda x: x["verdict"] == "CORRECT_SUPPORTED")
        c_unsupp = count(rq, lambda x: x["verdict"] == "CORRECT_UNSUPPORTED")
        c_tot = count(rq, lambda x: x["verdict"].startswith("CORRECT"))
        c_abs = count(rq, lambda x: x["verdict"] == "ABSTAIN")
        c_wrg = count(rq, lambda x: x["verdict"] == "WRONG")
        c_fmt = count(rq, lambda x: x["verdict"] == "READER_FORMAT_ERROR")

        t_pass = count(tq, lambda x: x["verdict"] == "TRAP_PASS")
        t_fail = count(tq, lambda x: x["verdict"] == "TRAP_FAIL")
        t_fmt = count(tq, lambda x: x["verdict"] == "READER_FORMAT_ERROR")

        p_supp, w_low, w_high = wilson_score_interval(c_supp, n_rq, 0.95)

        return {
            "arm": arm_label,
            "review_questions": {
                "total": n_rq,
                "correct_supported": f"{c_supp}/{n_rq}",
                "correct_supported_count": c_supp,
                "correct_unsupported": f"{c_unsupp}/{n_rq}",
                "correct_total": f"{c_tot}/{n_rq}",
                "abstain": f"{c_abs}/{n_rq}",
                "wrong": f"{c_wrg}/{n_rq}",
                "reader_format_error": f"{c_fmt}/{n_rq}",
                "wilson_95_ci": {
                    "estimate": round(p_supp, 4),
                    "lower": round(w_low, 4),
                    "upper": round(w_high, 4),
                    "ci_range_str": f"[{w_low:.3f}, {w_high:.3f}]",
                },
            },
            "trap_questions": {
                "total": n_tq,
                "trap_pass": f"{t_pass}/{n_tq}",
                "trap_pass_count": t_pass,
                "trap_fail": f"{t_fail}/{n_tq}",
                "reader_format_error": f"{t_fmt}/{n_tq}",
            },
            "total_tokens_used": sum(x["tokens_used"] for x in results),
            "per_question": results,
        }

    c_summary = summarize_arm("control", control_eval)
    t_summary = summarize_arm("treatment", treatment_eval)

    # Paired McNemar Test on Review Questions
    c_map = {x["id"]: x for x in control_eval if x["type"] == "author_review"}
    t_map = {x["id"]: x for x in treatment_eval if x["type"] == "author_review"}

    paired_table = {"a": 0, "b": 0, "c": 0, "d": 0}
    paired_comparison_rows = []

    for qid in sorted(q_map.keys()):
        if q_map[qid].get("type") != "author_review":
            continue
        c_res = c_map.get(qid, {})
        t_res = t_map.get(qid, {})

        c_pass = (c_res.get("verdict") == "CORRECT_SUPPORTED")
        t_pass = (t_res.get("verdict") == "CORRECT_SUPPORTED")

        if c_pass and t_pass:
            paired_table["a"] += 1
            change_type = "both_passed"
        elif not c_pass and t_pass:
            paired_table["b"] += 1
            change_type = "improved"
        elif c_pass and not t_pass:
            paired_table["c"] += 1
            change_type = "regressed"
        else:
            paired_table["d"] += 1
            change_type = "both_failed"

        paired_comparison_rows.append({
            "id": qid,
            "control_choice": c_res.get("selected_choice"),
            "control_verdict": c_res.get("verdict"),
            "treatment_choice": t_res.get("selected_choice"),
            "treatment_verdict": t_res.get("verdict"),
            "change": change_type,
        })

    b = paired_table["b"]
    c = paired_table["c"]
    _, _, p_val, is_sig = exact_mcnemar_test(b, c)

    if is_sig:
        significance_statement = (
            f"Statistically significant difference (exact McNemar two-sided p={p_val:.4f} < 0.05). "
            f"Treatment demonstrates genuine transfer beyond random noise (improved: {b}, regressed: {c})."
        )
    else:
        significance_statement = (
            f"Difference is indistinguishable from noise at alpha=0.05 (exact McNemar two-sided p={p_val:.4f} >= 0.05). "
            f"With small sample size N={len(paired_comparison_rows)}, {b} improvements vs {c} regressions do not clear the significance threshold."
        )

    module_id = raw_responses_data.get("module_id", "curriculum-module")
    return {
        "schema": "curriculum-transfer-benchmark-v2",
        "module_id": module_id,
        "source": raw_responses_data.get("source"),
        "model": raw_responses_data.get("model", READER_MODEL),
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "paired_statistics": {
            "sample_size_n": len(paired_comparison_rows),
            "contingency_table": paired_table,
            "mcnemar_exact": {
                "b_improved": b,
                "c_regressed": c,
                "two_sided_p_value": round(p_val, 5),
                "is_significant_at_alpha_05": is_sig,
                "interpretation": significance_statement,
            },
        },
        "control_arm": c_summary,
        "treatment_arm": t_summary,
        "paired_per_question": paired_comparison_rows,
    }


def format_markdown_report(eval_results: Dict[str, Any]) -> str:
    c_arm = eval_results["control_arm"]
    t_arm = eval_results["treatment_arm"]
    stats = eval_results["paired_statistics"]
    mcn = stats["mcnemar_exact"]

    c_rq = c_arm["review_questions"]
    t_rq = t_arm["review_questions"]
    c_tq = c_arm["trap_questions"]
    t_tq = t_arm["trap_questions"]

    lines = [
        f"# Curriculum Transfer Benchmark Report: {eval_results.get('module_id')}",
        "",
        f"- **Source**: {eval_results.get('source')}",
        f"- **Reader Model**: `{eval_results.get('model')}`",
        f"- **Timestamp**: {eval_results.get('timestamp')}",
        f"- **Sample Size**: N = {stats['sample_size_n']} review questions, {c_tq['total']} traps",
        "",
        "## 1. Summary Comparison Table",
        "",
        "| Metric | Control Arm (No Module Notes) | Treatment Arm (With Module Notes) | Difference / Change |",
        "|---|---|---|---|",
        f"| **Supported Answers (n={c_rq['total']})** | {c_rq['correct_supported']} ({c_rq['wilson_95_ci']['ci_range_str']}) | {t_rq['correct_supported']} ({t_rq['wilson_95_ci']['ci_range_str']}) | +{t_rq['correct_supported_count'] - c_rq['correct_supported_count']} net gained |",
        f"| **Unsupported / Guessed** | {c_rq['correct_unsupported']} | {t_rq['correct_unsupported']} | - |",
        f"| **Abstained (INSUFFICIENT)** | {c_rq['abstain']} | {t_rq['abstain']} | - |",
        f"| **Wrong Answers** | {c_rq['wrong']} | {t_rq['wrong']} | - |",
        f"| **Trap Pass Rate (n={c_tq['total']})** | {c_tq['trap_pass']} | {t_tq['trap_pass']} | - |",
        "",
        "## 2. Paired McNemar Statistical Test",
        "",
        f"- **Contingency Table**: [Both pass: {stats['contingency_table']['a']}, Improved (Control 0 -> Treatment 1): {mcn['b_improved']}, Regressed: {mcn['c_regressed']}, Both fail: {stats['contingency_table']['d']}]",
        f"- **Exact Two-Sided p-value**: `{mcn['two_sided_p_value']:.5f}`",
        f"- **Significant at $\\alpha = 0.05$**: **{'YES' if mcn['is_significant_at_alpha_05'] else 'NO'}**",
        f"- **Conclusion**: {mcn['interpretation']}",
        "",
        "## 3. Per-Question Paired Comparison",
        "",
        "| Question ID | Control Choice | Control Verdict | Treatment Choice | Treatment Verdict | Shift |",
        "|---|---|---|---|---|---|",
    ]

    for row in eval_results["paired_per_question"]:
        lines.append(
            f"| `{row['id']}` | {row['control_choice']} | `{row['control_verdict']}` | {row['treatment_choice']} | `{row['treatment_verdict']}` | **{row['change']}** |"
        )

    return "\n".join(lines)


def run_full_curriculum_eval(
    profile_path: Union[str, Path],
    offline_only: bool = False,
) -> Dict[str, Any]:
    p_path = Path(profile_path)
    ok, errors = validate_curriculum_profile(p_path, check_referenced_files=True)
    if not ok:
        raise ValueError(f"Profile validation failed: {errors}")

    profile = json.loads(p_path.read_text(encoding="utf-8"))
    module_id = profile["module_id"]
    source_cfg = profile["source"]
    eval_cfg = profile["evaluation"]

    test_set_path = REPO / eval_cfg["frozen_test_set"]
    test_data = json.loads(test_set_path.read_text(encoding="utf-8"))
    questions = test_data["questions"]

    raw_responses_file = RAW_RESPONSES_DIR / f"{module_id}_responses.json"

    if offline_only:
        if not raw_responses_file.exists():
            raise FileNotFoundError(f"Raw responses file not found for offline evaluation: {raw_responses_file}")
        raw_data = json.loads(raw_responses_file.read_text(encoding="utf-8"))
    else:
        api_key = os.environ.get("GEMINI_API_KEY")
        if not api_key:
            raise RuntimeError("GEMINI_API_KEY is required for online evaluation")

        import google.generativeai as genai
        genai.configure(api_key=api_key)
        model = genai.GenerativeModel(READER_MODEL)

        storage = FileStorageEngine(str(REPO))
        full_index = VaultIndex.load(REPO, lifecycles=["ACTIVE", "VERIFIED", "REVIEW"])

        # Identify notes for this module
        module_tag = profile.get("domain", "")
        module_notes = {
            n.id for n in full_index.notes
            if module_id in (n.meta.get("provenance", {}) or {}).get("source_ref", "")
            or f"openstax_psy2e" in (n.meta.get("provenance", {}) or {}).get("source_ref", "")
        }

        def execute_arm_online(arm_name: str, include_module_notes: bool) -> List[Dict[str, Any]]:
            if include_module_notes:
                arm_index = full_index
                arm_storage = storage
            else:
                arm_storage = FileStorageEngine(str(REPO))
                arm_storage.id_to_path = {k: v for k, v in storage.id_to_path.items() if k not in module_notes}
                arm_index = VaultIndex([n for n in full_index.notes if n.id not in module_notes])

            controller = MemoryController(storage=arm_storage, index=arm_index)
            arm_responses = []

            for q in questions:
                print(f"  [{arm_name.upper()}] {q['id']}...", end=" ", flush=True)
                search_res = controller.search(
                    Principal.AI_AGENT,
                    query=q["question"],
                    page_size=MAX_NOTES,
                    lifecycles=[Lifecycle.REVIEW, Lifecycle.ACTIVE],
                )
                retrieved_ids = [it.get("id") for it in search_res.get("results", []) if it.get("id")]
                notes_parts = []
                for nid in retrieved_ids:
                    note = arm_storage.get(nid)
                    if note:
                        c = note.get("content", "") or ""
                        if len(c) > 4500:
                            c = c[:4500]
                        notes_parts.append(c)

                notes_text = "\n\n".join(notes_parts)
                mod_count = sum(1 for nid in retrieved_ids if nid in module_notes)

                if not notes_text:
                    call_res = {"selected_choice": "INSUFFICIENT", "evidence_quote": None, "total_tokens": 0}
                else:
                    prompt = build_reader_prompt(q["question"], q.get("choices", []), notes_text)
                    call_res = call_reader(prompt, model)

                arm_responses.append({
                    "question_id": q["id"],
                    "arm": arm_name,
                    "type": q.get("type", "unknown"),
                    "question": q["question"],
                    "choices": q.get("choices", []),
                    "correct_answer": q.get("correct_answer", ""),
                    "notes_text": notes_text,
                    "notes_retrieved_count": len(notes_parts),
                    "module_notes_in_retrieved": mod_count,
                    "selected_choice": call_res.get("selected_choice"),
                    "evidence_quote": call_res.get("evidence_quote"),
                    "tokens_used": call_res.get("total_tokens", 0),
                })
                print(f"{call_res.get('selected_choice')}")
                time.sleep(4)

            return arm_responses

        print(f"\n=== Running Online Reader for {module_id} ===")
        print("--- Control Arm ---")
        ctrl_responses = execute_arm_online("control", include_module_notes=False)
        print("--- Treatment Arm ---")
        treat_responses = execute_arm_online("treatment", include_module_notes=True)

        raw_data = {
            "module_id": module_id,
            "source": f"{source_cfg.get('work_title')} - {source_cfg.get('chapter_or_topic')}",
            "model": READER_MODEL,
            "timestamp": datetime.now(timezone.utc).isoformat(),
            "responses": ctrl_responses + treat_responses,
        }
        RAW_RESPONSES_DIR.mkdir(parents=True, exist_ok=True)
        raw_responses_file.write_text(json.dumps(raw_data, indent=2, ensure_ascii=False), encoding="utf-8")
        print(f"Saved raw responses to {raw_responses_file}")

    # Pure offline evaluation
    eval_results = evaluate_raw_responses(raw_data, test_data)

    # Save outputs
    REPORTS_DIR.mkdir(parents=True, exist_ok=True)
    EVAL_RESULTS_DIR.mkdir(parents=True, exist_ok=True)

    report_json_path = REPORTS_DIR / f"curriculum_heldout_eval_{module_id}.json"
    report_json_path.write_text(json.dumps(eval_results, indent=2, ensure_ascii=False), encoding="utf-8")

    md_report = format_markdown_report(eval_results)
    report_md_path = EVAL_RESULTS_DIR / f"TRANSFER_BENCHMARK_{module_id.upper()}.md"
    report_md_path.write_text(md_report, encoding="utf-8")

    print(f"\nEvaluation complete!")
    print(f"JSON Report: {report_json_path}")
    print(f"Markdown Report: {report_md_path}")
    print("\n" + md_report[:800] + "...\n")
    return eval_results


def main() -> int:
    parser = argparse.ArgumentParser(description="General transfer benchmark evaluation runner.")
    parser.add_argument("--profile", type=Path, required=True, help="Path to curriculum module profile JSON")
    parser.add_argument("--offline-only", action="store_true", help="Perform offline judgment on saved raw responses")
    args = parser.parse_args()

    run_full_curriculum_eval(args.profile, offline_only=args.offline_only)
    return 0


if __name__ == "__main__":
    sys.exit(main())
