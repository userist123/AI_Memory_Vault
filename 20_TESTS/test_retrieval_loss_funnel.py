"""20_TESTS/test_retrieval_loss_funnel.py — Validation of RetrievalLossFunnel harness and negative controls.

Validates Part 2 PR 2 specifications:
1. Negative Control A: Shuffled labels in-memory permutation (recall < 5%).
2. Negative Control B: Gold injection detection (100% detection rate).
3. Negative Control C: Determinism across repeated executions.
4. Benchmark file immutability: SHA-256 hash verified bit-for-bit before and after.
5. Harness negative control: Synthetic fabricated cases (PAGINATION_CUT at rank 7, NEVER_CANDIDATE, RAW_EXCLUDED, AGENT_LIFECYCLE_FLOOR_EXCLUDED).
"""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import pytest

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.authorizer import Principal
from memory_controller.controller import (
    AGENT_LIFECYCLE_FLOOR,
    RANKING_ARM_BASELINE,
    Lifecycle,
    MemoryController,
    StorageEngine,
)
from observability.retrieval_trace import ExclusionReason
import sys
REPO_ROOT = Path(__file__).resolve().parents[1]
scripts_eval_path = REPO_ROOT / "30_SCRIPTS" / "evaluation"
if str(scripts_eval_path) not in sys.path:
    sys.path.insert(0, str(scripts_eval_path))

from retrieval_loss_funnel import (
    BENCHMARK_PATH,
    BENCHMARK_SHA_PATH,
    CaseDiagnostic,
    RetrievalLossFunnel,
)


@pytest.fixture(scope="module")
def shared_harness():
    """Shared harness with index and storage loaded once for tests."""
    return RetrievalLossFunnel()


def test_benchmark_file_immutability_during_permutation(shared_harness):
    """Verifies that in-memory label shuffling never touches or alters the frozen benchmark file on disk."""
    expected_sha = BENCHMARK_SHA_PATH.read_text(encoding="utf-8").split()[0]
    hash_before = hashlib.sha256(BENCHMARK_PATH.read_bytes()).hexdigest()
    assert hash_before == expected_sha, "Frozen benchmark hash pre-check failed"

    # Run shuffled labels control
    ctrl_a = shared_harness.run_negative_control_shuffled_labels(num_seeds=2, seed_start=100)

    hash_after = hashlib.sha256(BENCHMARK_PATH.read_bytes()).hexdigest()
    assert hash_after == expected_sha, "FATAL: Frozen benchmark file on disk was modified by permutation!"
    assert ctrl_a["benchmark_sha256_unmodified"] is True


def test_negative_control_shuffled_labels(shared_harness):
    """Negative Control A: When gold notes are permuted across cases in memory, recall must collapse below 5%."""
    ctrl_a = shared_harness.run_negative_control_shuffled_labels(num_seeds=5, seed_start=42)

    print(f"\n[Negative Control A] Mean recall: {ctrl_a['mean_recall'] * 100:.2f}%, Std: {ctrl_a['std_recall'] * 100:.2f}%")
    assert ctrl_a["mean_recall"] < 0.05, f"Shuffled labels recall {ctrl_a['mean_recall']} exceeds 5% threshold!"
    assert ctrl_a["passed"] is True
    assert len(ctrl_a["runs"]) == 5


def test_negative_control_gold_injection(shared_harness):
    """Negative Control B: When gold note is injected into results, harness must detect it 130/130 (100%)."""
    ctrl_b = shared_harness.run_negative_control_gold_injection()

    print(f"\n[Negative Control B] Detection: {ctrl_b['detected_count']}/{ctrl_b['total_cases']}")
    assert ctrl_b["detected_count"] == 130, f"Gold injection missed cases: {ctrl_b['failures']}"
    assert ctrl_b["passed"] is True
    assert ctrl_b["detection_rate"] == 1.0


def test_negative_control_determinism(shared_harness):
    """Negative Control C: Repeated executions across benchmark cases must have measurable stability."""
    ctrl_c = shared_harness.run_negative_control_determinism(num_runs=3)

    print(f"\n[Negative Control C] Identical: {ctrl_c['identical_cases_count']}/{ctrl_c['total_cases']}, Varying: {ctrl_c['varying_cases_count']}")
    assert ctrl_c["identical_cases_count"] >= 125, f"Too many varying cases: {ctrl_c['varying_cases_count']}"
    assert isinstance(ctrl_c["std_hits"], float)
    assert ctrl_c["passed"] is True


