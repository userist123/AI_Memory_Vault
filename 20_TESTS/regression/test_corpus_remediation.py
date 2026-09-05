"""20_TESTS/regression/test_corpus_remediation.py — P3-B Corpus Remediation Test Suite.

Validates:
1. P3-B.1 Note Classifier categories:
   - template stub
   - canonical knowledge
   - duplicate
   - near duplicate
   - real lesson
   - test artifact
   - generated artifact
2. P3-B.2 & P3-B.3 Clustering and Canonical Representative:
   - Exact duplicate clustering and representative selection
   - Deterministic tie-breaking prioritizing verified > ACTIVE > provenance
3. P3-B.4 & P3-B.5 Policy Simulation & Safety Rule:
   - CONSERVATIVE, BALANCED, STRICT simulation
   - Safety rule: ZERO ACTIVE + verified notes can ever be removed
4. P3-B.6 Golden Regression Dataset:
   - Structured JSON validation
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from cognitive_core.benchmarks.corpus_remediation import (
    CleanupPolicy,
    CorpusNoteClassifier,
    NoteCategory,
    cluster_corpus,
    generate_golden_regression_dataset,
    pick_canonical_representative,
    simulate_cleanup_policy,
)
from cognitive_core.vault_index import Note, VaultIndex


@pytest.fixture
def remediation_vault(tmp_path: Path) -> VaultIndex:
    notes = [
        # Canonical active verified
        (
            "01_ARCHITECTURE/memory/knw-canonical-01.md",
            "id: knw-canonical-01\ntitle: Architecture Protocol Spec\ntype: knowledge\nlifecycle: ACTIVE\nverification: verified\nprovenance:\n  source_type: official\n",
            "This is a substantive canonical architecture standard for distributed state transactions.",
        ),
        # Exact duplicate of knw-canonical-01 in review
        (
            "01_ARCHITECTURE/memory/dup-review-01.md",
            "id: dup-review-01\ntitle: Duplicate Architecture Spec\ntype: knowledge\nlifecycle: REVIEW\nverification: unverified\nprovenance:\n  source_type: ai\n",
            "This is a substantive canonical architecture standard for distributed state transactions.",
        ),
        # Template stub
        (
            "01_ARCHITECTURE/memory/policy-lesson_template_stub.md",
            "id: policy-lesson_template_stub\ntitle: Policy Lesson Template\ntype: lesson\nlifecycle: REVIEW\nverification: unverified\n",
            "{{insert_policy_here}} [placeholder]",
        ),
        # Test artifact
        (
            "01_ARCHITECTURE/test_fixtures/test_synthetic_fixture.md",
            "id: test_synthetic_fixture\ntitle: Test Fixture Note\ntype: knowledge\nlifecycle: REVIEW\nverification: unverified\n",
            "Test fixture content for unit tests only.",
        ),
        # Real lesson
        (
            "01_ARCHITECTURE/memory/les-incident-01.md",
            "id: les-incident-01\ntitle: Incident Retrospective WAL Deadlock\ntype: lesson\nlifecycle: ACTIVE\nverification: verified\nprovenance:\n  source_type: experience\n",
            "Detailed postmortem analysis of SQLite WAL lock contention under concurrent write load with solution.",
        ),
    ]

    for rel, fm, b in notes:
        p = tmp_path / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(f"---\n{fm}\n---\n{b}", encoding="utf-8")

    return VaultIndex.load(tmp_path)


# ---------------------------------------------------------------------------
# 1. P3-B.1 Classifier Tests
# ---------------------------------------------------------------------------

def test_classifier_identifies_all_categories(remediation_vault: VaultIndex):
    classifier = CorpusNoteClassifier(remediation_vault)

    cls_canon = classifier.classify_note(remediation_vault.by_id["knw-canonical-01"])
    assert cls_canon.category == NoteCategory.CANONICAL_KNOWLEDGE.value

    cls_dup = classifier.classify_note(remediation_vault.by_id["dup-review-01"])
    assert cls_dup.category == NoteCategory.DUPLICATE.value
    assert "Exact body hash match" in cls_dup.reason

    cls_stub = classifier.classify_note(remediation_vault.by_id["policy-lesson_template_stub"])
    assert cls_stub.category == NoteCategory.TEMPLATE_STUB.value

    cls_test = classifier.classify_note(remediation_vault.by_id["test_synthetic_fixture"])
    assert cls_test.category == NoteCategory.TEST_ARTIFACT.value

    cls_lesson = classifier.classify_note(remediation_vault.by_id["les-incident-01"])
    assert cls_lesson.category == NoteCategory.REAL_LESSON.value


# ---------------------------------------------------------------------------
# 2. P3-B.2 & P3-B.3 Duplicate Clustering & Representative Selection
# ---------------------------------------------------------------------------

def test_clustering_and_representative_selection(remediation_vault: VaultIndex):
    classifier = CorpusNoteClassifier(remediation_vault)
    clusters = cluster_corpus(remediation_vault, classifier)

    assert len(clusters) >= 1
    exact_cluster = next((c for c in clusters if c.cluster_type == "exact_duplicate"), None)
    assert exact_cluster is not None
    assert "knw-canonical-01" in exact_cluster.members
    assert "dup-review-01" in exact_cluster.members

    # Representative must be knw-canonical-01 (ACTIVE + verified + official)
    assert exact_cluster.representative == "knw-canonical-01"
    assert "verified" in exact_cluster.selection_reason
    assert "ACTIVE" in exact_cluster.selection_reason


def test_representative_selection_determinism(remediation_vault: VaultIndex):
    n1 = remediation_vault.by_id["knw-canonical-01"]
    n2 = remediation_vault.by_id["dup-review-01"]

    rep1, reason1 = pick_canonical_representative([n1, n2])
    rep2, reason2 = pick_canonical_representative([n2, n1])

    assert rep1.id == "knw-canonical-01"
    assert rep2.id == "knw-canonical-01"
    assert reason1 == reason2


# ---------------------------------------------------------------------------
# 3. P3-B.4 & P3-B.5 Policy Simulation & Safety Rule Enforcement
# ---------------------------------------------------------------------------

def test_safety_rule_strictly_protects_active_verified_notes(remediation_vault: VaultIndex):
    classifier = CorpusNoteClassifier(remediation_vault)
    clusters = cluster_corpus(remediation_vault, classifier)

    for policy in [CleanupPolicy.CONSERVATIVE, CleanupPolicy.BALANCED, CleanupPolicy.STRICT]:
        res = simulate_cleanup_policy(remediation_vault, classifier, policy, clusters)
        # Invariant: active_verified_removed MUST be 0!
        assert res.active_verified_removed == 0
        assert res.active_verified_retained == 2  # knw-canonical-01 and les-incident-01
        assert res.notes_retained >= 2


def test_cleanup_policy_conservative_vs_balanced(remediation_vault: VaultIndex):
    classifier = CorpusNoteClassifier(remediation_vault)
    clusters = cluster_corpus(remediation_vault, classifier)

    res_cons = simulate_cleanup_policy(remediation_vault, classifier, CleanupPolicy.CONSERVATIVE, clusters)
    res_bal = simulate_cleanup_policy(remediation_vault, classifier, CleanupPolicy.BALANCED, clusters)

    # Balanced removes template stubs in addition to exact duplicates
    assert res_bal.notes_removed >= res_cons.notes_removed
    assert res_bal.notes_retained <= res_cons.notes_retained


# ---------------------------------------------------------------------------
# 4. P3-B.6 Golden Regression Dataset
# ---------------------------------------------------------------------------

def test_golden_regression_dataset_structure(remediation_vault: VaultIndex):
    classifier = CorpusNoteClassifier(remediation_vault)
    clusters = cluster_corpus(remediation_vault, classifier)
    dataset = generate_golden_regression_dataset(remediation_vault, classifier, clusters)

    assert "golden_samples" in dataset
    samples = dataset["golden_samples"]
    assert "canonical_active_verified" in samples
    assert "review_unverified_sample" in samples
    assert "exact_duplicate_cluster" in samples
    assert "template_cluster" in samples
