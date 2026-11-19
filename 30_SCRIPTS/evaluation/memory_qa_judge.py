"""Memory QA ablation, step 3: blind judging packet and scoring.

``packet``: every unique (case, answer) pair, shuffled (seed 20261119), with
the question and the reference answer, never the condition.
``score``: implements PREREGISTRATION.md section 5 from two judges' labels.

    python 30_SCRIPTS/evaluation/memory_qa_judge.py packet
    python 30_SCRIPTS/evaluation/memory_qa_judge.py score --judge1 j1.json --judge2 j2.json
"""
from __future__ import annotations

import argparse
import json
import math
import random
import re
import unicodedata
from collections import defaultdict
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
EXP = REPO_ROOT / "07_EVALUATION" / "memory_qa_ablation"
BENCH = REPO_ROOT / "07_EVALUATION" / "retrieval_benchmark_v3" / "retrieval_benchmark_v3.json"
RUBRIC = ("Answerable question (a reference is given): CORRECT only if the answer states every key proposition of the "
          "reference (all steps for multi-step references) and nothing in it contradicts the reference; ABSTAIN if it "
          "makes no substantive claim (e.g. 'I don't know'); otherwise INCORRECT. Paraphrase and another language are "
          "fine. Unanswerable question (reference says none exists): ABSTAIN if it declines or says it does not know; "
          "HALLUCINATION if it asserts any substantive answer.")


def mcnemar_exact(b: int, c: int) -> float:
    n = b + c
    if n == 0:
        return 1.0
    return min(1.0, 2 * sum(math.comb(n, i) for i in range(min(b, c) + 1)) / 2 ** n)


def kappa(x, y) -> float:
    n = len(x)
    po = sum(a == b for a, b in zip(x, y)) / n
    cats = set(x) | set(y)
    pe = sum((x.count(c) / n) * (y.count(c) / n) for c in cats)
    return 1.0 if pe == 1 else (po - pe) / (1 - pe)


def fold(text: str) -> list:
    t = unicodedata.normalize("NFKD", text.lower())
    t = "".join(ch for ch in t if not unicodedata.combining(ch))
    return re.findall(r"\w+", t)


def fact_recall(answer: str, facts: list) -> float:
    if not facts:
        return float("nan")
    a = set(fold(answer))
    toks = [w for f in facts for w in fold(f) if len(w) > 2]
    return sum(w in a for w in toks) / len(toks) if toks else float("nan")


def packet() -> None:
    answers = json.loads((EXP / "answers.json").read_text(encoding="utf-8"))
    cases = {c["id"]: c for c in json.loads(BENCH.read_text(encoding="utf-8"))["cases"]}
    pairs, key = {}, {}
    for r in answers["rows"]:
        for cond, a in r["answers"].items():
            k = (r["id"], a["answer"])
            pairs.setdefault(k, []).append(cond)
    items = []
    for (cid, ans), conds in pairs.items():
        c = cases[cid]
        ref = c["expected_answer"] if not c["abstain"] else "NONE - this question has no answer in the vault."
        items.append({"question": c["query"], "reference": ref, "answer": ans, "_key": (cid, conds)})
    random.Random(20261119).shuffle(items)
    out = []
    for i, it in enumerate(items, 1):
        pid = f"Q{i:03d}"
        cid, conds = it.pop("_key")
        key[pid] = {"case": cid, "conditions": conds}
        out.append({"pair_id": pid, **it})
    (EXP / "judge_packet.json").write_text(json.dumps({"rubric": RUBRIC, "items": out}, ensure_ascii=False, indent=1),
                                           encoding="utf-8")
    (EXP / "judge_key.json").write_text(json.dumps(key, indent=1), encoding="utf-8")
    print(json.dumps({"unique_pairs": len(out)}))


def labels_by_case(judge_file: Path) -> dict:
    key = json.loads((EXP / "judge_key.json").read_text(encoding="utf-8"))
    j = json.loads(judge_file.read_text(encoding="utf-8"))["judgements"]
    abstain = {c["id"] for c in json.loads(BENCH.read_text(encoding="utf-8"))["cases"] if c["abstain"]}
    ids = [i["pair_id"] for i in j]
    if len(ids) != len(set(ids)):
        raise SystemExit(f"DUPLICATE_PAIR_IDS in {judge_file.name}")
    for item in j:
        allowed = {"ABSTAIN", "HALLUCINATION"} if key[item["pair_id"]]["case"] in abstain             else {"CORRECT", "INCORRECT", "ABSTAIN"}
        if item.get("label") not in allowed:
            raise SystemExit(f"INVALID_LABEL {item['pair_id']}={item.get('label')!r} in {judge_file.name}")
    lab = {}
    for item in j:
        k = key[item["pair_id"]]
        for cond in k["conditions"]:
            lab[(k["case"], cond)] = item["label"]
    if len(set(i["pair_id"] for i in j)) != len(key):
        raise SystemExit(f"UNJUDGED_PAIRS {len(key) - len(set(i['pair_id'] for i in j))}")
    return lab


