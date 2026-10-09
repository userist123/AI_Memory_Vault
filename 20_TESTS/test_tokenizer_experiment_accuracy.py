"""The tokenizer experiment's artifacts are consistent with each other and with the preregistered rule.

What this guards:
1. The three artifacts exist; the benchmark is still the frozen file (SHA-256 pinned).
2. The committed report is bit-for-bit what `render_report()` produces from the JSON.
3. Tampering with either the report or the JSON is noticed.
4. Every table in the report declares its operating point.
5. The recorded verdicts are what the preregistered rule yields from the recorded counts —
   recomputed here, not pinned. The first version of this file pinned the counts themselves
   (21/130 three times over), which made it a test that the experiment stayed broken: those
   numbers came from three arms that had all run the production tokenizer.
6. The negative control passed — the one thing that distinguishes a measured null result
   from an inert arm.
7. The statistical helpers and the three tokenizers behave as documented.
"""
from __future__ import annotations

import copy
import hashlib
import json
import sys
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[1]
scripts_eval_path = REPO_ROOT / "30_SCRIPTS" / "evaluation"
if str(scripts_eval_path) not in sys.path:
    sys.path.insert(0, str(scripts_eval_path))

from eval_tokenizer_experiment import (  # noqa: E402
    ARTIFACT_JSON_PATH,
    BENCHMARK_PATH,
    BENCHMARK_SHA_PATH,
    PREREG_PATH,
    REPORT_MD_PATH,
    mcnemar_exact_test,
    render_report,
    strip_diacritics,
    tokenize_baseline,
    tokenize_stripped,
    tokenize_unicode,
    wilson_score_interval,
)

FROZEN_SHA = "eeb53822ae5f022df89290d6fca789c1d4387b7572ed70103a7c08aae5b57eaa"


@pytest.fixture(scope="module")
def data():
    assert ARTIFACT_JSON_PATH.exists(), f"Artifact missing: {ARTIFACT_JSON_PATH}"
    return json.loads(ARTIFACT_JSON_PATH.read_text(encoding="utf-8"))


@pytest.fixture(scope="module")
def report():
    assert REPORT_MD_PATH.exists(), f"Report missing: {REPORT_MD_PATH}"
    return REPORT_MD_PATH.read_text(encoding="utf-8")


# --- 1. artifacts and the frozen benchmark --------------------------------------------

def test_artifacts_exist_and_are_not_stubs():
    assert PREREG_PATH.stat().st_size > 2000
    assert ARTIFACT_JSON_PATH.stat().st_size > 10_000
    assert REPORT_MD_PATH.stat().st_size > 2000


def test_frozen_benchmark_integrity(data):
    expected = BENCHMARK_SHA_PATH.read_text(encoding="utf-8").split()[0]
    actual = hashlib.sha256(BENCHMARK_PATH.read_bytes()).hexdigest()
    assert actual == expected == FROZEN_SHA
    assert data["metadata"]["benchmark_sha256"] == FROZEN_SHA, "the artifact was measured on a different benchmark"


# --- 2 & 3. the report is generated, and tampering shows ------------------------------

def test_report_matches_render_output_bit_for_bit(data, report):
    assert report.strip() == render_report(data).strip()


def test_tampering_with_the_report_is_noticed(data, report):
    """Three edits a careless hand might make; each must break the bit-for-bit match."""
    rendered = render_report(data).strip()
    arm1 = data["arms"]["arm1_baseline"]
    hits_text = f"{arm1['hits_all']} / {arm1['total_cases']}"
    pct_text = f"{arm1['recall_all'] * 100:.2f}%"
    assert hits_text in report and pct_text in report, "the report no longer states arm 1's figures as expected"
    for before, after in (
        (hits_text, f"{arm1['hits_all'] + 1} / {arm1['total_cases']}"),
        (pct_text, f"{arm1['recall_all'] * 100 + 0.35:.2f}%"),
        (data["verdicts"]["decision"], "ADOPTARE TOKENIZATOR NOU (arm2_unicode)"),
    ):
        tampered = report.replace(before, after, 1)
        assert tampered != report, f"{before!r} not found, so this edit tests nothing"
        assert tampered.strip() != rendered


def test_tampering_with_the_json_is_noticed(data, report):
    tampered = copy.deepcopy(data)
    tampered["arms"]["arm2_unicode"]["hits_ro"] += 3
    tampered["arms"]["arm2_unicode"]["recall_ro"] = round(tampered["arms"]["arm2_unicode"]["hits_ro"] / 61, 4)
    assert render_report(tampered).strip() != report.strip()


# --- 4. every table says where it was measured -----------------------------------------

