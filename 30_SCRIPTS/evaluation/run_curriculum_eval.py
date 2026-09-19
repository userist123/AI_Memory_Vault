"""Transfer benchmark: OpenStax Ch8 Memory — dual-arm evaluation.

Control arm: vault WITHOUT the 16 new openstax_psy2e_8_* notes.
Treatment arm: vault WITH the 16 new openstax_psy2e_8_* notes (lifecycle=REVIEW).

Reader: gemini-3.1-flash-lite, temperature=0, pure note-grounded reasoning.
- Answerable: correct + verbatim quote found in notes  CORRECT_SUPPORTED
- Answerable: correct + no quote / invented quote       CORRECT_UNSUPPORTED
- Answerable: wrong choice                              WRONG
- Answerable: INSUFFICIENT                              ABSTAIN
- Trap: INSUFFICIENT                                    TRAP_PASS (correct behavior)
- Trap: any choice                                      TRAP_FAIL (hallucination)

Saves to 08_OBSERVABILITY/reports/curriculum_heldout_eval.json.
"""
from __future__ import annotations

import json
import os
import sys
import time
from pathlib import Path
from typing import Any, Dict, List

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO.resolve()))
sys.path.insert(0, str((REPO / "03_IMPLEMENTATION" / "packages").resolve()))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

import google.generativeai as genai  # noqa: E402
from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller.controller import MemoryController, Lifecycle  # noqa: E402
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402
from retrieval.vault_index import VaultIndex  # noqa: E402

FROZEN_TEST_PATH = REPO / "07_EVALUATION" / "curriculum" / "openstax_ch08_frozen_test_set.json"
REPORT_PATH = REPO / "08_OBSERVABILITY" / "reports" / "curriculum_heldout_eval.json"

OPENSTAX_NOTE_PATTERN = "openstax-psychology-2e-ch08"
MAX_NOTES = 5  # AGENTS.md: MAX_MEMORY_RESULTS = 5
READER_MODEL = "gemini-3.1-flash-lite"


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
    import time as _time
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
                result = {"selected_choice": "PARSE_ERROR", "evidence_quote": None}
            return {
                "selected_choice": result.get("selected_choice", "PARSE_ERROR"),
                "evidence_quote": result.get("evidence_quote"),
                "prompt_tokens": getattr(usage, "prompt_token_count", 0),
                "completion_tokens": getattr(usage, "candidates_token_count", 0),
                "total_tokens": getattr(usage, "total_token_count", 0),
            }
        except ResourceExhausted as exc:
            wait = 30 * (2 ** attempt)
            print(f"\n  [quota] 429 on attempt {attempt + 1}/{max_retries}, waiting {wait}s... ({exc})", flush=True)
            _time.sleep(wait)
    # All retries exhausted
    return {"selected_choice": "QUOTA_ERROR", "evidence_quote": None, "total_tokens": 0}



def verify_evidence_quote(evidence_quote, notes_text: str) -> bool:
    if not evidence_quote:
        return False
    norm_quote = " ".join(evidence_quote.split())
    norm_text = " ".join(notes_text.split())
    return norm_quote in norm_text


def evaluate_question(
    q: Dict[str, Any],
    controller: MemoryController,
    storage: FileStorageEngine,
    model: Any,
    openstax_ids: set,
) -> Dict[str, Any]:
    is_trap = q.get("unanswerable", False)
    correct_answer = q.get("correct_answer", "")
    choices = q.get("choices", [])

    search_res = controller.search(
        Principal.AI_AGENT,
        query=q["question"],
        page_size=MAX_NOTES,
        lifecycles=[Lifecycle.REVIEW, Lifecycle.ACTIVE],
    )
    retrieved_ids = [it.get("id") for it in search_res.get("results", []) if it.get("id")]

    MAX_NOTE_CHARS = 4500
    notes_parts = []
    for nid in retrieved_ids:
        note = storage.get(nid)
        if note:
            c = note.get("content", "") or ""
            if len(c) > MAX_NOTE_CHARS:
                c = c[:MAX_NOTE_CHARS]
            notes_parts.append(c)
    notes_text = "\n\n".join(p for p in notes_parts if p)

    # Count how many of the retrieved IDs are OpenStax notes (using pre-computed set from index)
    openstax_count = sum(1 for nid in retrieved_ids if nid in openstax_ids)

    if not notes_text:
        reader_result = {"selected_choice": "INSUFFICIENT", "evidence_quote": None, "total_tokens": 0}
    else:
        prompt = build_reader_prompt(q["question"], choices, notes_text)
        reader_result = call_reader(prompt, model)


    selected = reader_result["selected_choice"]
    evidence_quote = reader_result["evidence_quote"]
    quote_verified = verify_evidence_quote(evidence_quote, notes_text)

    if is_trap:
        verdict = "TRAP_PASS" if selected == "INSUFFICIENT" else "TRAP_FAIL"
    else:
        if selected == correct_answer:
            verdict = "CORRECT_SUPPORTED" if quote_verified else "CORRECT_UNSUPPORTED"
        elif selected == "INSUFFICIENT":
            verdict = "ABSTAIN"
        else:
            verdict = "WRONG"

    return {
        "id": q["id"],
        "type": q.get("type", "unknown"),
        "question": q["question"][:80] + "...",
        "correct_answer": correct_answer,
        "selected_choice": selected,
        "verdict": verdict,
        "evidence_quote": evidence_quote,
        "quote_verified": quote_verified,
        "notes_retrieved_count": len(notes_parts),
        "openstax_notes_in_retrieved": openstax_count,
        "tokens_used": reader_result.get("total_tokens", 0),
    }