def score(j1: Path, j2: Path) -> None:
    l1, l2 = labels_by_case(j1), labels_by_case(j2)
    cases = {c["id"]: c for c in json.loads(BENCH.read_text(encoding="utf-8"))["cases"]}
    ctx = {r["id"]: r for r in json.loads((EXP / "contexts.json").read_text(encoding="utf-8"))["rows"]}
    answers = {r["id"]: r["answers"] for r in json.loads((EXP / "answers.json").read_text(encoding="utf-8"))["rows"]}
    ans_ids = [cid for cid, c in cases.items() if not c["abstain"]]
    abst_ids = [cid for cid, c in cases.items() if c["abstain"]]

    def correct(lab, cid, cond):
        return lab[(cid, cond)] == "CORRECT"

    def contrast(lab, x, y, ids):
        w = [c for c in ids if correct(lab, c, x) and not correct(lab, c, y)]
        l = [c for c in ids if correct(lab, c, y) and not correct(lab, c, x)]
        return {"wins": len(w), "losses": len(l), "net": len(w) - len(l), "p": round(mcnemar_exact(len(w), len(l)), 6),
                "win_ids": w, "loss_ids": l}

    res = {"answerable": len(ans_ids), "abstain_cases": len(abst_ids)}
    for name, lab in (("judge1", l1), ("judge2", l2)):
        res[name] = {
            "correct": {c: sum(correct(lab, i, c) for i in ans_ids) for c in "ABC"},
            "incorrect": {c: sum(lab[(i, c)] == "INCORRECT" for i in ans_ids) for c in "ABC"},
            "hallucination_on_abstain": {c: sum(lab[(i, c)] == "HALLUCINATION" for i in abst_ids) for c in "ABC"},
            "C_vs_B": contrast(lab, "C", "B", ans_ids), "B_vs_A": contrast(lab, "B", "A", ans_ids),
            "C_vs_A": contrast(lab, "C", "A", ans_ids),
        }
    prim = res["judge1"]["C_vs_B"]
    ro = [i for i in ans_ids if cases[i].get("language") == "ro"]
    ro_c = contrast(l1, "C", "B", ro)
    x = [l1[(i, c)] == "CORRECT" for i in ans_ids for c in "BC"]
    y = [l2[(i, c)] == "CORRECT" for i in ans_ids for c in "BC"]
    k = kappa(x, y)
    hall = res["judge1"]["hallucination_on_abstain"]
    gold_notes = {g for cid in prim["win_ids"] + prim["loss_ids"] for g in cases[cid]["gold_relevant_notes"]}
    rule = {"net_ge_8": prim["net"] >= 8, "p_lt_05": prim["p"] < 0.05,
            "losses_le_wins_over_3": prim["losses"] <= prim["wins"] / 3, "ro_net_ge_0": ro_c["net"] >= 0,
            "kappa_ge_08": k >= 0.8, "abstain_guardrail": hall["C"] - hall["B"] <= 3}
    if all(rule.values()):
        verdict = "PASSES_THROUGH"
    elif prim["net"] <= 2 or prim["losses"] > prim["wins"] / 2:
        verdict = "NULL"
    else:
        verdict = "INCONCLUSIVE"
    served = {}
    for cond in "BC":
        hit = [i for i in ans_ids if set(cases[i]["gold_relevant_notes"]) & set(ctx[i][cond]["ids"])]
        served[cond] = {"gold_served": len(hit), "correct_given_gold_served": sum(correct(l1, i, cond) for i in hit)}
    strata = defaultdict(lambda: {"n": 0, "B": 0, "C": 0, "A": 0})
    for i in ans_ids:
        for key in (f"class:{cases[i]['class']}", f"lang:{cases[i]['language']}"):
            strata[key]["n"] += 1
            for c in "ABC":
                strata[key][c] += correct(l1, i, c)
    recall = {}
    for c in "ABC":
        vals = [v for v in (fact_recall(answers[i][c]["answer"], cases[i]["required_facts"]) for i in ans_ids)
                if not math.isnan(v)]
        recall[c] = round(sum(vals) / len(vals), 4) if vals else None
    res.update({"primary": prim, "ro_contrast": {k2: v for k2, v in ro_c.items() if not k2.endswith("ids")},
                "kappa_judges_BC_correct": round(k, 4), "rule": rule, "verdict": verdict,
                "distinct_gold_notes_in_discordant": len(gold_notes), "gold_served": served,
                "strata_judge1": dict(strata), "required_facts_token_recall": recall})
    (EXP / "results.json").write_text(json.dumps(res, indent=1, ensure_ascii=False), encoding="utf-8")
    print(json.dumps({k2: v for k2, v in res.items() if k2 not in ("judge2",)}, indent=1, ensure_ascii=False))


def main() -> int:
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("packet")
    s = sub.add_parser("score")
    s.add_argument("--judge1", type=Path, required=True)
    s.add_argument("--judge2", type=Path, required=True)
    a = ap.parse_args()
    packet() if a.cmd == "packet" else score(a.judge1, a.judge2)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
