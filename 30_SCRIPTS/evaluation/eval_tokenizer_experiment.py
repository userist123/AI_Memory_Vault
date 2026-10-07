"""30_SCRIPTS/evaluation/eval_tokenizer_experiment.py — Tokenizer Normalization Experiment (EXP-TOKEN-001).

Implements Part 3 PR 1:
Evaluates 3 tokenizer arms across all 130 non-abstain cases from benchmark v3:
- Arm 1 (Baseline): current production tokenizer (TOKEN_RE = [a-z0-9][a-z0-9_\\-\\.]*)
- Arm 2 (Unicode): Unicode-preserving tokenizer recognizing Romanian characters (ăâîșț)
- Arm 3 (Stripped): Diacritics-stripped tokenizer (accent folding via NFKD)

Outputs:
- 07_EVALUATION/tokenizer_experiment/tokenizer_experiment_cases.json
- 07_EVALUATION/tokenizer_experiment/TOKENIZER_EXPERIMENT_REPORT.md
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
import math
import os
import re
import sys
import time
import unicodedata
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional, Sequence, Set, Tuple

REPO_ROOT = Path(__file__).resolve().parents[2]
impl_path = REPO_ROOT / "03_IMPLEMENTATION" / "packages"
if str(impl_path) not in sys.path:
    sys.path.insert(0, str(impl_path))

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.authorizer import Principal
from memory_controller.controller import (
    AGENT_LIFECYCLE_FLOOR,
    RANKING_ARM_BASELINE,
    RANKING_ARM_FUSED_SCORE,
    Lifecycle,
    MemoryController,
)
from memory_controller.storage.file_engine import FileStorageEngine
from retrieval.vault_index import VaultIndex
import retrieval.hybrid_retrieval as hr
import retrieval.context.candidate_generation as cg

BENCHMARK_PATH = (
    REPO_ROOT
    / "07_EVALUATION"
    / "retrieval_benchmark_v3"
    / "retrieval_benchmark_v3.json"
)
BENCHMARK_SHA_PATH = (
    REPO_ROOT
    / "07_EVALUATION"
    / "retrieval_benchmark_v3"
    / "retrieval_benchmark_v3.json.sha256"
)
PREREG_PATH = (
    REPO_ROOT
    / "07_EVALUATION"
    / "tokenizer_experiment"
    / "PREREGISTRATION.md"
)
ARTIFACT_JSON_PATH = (
    REPO_ROOT
    / "07_EVALUATION"
    / "tokenizer_experiment"
    / "tokenizer_experiment_cases.json"
)
REPORT_MD_PATH = (
    REPO_ROOT
    / "07_EVALUATION"
    / "tokenizer_experiment"
    / "TOKENIZER_EXPERIMENT_REPORT.md"
)

TOKEN_RE_BASE = hr.TOKEN_RE
STOP_BASE = hr.STOP

UNICODE_TOKEN_RE = re.compile(
    r"[a-z0-9\u0103\u00e2\u00ee\u0219\u021b\u015f\u0163][a-z0-9\u0103\u00e2\u00ee\u0219\u021b\u015f\u0163_\-\.]*",
    re.IGNORECASE,
)
STOP_UNICODE = STOP_BASE | {"și", "şi", "să", "în", "deși", "printr-un", "printr-o"}


def tokenize_baseline(text: str) -> List[str]:
    return [t for t in TOKEN_RE_BASE.findall(text.lower()) if t not in STOP_BASE and len(t) > 1]


def tokenize_unicode(text: str) -> List[str]:
    return [t.lower() for t in UNICODE_TOKEN_RE.findall(text) if t.lower() not in STOP_UNICODE and len(t) > 1]


def strip_diacritics(s: str) -> str:
    nfkd = unicodedata.normalize("NFKD", s)
    return "".join(c for c in nfkd if not unicodedata.combining(c))


def tokenize_stripped(text: str) -> List[str]:
    clean = strip_diacritics(text.lower())
    return [t for t in TOKEN_RE_BASE.findall(clean) if t not in STOP_BASE and len(t) > 1]


#: Every module object that holds a `tokenize` the search path can reach.
#: The `memory_controller` compatibility shim imports the same files under a
#: second name, so `retrieval.context.candidate_generation` and
#: `memory_controller.context.candidate_generation` are two distinct module
#: objects. The first version of this experiment patched only the former; the
#: controller calls the latter, so every arm ran the production tokenizer.
_TOKENIZE_MODULE_SUFFIXES = ("hybrid_retrieval", "context.candidate_generation")


def _tokenize_holders() -> List[Any]:
    return [m for name, m in list(sys.modules.items())
            if m is not None and name.endswith(_TOKENIZE_MODULE_SUFFIXES)
            and hasattr(m, "tokenize")]


ORIGINAL_TOKENIZE = {id(m): m.tokenize for m in _tokenize_holders()}


def install_tokenizer(fn: Callable[[str], List[str]]) -> None:
    """Replace `tokenize` in every alias, then prove the controller sees it."""
    for module in _tokenize_holders():
        module.tokenize = fn
    controller_module = sys.modules[MemoryController.__module__]
    generator_module = sys.modules[controller_module.generate_candidates.__module__]
    if generator_module.tokenize is not fn:
        raise RuntimeError(
            f"tokenizer patch did not reach {generator_module.__name__}; "
            "the arm would silently run the production tokenizer")


def restore_tokenizer() -> None:
    for module in _tokenize_holders():
        original = ORIGINAL_TOKENIZE.get(id(module))
        if original is not None:
            module.tokenize = original


def tokenize_sabotage(text: str) -> List[str]:
    """The negative control: no tokens at all. Candidate generation must collapse."""
    return []


def wilson_score_interval(k: int, n: int, confidence: float = 0.95) -> Dict[str, float]:
    """Calculates asymmetric Wilson score confidence interval."""
    if n == 0:
        return {"proportion": 0.0, "lower": 0.0, "upper": 0.0}
    p = k / n
    z = 1.959963984540054  # 95%
    denom = 1.0 + (z**2) / n
    center = (p + (z**2) / (2 * n)) / denom
    margin = (z / denom) * math.sqrt((p * (1.0 - p) / n) + ((z**2) / (4 * (n**2))))
    return {
        "proportion": round(p, 4),
        "lower": round(max(0.0, center - margin), 4),
        "upper": round(min(1.0, center + margin), 4),
    }


def mcnemar_exact_test(b: int, c: int) -> float:
    """Calculates exact two-tailed McNemar test p-value from discordant counts b and c."""
    n = b + c
    if n == 0:
        return 1.0
    k = min(b, c)
    # Two-tailed binomial test with p = 0.5
    cum_prob = sum(math.comb(n, i) * (0.5**n) for i in range(k + 1))
    return min(1.0, 2.0 * cum_prob)


@dataclass
class ArmResult:
    arm_id: str
    arm_name: str
    description: str
    total_cases: int
    hits_all: int
    recall_all: float
    ci_all: Dict[str, float]
    hits_ro: int
    total_ro: int
    recall_ro: float
    ci_ro: Dict[str, float]
    hits_en: int
    total_en: int
    recall_en: float
    ci_en: Dict[str, float]
    reachable_in_top200_all: int
    reachable_in_top200_ro: int
    reachable_in_top200_en: int
    per_case_hits: Dict[str, bool]
    per_case_ranks: Dict[str, Optional[int]]


def evaluate_arm(
    arm_id: str,
    arm_name: str,
    desc: str,
    tok_fn: Callable[[str], List[str]],
    cases: List[Dict[str, Any]],
    storage: FileStorageEngine,
    index: VaultIndex,
    ranking_arm: str = RANKING_ARM_FUSED_SCORE,
) -> ArmResult:
    """Evaluates a single tokenizer arm across all cases."""
    install_tokenizer(tok_fn)

    # Fresh controller per arm ensures completely clean cache and isolation
    controller = MemoryController(
        storage=storage,
        index=index,
        enable_graph_expansion=False,
        strict_graph_expansion=False,
        ranking_arm=ranking_arm,
        enable_spreading_activation=False,
        enable_cognitive_core=False,
    )

    per_case_hits: Dict[str, bool] = {}
    per_case_ranks: Dict[str, Optional[int]] = {}

    hits_all = 0
    hits_ro = 0
    hits_en = 0
    top200_all = 0
    top200_ro = 0
    top200_en = 0

    total_ro = sum(1 for c in cases if c.get("language") == "ro")
    total_en = sum(1 for c in cases if c.get("language") == "en")

    for c in cases:
        cid = c["id"]
        q = c.get("query", "")
        golds = set(c.get("gold_relevant_notes") or [])
        lang = c.get("language", "en")

        pack = controller.search(Principal.AI_AGENT, q, page_size=5)
        res_ids = [r.get("id") for r in pack.get("results", []) if isinstance(r, dict)]
        hit = bool(set(res_ids) & golds)
        per_case_hits[cid] = hit

        # Gold rank in fused candidate ranking
        cand_trace = pack.get("candidate_trace", {})
        fused = cand_trace.get("fused_ranking", [])
        gold_rank: Optional[int] = None
        for entry in fused:
            if entry.get("id") in golds:
                gold_rank = entry.get("rank")
                break
        per_case_ranks[cid] = gold_rank

        if hit:
            hits_all += 1
            if lang == "ro":
                hits_ro += 1
            else:
                hits_en += 1

        if gold_rank is not None and gold_rank <= 200:
            top200_all += 1
            if lang == "ro":
                top200_ro += 1
            else:
                top200_en += 1

    total_cases = len(cases)
    return ArmResult(
        arm_id=arm_id,
        arm_name=arm_name,
        description=desc,
        total_cases=total_cases,
        hits_all=hits_all,
        recall_all=round(hits_all / total_cases, 4),
        ci_all=wilson_score_interval(hits_all, total_cases),
        hits_ro=hits_ro,
        total_ro=total_ro,
        recall_ro=round(hits_ro / total_ro, 4),
        ci_ro=wilson_score_interval(hits_ro, total_ro),
        hits_en=hits_en,
        total_en=total_en,
        recall_en=round(hits_en / total_en, 4),
        ci_en=wilson_score_interval(hits_en, total_en),
        reachable_in_top200_all=top200_all,
        reachable_in_top200_ro=top200_ro,
        reachable_in_top200_en=top200_en,
        per_case_hits=per_case_hits,
        per_case_ranks=per_case_ranks,
    )


def compute_mcnemar_comparison(
    baseline: ArmResult, experimental: ArmResult, cases: List[Dict[str, Any]]
) -> Dict[str, Any]:
    """Computes discordant pairs and McNemar test statistics."""
    b_ro, c_ro = 0, 0
    b_en, c_en = 0, 0
    b_all, c_all = 0, 0

    discordant_cases: List[Dict[str, Any]] = []

    for c in cases:
        cid = c["id"]
        lang = c.get("language", "en")
        base_hit = baseline.per_case_hits[cid]
        exp_hit = experimental.per_case_hits[cid]

        if not base_hit and exp_hit:
            # Gain
            b_all += 1
            if lang == "ro":
                b_ro += 1
            else:
                b_en += 1
            discordant_cases.append({
                "case_id": cid,
                "language": lang,
                "transition": "GAIN (fail -> hit)",
                "baseline_rank": baseline.per_case_ranks[cid],
                "experimental_rank": experimental.per_case_ranks[cid],
            })
        elif base_hit and not exp_hit:
            # Loss
            c_all += 1
            if lang == "ro":
                c_ro += 1
            else:
                c_en += 1
            discordant_cases.append({
                "case_id": cid,
                "language": lang,
                "transition": "LOSS (hit -> fail)",
                "baseline_rank": baseline.per_case_ranks[cid],
                "experimental_rank": experimental.per_case_ranks[cid],
            })

    return {
        "all": {
            "gains_b": b_all,
            "losses_c": c_all,
            "p_mcnemar": mcnemar_exact_test(b_all, c_all),
            "delta_cases": b_all - c_all,
            "delta_pp": round((experimental.recall_all - baseline.recall_all) * 100, 2),
        },
        "ro": {
            "gains_b": b_ro,
            "losses_c": c_ro,
            "p_mcnemar": mcnemar_exact_test(b_ro, c_ro),
            "delta_cases": b_ro - c_ro,
            "delta_pp": round((experimental.recall_ro - baseline.recall_ro) * 100, 2),
        },
        "en": {
            "gains_b": b_en,
            "losses_c": c_en,
            "p_mcnemar": mcnemar_exact_test(b_en, c_en),
            "delta_cases": b_en - c_en,
            "delta_pp": round((experimental.recall_en - baseline.recall_en) * 100, 2),
        },
        "discordant_cases": discordant_cases,
    }


def render_report(data: Dict[str, Any]) -> str:
    """Deterministically renders markdown report from the experiment data."""
    md: List[str] = []
    w = md.append

    meta = data["metadata"]
    arms = data["arms"]
    comp_u = data["comparisons"]["arm2_unicode_vs_baseline"]
    comp_s = data["comparisons"]["arm3_stripped_vs_baseline"]
    verdicts = data["verdicts"]

    w("# Raport Experimental: Normalizarea Tokenizatorului (EXP-TOKEN-001)")
    w("")
    w("> **Raport Experimental Formal — Programul de Măsurare (Partea 3, PR 1)**  ")
    w("> **Depozit**: `userist123/AI_Memory_Vault_CODEX_READY`  ")
    w(f"> **Hash Benchmark Înghețat (SHA-256)**: `{meta['benchmark_sha256']}`  ")
    w(f"> **Cazuri măsurabile**: {meta['measurable_cases']} din {meta['total_benchmark_cases']} ({meta['ro_cases']} română, {meta['en_cases']} engleză)  ")
    w(f"> **Braț de clasare**: `{meta.get('ranking_arm', 'baseline (nedeclarat)')}`  ")
    w("")
    w("---")
    w("")
    w("## 1. Punctul de Operare și Scopul Măsurătorii")
    w("")
    w("Acest experiment a fost proiectat pentru a testa dacă înlocuirea tokenizatorului ASCII curent (`TOKEN_RE = [a-z0-9][a-z0-9_\\-\\.]*`) cu o variantă compatibilă Unicode sau cu eliminare simetrică a diacriticelor aduce o îmbunătățire măsurabilă pe felia românească a benchmark-ului.")
    w("")
    w("Toate măsurătorile din acest raport folosesc strict punctul de operare de producție:")
    w("- **Principal**: `Principal.AI_AGENT`")
    w("- **Page Size**: `page_size = 5`")
    w("- **Prag Ciclu de Viață**: ACTIV (`Floor: ACTIV`, note din afara `{ACTIVE}` excluse)")
    w("")
    w("---")
    w("")
    w("## 2. Tabelul 1 — Rezultatele Primare ale Celor 3 Brațe Experimentale")
    w("### [Punct de operare: Principal.AI_AGENT, page_size=5, Floor: ACTIV]")
    w("")
    w("| Braț Experimental | Total (N=130) | Recall Total (%) | Interval Wilson 95% | Română (N=61) | Recall RO (%) | Engleză (N=69) | Recall EN (%) | Top 200 RO |")
    w("|:---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|")
    for arm_key in ["arm1_baseline", "arm2_unicode", "arm3_stripped"]:
        a = arms[arm_key]
        w(f"| **{a['arm_name']}** | {a['hits_all']} / 130 | **{a['recall_all']*100:.2f}%** | [{a['ci_all']['lower']*100:.2f}%, {a['ci_all']['upper']*100:.2f}%] | {a['hits_ro']} / 61 | **{a['recall_ro']*100:.2f}%** | {a['hits_en']} / 69 | **{a['recall_en']*100:.2f}%** | {a['reachable_in_top200_ro']} / 61 |")
    w("")
    def same_hits(x: str, y: str) -> bool:
        return arms[x]["per_case_hits"] == arms[y]["per_case_hits"]

    identical = same_hits("arm1_baseline", "arm2_unicode") and same_hits("arm1_baseline", "arm3_stripped")
    base = arms["arm1_baseline"]
    w("> [!IMPORTANT]")
    if identical:
        w(f"> **Toate cele 3 brațe au exact același set de reușite** ({base['hits_all']} / {base['total_cases']}). "
          "Înainte de a citi asta ca rezultat, verificați controlul negativ de mai jos: un set identic "
          "este și semnătura unui braț care nu ajunge în calea de căutare.")
    else:
        w("> **Brațele diferă.** Diferențele, pe cazuri, sunt în tabelul 2.")
    w("")

    ctrl = data.get("negative_control")
    w("### Control negativ — tokenizator gol")
    w("")
    if ctrl is None:
        w("> [!CAUTION]")
        w("> **Acest artefact nu are control negativ.** Prima versiune a experimentului a modificat")
        w("> `tokenize` doar în `retrieval.context.candidate_generation`, în timp ce controllerul folosește")
        w("> `memory_controller.context.candidate_generation` — același fișier, alt obiect-modul, din cauza")
        w("> shimului de compatibilitate. Toate brațele au rulat tokenizatorul de producție. Rezultatele de mai")
        w("> sus nu măsoară nimic despre tokenizare.")
    else:
        verdict = "TRECUT" if ctrl["passed"] else "EȘUAT"
        w(f"Cu `tokenize()` care întoarce `[]` pentru orice text, s-au obținut {ctrl['hits_all']} reușite, "
          f"iar {ctrl['cases_changed_vs_baseline']} cazuri și-au schimbat rezultatul sau rangul notei de aur "
          f"față de brațul 1. **Control {verdict}.** "
          + ("Patch-ul ajunge în calea de căutare, deci brațele sunt reale."
             if ctrl["passed"] else "Brațele de mai sus sunt nule."))
        mods = meta.get("patched_modules") or []
        if mods:
            w("")
            w("Module în care a fost înlocuit `tokenize`: " + ", ".join(f"`{m}`" for m in mods) + ".")
    w("")
    w("---")
    w("")
    w("## 3. Tabelul 2 — Analiza Pereche și Testul Exact McNemar")
    w(f"### [Punct de operare: Principal.AI_AGENT, page_size=5, braț de clasare `{meta.get('ranking_arm', 'baseline (nedeclarat)')}`]")
    w("")
    w("| Comparație vs Baseline | Felie | Câștiguri ($b$) | Pierderi ($c$) | Cazuri Discordante | $\\Delta$ Cazuri | $\\Delta$ Procentual (pp) | $p$ McNemar Exact | Semnificație la 0.05 |")
    w("|:---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---|")
    for comp_name, comp in [("Brațul 2 (Unicode)", comp_u), ("Brațul 3 (Stripped)", comp_s)]:
        for slice_name, label in [("ro", "Română (N=61)"), ("en", "Engleză (N=69)"), ("all", "Total (N=130)")]:
            sc = comp[slice_name]
            disc = sc["gains_b"] + sc["losses_c"]
            sig = ("fără cazuri discordante" if disc == 0
                   else "semnificativ" if sc["p_mcnemar"] < 0.05 else "nesemnificativ")
            w(f"| {comp_name} | `{label}` | {sc['gains_b']} | {sc['losses_c']} | {disc} | {sc['delta_cases']:+d} | {sc['delta_pp']:+.2f} pp | `{sc['p_mcnemar']:.6f}` | {sig} |")
    w("")
    w("---")
    w("")
    w("## 4. Tabelul 3 — Evaluarea Ipotezelor Preînregistrate")
    w("")
    w("Regula se aplică în cazuri, nu în puncte procentuale: preînregistrarea dă pentru română atât "
      "„+5.00 pp” cât și „≥ 3 cazuri nete”, iar pe 61 de cazuri trei cazuri înseamnă 4.92 pp. "
      "Numărul de cazuri este lectura neambiguă.")
    w("")
    w("| Criteriu | Condiție | Brațul 2 (Unicode) | Brațul 3 (Stripped) |")
    w("|:---|:---|:---:|:---:|")
    per_arm = verdicts.get("per_arm") or {}

    def cell(arm: str, key: str, comp: Dict[str, Any], sl: str) -> str:
        ok = per_arm.get(arm, {}).get(key)
        mark = "—" if ok is None else ("da" if ok else "nu")
        return f"{comp[sl]['delta_cases']:+d} cazuri ({comp[sl]['delta_pp']:+.2f} pp) — {mark}"

    w(f"| H-TOKEN-1, câștig pe română | $\\ge 3$ cazuri nete | {cell('arm2_unicode', 'ro_gain_ge_3_cases', comp_u, 'ro')} | {cell('arm3_stripped', 'ro_gain_ge_3_cases', comp_s, 'ro')} |")
    w(f"| Non-regresie pe engleză | $\\ge -1$ caz, iar la pierdere $p > 0.10$ | {cell('arm2_unicode', 'en_non_regression', comp_u, 'en')} | {cell('arm3_stripped', 'en_non_regression', comp_s, 'en')} |")
    w(f"| Câștig net total | $\\ge 2$ cazuri nete | {cell('arm2_unicode', 'total_gain_ge_2_cases', comp_u, 'all')} | {cell('arm3_stripped', 'total_gain_ge_2_cases', comp_s, 'all')} |")
    w("")
    w(f"H-TOKEN-1: **{verdicts['hypothesis_H_TOKEN_1']}**. Non-regresie pe engleză: **{verdicts['non_regression_english']}**. "
      f"Câștig net total: **{verdicts['net_total_gain']}**.")
    w("")
    w("---")
    w("")
    w("## 5. Decizia")
    w("")
    w(f"**{verdicts['decision']}**")
    w("")
    if verdicts.get("chosen_arm"):
        w(f"Brațul ales: `{verdicts['chosen_arm']}`. Conform secțiunii 8 a preînregistrării, "
          "tokenizatorul de producție nu se modifică în acest PR; adoptarea este o schimbare separată.")
    else:
        w("Niciun braț nu îndeplinește simultan cele trei condiții preînregistrate; "
          "tokenizatorul de producție rămâne cum este.")
    w("")
    w("---")
    w("*Fiecare cifră și fiecare verdict din acest raport sunt citite din "
      "`07_EVALUATION/tokenizer_experiment/tokenizer_experiment_cases.json`.*")
    w("")
    return "\n".join(md)


def run_experiment(ranking_arm: str = RANKING_ARM_FUSED_SCORE) -> Dict[str, Any]:
    """Runs the full experiment across all 3 arms."""
    print("=================================================================")
    print("   Tokenizer Normalization Experiment (EXP-TOKEN-001)")
    print("=================================================================")

    # Verify frozen benchmark integrity
    expected_sha = BENCHMARK_SHA_PATH.read_text(encoding="utf-8").split()[0]
    actual_sha = hashlib.sha256(BENCHMARK_PATH.read_bytes()).hexdigest()
    if actual_sha != expected_sha:
        raise ValueError(f"FROZEN_BENCHMARK_HASH_MISMATCH: expected {expected_sha}, got {actual_sha}")
    print(f"Benchmark SHA-256 verified: {actual_sha[:16]}... (frozen)")

    raw_data = json.loads(BENCHMARK_PATH.read_text(encoding="utf-8"))
    cases = [c for c in raw_data.get("cases", []) if not c.get("abstain")]
    print(f"Loaded {len(cases)} non-abstain measurable cases.")

    storage = FileStorageEngine(str(REPO_ROOT))
    index = VaultIndex.load(REPO_ROOT, include_raw=True, include_archived=True)

    t0 = time.perf_counter()
    print(f"RANKING_ARM={ranking_arm}")
    print("\nNegative control: a tokenizer that returns no tokens...")
    res_sabotage = evaluate_arm(
        "control_sabotage",
        "Control negativ — tokenizator gol",
        "tokenize() returnează [] pentru orice text; generarea de candidați trebuie să cedeze",
        tokenize_sabotage,
        cases,
        storage,
        index,
        ranking_arm,
    )
    print(f"  Sabotage: Total={res_sabotage.hits_all}/130")

    print("\nEvaluating Arm 1 (Baseline)...")
    res_arm1 = evaluate_arm(
        "arm1_baseline",
        "Brațul 1 (Linie de Referință — Baseline)",
        "Tokenizator curent ASCII (TOKEN_RE = [a-z0-9][a-z0-9_\\-\\.]*)",
        tokenize_baseline,
        cases,
        storage,
        index,
        ranking_arm,
    )
    print(f"  Arm 1: Total={res_arm1.hits_all}/130 ({res_arm1.recall_all*100:.2f}%), RO={res_arm1.hits_ro}/61, EN={res_arm1.hits_en}/69")
    sabotage_changed = sum(
        1 for cid in res_arm1.per_case_hits
        if res_arm1.per_case_hits[cid] != res_sabotage.per_case_hits[cid]
        or res_arm1.per_case_ranks[cid] != res_sabotage.per_case_ranks[cid])
    print(f"  Sabotage changed {sabotage_changed} cases (hit or gold rank)")
    if sabotage_changed == 0:
        restore_tokenizer()
        raise RuntimeError(
            "NEGATIVE_CONTROL_FAILED: an empty tokenizer changed nothing, so the "
            "patch does not reach the search path and every arm would be void")

    print("\nEvaluating Arm 2 (Unicode Preserving)...")
    res_arm2 = evaluate_arm(
        "arm2_unicode",
        "Brațul 2 (Litere Unicode — Unicode Preserving)",
        "Tokenizator Unicode care recunoaște literele specifice limbii române (ăâîșț)",
        tokenize_unicode,
        cases,
        storage,
        index,
        ranking_arm,
    )
    print(f"  Arm 2: Total={res_arm2.hits_all}/130 ({res_arm2.recall_all*100:.2f}%), RO={res_arm2.hits_ro}/61, EN={res_arm2.hits_en}/69")

    print("\nEvaluating Arm 3 (Diacritics Stripped)...")
    res_arm3 = evaluate_arm(
        "arm3_stripped",
        "Brațul 3 (Foldare Diacritice — Diacritics Stripping)",
        "Tokenizator cu eliminare simetrică a diacriticelor (accent folding via NFKD)",
        tokenize_stripped,
        cases,
        storage,
        index,
        ranking_arm,
    )
    print(f"  Arm 3: Total={res_arm3.hits_all}/130 ({res_arm3.recall_all*100:.2f}%), RO={res_arm3.hits_ro}/61, EN={res_arm3.hits_en}/69")

    elapsed = time.perf_counter() - t0
    print(f"\nExecution elapsed: {elapsed:.2f} seconds")

    restore_tokenizer()

    # Comparisons
    comp_u = compute_mcnemar_comparison(res_arm1, res_arm2, cases)
    comp_s = compute_mcnemar_comparison(res_arm1, res_arm3, cases)

    # Decision rule, PREREGISTRATION.md section 7. Counted in cases, because the
    # text gives both "+5.00 pp" and ">= 3 net cases" for Romanian and on n=61
    # three cases are 4.92 pp: the case count is the unambiguous reading, and the
    # percentage-point figures are reported alongside.
    def judge(comp: Dict[str, Any]) -> Dict[str, Any]:
        ro_ok = comp["ro"]["delta_cases"] >= 3
        en_ok = comp["en"]["delta_cases"] >= -1 and (
            comp["en"]["delta_cases"] >= 0 or comp["en"]["p_mcnemar"] > 0.10)
        total_ok = comp["all"]["delta_cases"] >= 2
        return {"ro_gain_ge_3_cases": ro_ok, "en_non_regression": en_ok,
                "total_gain_ge_2_cases": total_ok, "adopt": ro_ok and en_ok and total_ok}

    judged = {"arm2_unicode": judge(comp_u), "arm3_stripped": judge(comp_s)}
    passing = [a for a, j in judged.items() if j["adopt"]]
    if not passing:
        decision_verdict = "MENȚINERE BASELINE (RESPINGERE ADOPTARE TOKENIZATOR NOU)"
        chosen = None
    else:
        by_gain = {"arm2_unicode": comp_u["all"]["delta_cases"], "arm3_stripped": comp_s["all"]["delta_cases"]}
        chosen = max(passing, key=lambda a: by_gain[a])
        decision_verdict = f"ADOPTARE TOKENIZATOR NOU ({chosen})"

    def status(flag: bool) -> str:
        return "CONFIRMATĂ" if flag else "INFIRMATĂ"

    data = {
        "metadata": {
            "experiment_id": "EXP-TOKEN-001",
            "total_benchmark_cases": len(raw_data.get("cases", [])),
            "measurable_cases": len(cases),
            "ro_cases": res_arm1.total_ro,
            "en_cases": res_arm1.total_en,
            "benchmark_sha256": actual_sha,
            "execution_time_seconds": round(elapsed, 2),
            "ranking_arm": ranking_arm,
            "patched_modules": sorted(m.__name__ for m in _tokenize_holders()),
        },
        "arms": {
            "arm1_baseline": asdict(res_arm1),
            "arm2_unicode": asdict(res_arm2),
            "arm3_stripped": asdict(res_arm3),
        },
        "comparisons": {
            "arm2_unicode_vs_baseline": comp_u,
            "arm3_stripped_vs_baseline": comp_s,
        },
        "verdicts": {
            "hypothesis_H_TOKEN_1": status(any(j["ro_gain_ge_3_cases"] for j in judged.values())),
            "non_regression_english": status(all(j["en_non_regression"] for j in judged.values())),
            "net_total_gain": status(any(j["total_gain_ge_2_cases"] for j in judged.values())),
            "per_arm": judged,
            "chosen_arm": chosen,
            "decision": decision_verdict,
        },
        "negative_control": {
            "description": "tokenize() returning [] for every text",
            "hits_all": res_sabotage.hits_all,
            "cases_changed_vs_baseline": sabotage_changed,
            "passed": sabotage_changed > 0,
        },
    }
    return data


def main() -> int:
    parser = argparse.ArgumentParser(description="Evaluate Tokenizer Normalization Experiment")
    parser.add_argument("--run", action="store_true", help="Run the full 3-arm benchmark evaluation")
    parser.add_argument("--render", action="store_true", help="Render markdown report from existing JSON")
    parser.add_argument("--ranking-arm", default=RANKING_ARM_FUSED_SCORE,
                        help="Ranking arm; defaults to the production default, fused_score")
    args = parser.parse_args()

    if args.render:
        if not ARTIFACT_JSON_PATH.exists():
            print(f"Error: {ARTIFACT_JSON_PATH} not found.")
            return 1
        data = json.loads(ARTIFACT_JSON_PATH.read_text(encoding="utf-8"))
        report_text = render_report(data)
        REPORT_MD_PATH.write_text(report_text, encoding="utf-8")
        print(f"Report written to {REPORT_MD_PATH}")
        return 0

    # Default is run + render
    data = run_experiment(args.ranking_arm)
    ARTIFACT_JSON_PATH.parent.mkdir(parents=True, exist_ok=True)
    ARTIFACT_JSON_PATH.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"Artifact written to {ARTIFACT_JSON_PATH} ({ARTIFACT_JSON_PATH.stat().st_size} bytes)")

    report_text = render_report(data)
    REPORT_MD_PATH.write_text(report_text, encoding="utf-8")
    print(f"Report written to {REPORT_MD_PATH} ({REPORT_MD_PATH.stat().st_size} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