def run_arm(questions: List[Dict[str, Any]], include_openstax: bool, model: Any) -> Dict[str, Any]:
    storage = FileStorageEngine(str(REPO))
    full_index = VaultIndex.load(REPO, lifecycles=["ACTIVE", "VERIFIED", "REVIEW"])
    openstax_ids = {
        n.id for n in full_index.notes
        if OPENSTAX_NOTE_PATTERN in (n.meta.get("provenance", {}) or {}).get("source_ref", "")
    }

    if include_openstax:
        index = full_index
    else:
        storage.id_to_path = {k: v for k, v in storage.id_to_path.items() if k not in openstax_ids}
        index = VaultIndex([n for n in full_index.notes if n.id not in openstax_ids])
        print(f"  Excluded {len(openstax_ids)} OpenStax notes from control arm index and storage.")

    controller = MemoryController(storage=storage, index=index)

    results = []
    for q in questions:
        arm_label = "TREATMENT" if include_openstax else "CONTROL"
        print(f"  [{arm_label}] {q['id']}...", end=" ", flush=True)
        res = evaluate_question(q, controller, storage, model, openstax_ids)
        results.append(res)
        print(f"{res['verdict']} (notes={res['notes_retrieved_count']} openstax={res['openstax_notes_in_retrieved']})")
        time.sleep(5)  # rate-limit: avoid 250k token/min quota on free tier


    review_q = [r for r in results if r["type"] == "author_review"]
    trap_q = [r for r in results if r["type"] == "unanswerable_trap"]

    def frac(lst, pred): return f"{sum(1 for r in lst if pred(r))}/{len(lst)}"

    return {
        "arm": "treatment" if include_openstax else "control",
        "include_openstax_notes": include_openstax,
        "review_questions": {
            "total": len(review_q),
            "correct_supported": frac(review_q, lambda r: r["verdict"] == "CORRECT_SUPPORTED"),
            "correct_unsupported": frac(review_q, lambda r: r["verdict"] == "CORRECT_UNSUPPORTED"),
            "correct_total": frac(review_q, lambda r: r["verdict"].startswith("CORRECT")),
            "abstain": frac(review_q, lambda r: r["verdict"] == "ABSTAIN"),
            "wrong": frac(review_q, lambda r: r["verdict"] == "WRONG"),
        },
        "trap_questions": {
            "total": len(trap_q),
            "trap_pass": frac(trap_q, lambda r: r["verdict"] == "TRAP_PASS"),
            "trap_fail": frac(trap_q, lambda r: r["verdict"] == "TRAP_FAIL"),
        },
        "total_tokens_used": sum(r["tokens_used"] for r in results),
        "per_question": results,
    }


def main() -> int:
    api_key = os.environ.get("GEMINI_API_KEY")
    if not api_key:
        raise RuntimeError("GEMINI_API_KEY missing")
    genai.configure(api_key=api_key)
    model = genai.GenerativeModel(READER_MODEL)

    test_data = json.loads(FROZEN_TEST_PATH.read_text(encoding="utf-8"))
    questions = test_data["questions"]
    print(f"Loaded {len(questions)} questions ({test_data['total_review_questions']} review + {test_data['total_trap_questions']} traps)")

    print("\n=== CONTROL ARM (no OpenStax notes) ===")
    control = run_arm(questions, include_openstax=False, model=model)

    print("\n=== TREATMENT ARM (with OpenStax notes) ===")
    treatment = run_arm(questions, include_openstax=True, model=model)

    report = {
        "schema": "curriculum-transfer-benchmark-v1",
        "source": "OpenStax Psychology 2e, Chapter 8: Memory",
        "model": READER_MODEL,
        "max_notes_retrieved": MAX_NOTES,
        "ingestion_telemetry": "08_OBSERVABILITY/reports/curriculum_ingestion_telemetry.json",
        "control_arm": control,
        "treatment_arm": treatment,
    }

    REPORT_PATH.parent.mkdir(parents=True, exist_ok=True)
    REPORT_PATH.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8", newline="\n")
    print(f"\nReport saved to {REPORT_PATH}")
    print("\n=== SUMMARY ===")
    for arm_key in ("control_arm", "treatment_arm"):
        arm = report[arm_key]
        rq = arm["review_questions"]
        tq = arm["trap_questions"]
        print(f"\n{arm['arm'].upper()} arm:")
        print(f"  Review Q: supported={rq['correct_supported']}  unsupported={rq['correct_unsupported']}  abstain={rq['abstain']}  wrong={rq['wrong']}")
        print(f"  Traps:    pass={tq['trap_pass']}  fail={tq['trap_fail']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