def test_harness_negative_control_synthetic_cases():
    """Negative control of the harness itself: validates exact categorization on synthetic cases.

    1. Gold note at rank 7 -> classified as PAGINATION_CUT with gold_rank=7.
    2. Gold note with zero lexical match / missing -> classified as NEVER_CANDIDATE with gold_rank=None.
    3. Gold note with RAW lifecycle -> classified as RAW_EXCLUDED.
    4. Gold note with unverified/draft lifecycle -> classified as AGENT_LIFECYCLE_FLOOR_EXCLUDED.
    """
    storage = StorageEngine()
    # Populate notes 1 to 14 with known rankings
    for i in range(1, 15):
        storage.set(
            f"note_{i:02d}",
            {
                "id": f"note_{i:02d}",
                "lifecycle": "ACTIVE",
                "type": "knowledge",
                "title": f"Active Note {i} on Memory Systems",
                "content": f"Detailed content on memory retrieval and cognitive architecture {i}.",
                "verification": "verified",
                "confidence": 0.95 - (i * 0.02),
                "source_type": "official",
            },
        )

    # Add raw note
    storage.set(
        "note_raw_99",
        {
            "id": "note_raw_99",
            "lifecycle": "RAW",
            "type": "knowledge",
            "title": "Raw note",
            "content": "Raw scratchpad content.",
            "verification": "unverified",
            "confidence": 0.1,
            "source_type": "unknown",
        },
    )

    # Add archived note (below agent floor)
    storage.set(
        "note_archived_88",
        {
            "id": "note_archived_88",
            "lifecycle": "ARCHIVED",
            "type": "knowledge",
            "title": "Archived old note",
            "content": "Archived content.",
            "verification": "unverified",
            "confidence": 0.4,
            "source_type": "ai",
        },
    )

    controller = MemoryController(storage=storage, ranking_arm=RANKING_ARM_BASELINE)
    harness = RetrievalLossFunnel(controller=controller, storage=storage)

    # Test 1: Case where gold note is note_07 (will be ranked outside top 5 page_size -> PAGINATION_CUT)
    case_rank_7 = {
        "id": "SYNTH-01",
        "class": "direct",
        "query": "Memory Systems",
        "gold_relevant_notes": ["note_07"],
        "language": "en",
    }
    diag_7 = harness.diagnose_case(case_rank_7, principal=Principal.AI_AGENT, page_size=5)
    assert diag_7.hit is False
    assert diag_7.gold_rank == 7, f"Expected gold_rank=7, got {diag_7.gold_rank}"
    assert diag_7.loss_category == ExclusionReason.PAGINATION_CUT.value
    assert ExclusionReason.PAGINATION_CUT.value in diag_7.reason_code_path

    # Test 2: Case where gold note was NEVER a candidate (note does not exist or completely absent)
    case_never = {
        "id": "SYNTH-02",
        "class": "multi_hop",
        "query": "quantum gravity entanglement",
        "gold_relevant_notes": ["non_existent_note_id_xyz"],
        "language": "en",
    }
    diag_never = harness.diagnose_case(case_never, principal=Principal.AI_AGENT, page_size=5)
    assert diag_never.hit is False
    assert diag_never.gold_rank is None
    assert diag_never.loss_category == "NEVER_CANDIDATE"

    # Test 3: Case where gold note is RAW
    case_raw = {
        "id": "SYNTH-03",
        "class": "direct",
        "query": "scratchpad",
        "gold_relevant_notes": ["note_raw_99"],
        "language": "en",
    }
    diag_raw = harness.diagnose_case(case_raw, principal=Principal.AI_AGENT, page_size=5)
    assert diag_raw.hit is False
    assert diag_raw.loss_category == ExclusionReason.RAW_EXCLUDED.value

    # Test 4: Case where gold note is sub-floor lifecycle (ARCHIVED)
    case_floor = {
        "id": "SYNTH-04",
        "class": "direct",
        "query": "draft content",
        "gold_relevant_notes": ["note_archived_88"],
        "language": "en",
    }
    diag_floor = harness.diagnose_case(case_floor, principal=Principal.AI_AGENT, page_size=5)
    assert diag_floor.hit is False
    assert diag_floor.loss_category == ExclusionReason.AGENT_LIFECYCLE_FLOOR_EXCLUDED.value