def test_every_table_declares_its_operating_point(report):
    lines = report.splitlines()
    separators = [i for i, line in enumerate(lines) if line.strip().startswith("|") and "---" in line]
    assert len(separators) >= 3, f"expected the three result tables, found {len(separators)}"
    for sep in separators:
        caption = "\n".join(lines[max(0, sep - 8): sep])
        assert "Principal.AI_AGENT" in caption and "page_size=5" in caption and "Floor: ACTIV" in caption, (
            f"table near line {sep + 1} does not declare its operating point:\n{caption}")


# --- 5. verdicts follow from the counts, by the preregistered rule -------------------

def _rule(comp: dict) -> dict:
    """PREREGISTRATION.md section 7, counted in cases (see the report for why cases, not pp)."""
    ro = comp["ro"]["delta_cases"] >= 3
    en = comp["en"]["delta_cases"] >= -1 and (comp["en"]["delta_cases"] >= 0 or comp["en"]["p_mcnemar"] > 0.10)
    total = comp["all"]["delta_cases"] >= 2
    return {"ro": ro, "en": en, "total": total, "adopt": ro and en and total}


def test_verdicts_are_what_the_rule_yields_from_the_counts(data, report):
    comps = {"arm2_unicode": data["comparisons"]["arm2_unicode_vs_baseline"],
             "arm3_stripped": data["comparisons"]["arm3_stripped_vs_baseline"]}
    judged = {arm: _rule(c) for arm, c in comps.items()}
    verdicts = data["verdicts"]

    def status(flag: bool) -> str:
        return "CONFIRMATĂ" if flag else "INFIRMATĂ"

    assert verdicts["hypothesis_H_TOKEN_1"] == status(any(j["ro"] for j in judged.values()))
    assert verdicts["non_regression_english"] == status(all(j["en"] for j in judged.values()))
    assert verdicts["net_total_gain"] == status(any(j["total"] for j in judged.values()))
    passing = [a for a, j in judged.items() if j["adopt"]]
    if passing:
        assert verdicts["decision"].startswith("ADOPTARE TOKENIZATOR NOU")
        assert verdicts["chosen_arm"] in passing
    else:
        assert verdicts["decision"] == "MENȚINERE BASELINE (RESPINGERE ADOPTARE TOKENIZATOR NOU)"
        assert verdicts["chosen_arm"] is None
    assert verdicts["decision"] in report


def test_discordant_counts_are_internally_consistent(data):
    """delta_cases is gains minus losses, and p comes from exactly those two numbers."""
    for comp in (data["comparisons"]["arm2_unicode_vs_baseline"], data["comparisons"]["arm3_stripped_vs_baseline"]):
        for sl in ("ro", "en", "all"):
            sc = comp[sl]
            assert sc["delta_cases"] == sc["gains_b"] - sc["losses_c"]
            assert sc["p_mcnemar"] == pytest.approx(mcnemar_exact_test(sc["gains_b"], sc["losses_c"]))
        assert comp["all"]["gains_b"] + comp["all"]["losses_c"] == len(comp["discordant_cases"])


# --- 6. the negative control ------------------------------------------------------------

def test_the_negative_control_passed(data):
    """An empty tokenizer must change the outcome. If it does not, the arms never reached the
    search path and every comparison above is void — which is exactly what happened the first time."""
    ctrl = data["negative_control"]
    assert ctrl["passed"] is True
    assert ctrl["cases_changed_vs_baseline"] > 0
    assert ctrl["hits_all"] < data["arms"]["arm1_baseline"]["hits_all"]


def test_the_patch_reached_the_module_the_controller_uses(data):
    """Both module objects for candidate_generation must have been patched, or the shim hides the arm."""
    patched = set(data["metadata"]["patched_modules"])
    assert "memory_controller.context.candidate_generation" in patched, patched


# --- 7. helpers and tokenizers ----------------------------------------------------------

def test_statistical_helpers_mcnemar_and_wilson():
    assert mcnemar_exact_test(0, 0) == 1.0
    assert mcnemar_exact_test(1, 1) == 1.0
    assert mcnemar_exact_test(10, 10) == 1.0
    assert mcnemar_exact_test(5, 0) == pytest.approx(0.0625, abs=1e-5)
    assert mcnemar_exact_test(0, 5) == pytest.approx(0.0625, abs=1e-5)
    assert mcnemar_exact_test(10, 1) == pytest.approx(0.01171875, abs=1e-5)
    w = wilson_score_interval(21, 130)
    assert w["lower"] < w["proportion"] < w["upper"]
    assert w["proportion"] == round(21 / 130, 4)


def test_tokenizer_behaviors():
    text_ro = "științific și regăsire"
    toks_base = tokenize_baseline(text_ro)
    assert "tiin" in toks_base and "ific" in toks_base and "reg" in toks_base and "sire" in toks_base
    toks_uni = tokenize_unicode(text_ro)
    assert "științific" in toks_uni and "regăsire" in toks_uni
    toks_strip = tokenize_stripped(text_ro)
    assert "stiintific" in toks_strip and "regasire" in toks_strip
    assert strip_diacritics("învățare") == "invatare"
