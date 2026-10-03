import pytest

from validate_h1_corpus import validate_corpus


def _corpus():
    return {
        "notes": [
            {"id": "A", "title": "Alpha", "excerpt": "required fact"},
            {"id": "RAW", "title": "Raw", "excerpt": "required fact", "lifecycle": "RAW"},
            {"id": "B", "title": "Beta", "excerpt": "other fact"},
            {"id": "C", "title": "Gamma", "excerpt": "target fact"},
        ],
        "links": [
            {"source": "A", "target": "B", "relation": "depends_on"},
            {"source": "B", "target": "C", "relation": "related_to"},
        ],
    }


def _base_case(**overrides):
    case = {
        "id": "H1-001",
        "family": "indirect_cue",
        "query": "indirect question",
        "gold_relevant_notes": ["A"],
        "required_facts": ["required fact"],
        "abstain": False,
        "principal": "HUMAN",
        "intended_boundary": "candidate_generation",
        "corpus_commit": "abc",
        "corpus_hash": "hash",
    }
    case.update(overrides)
    return case


def test_missing_gold_blocks_corpus():
    payload = {"corpus_commit": "abc", "corpus_hash": "hash", "cases": [_base_case(gold_relevant_notes=["MISSING"])]}
    result = validate_corpus(payload, _corpus())
    assert not result["valid"]
    assert any("missing_gold:MISSING" in error for error in result["errors"])


def test_required_fact_must_exist_in_gold_evidence():
    payload = {"corpus_commit": "abc", "corpus_hash": "hash", "cases": [_base_case(required_facts=["not present"])]}
    result = validate_corpus(payload, _corpus())
    assert not result["valid"]
    assert any("missing_required_fact:A:not present" in error for error in result["errors"])


def test_raw_gold_is_rejected():
    payload = {"corpus_commit": "abc", "corpus_hash": "hash", "cases": [_base_case(gold_relevant_notes=["RAW"])]}
    result = validate_corpus(payload, _corpus())
    assert not result["valid"]
    assert any("ineligible_gold_lifecycle:RAW:RAW" in error for error in result["errors"])


def test_multihop_requires_directionally_exact_edges():
    payload = {
        "corpus_commit": "abc",
        "corpus_hash": "hash",
        "cases": [_base_case(
            family="multi_hop_associative",
            gold_relevant_notes=["C"],
            required_facts=["target fact"],
            graph_path=[
                {"source": "A", "target": "B", "relation": "depends_on"},
                {"source": "C", "target": "B", "relation": "related_to"}
            ],
        )],
    }
    result = validate_corpus(payload, _corpus())
    assert not result["valid"]
    assert any("missing_or_reversed_edge:C:B:related_to" in error for error in result["errors"])


def test_duplicate_case_ids_are_rejected():
    case = _base_case()
    case2 = _base_case(id="H1-001")
    payload = {"corpus_commit": "abc", "corpus_hash": "hash", "cases": [case, case2]}
    result = validate_corpus(payload, _corpus())
    assert not result["valid"]
    assert any("duplicate_case_id:H1-001" in error for error in result["errors"])


def test_multiple_gold_requires_reason():
    payload = {"corpus_commit": "abc", "corpus_hash": "hash", "cases": [_base_case(gold_relevant_notes=["A", "B"])]}
    result = validate_corpus(payload, _corpus())
    assert not result["valid"]
    assert "H1-001:multiple_gold_requires_reason" in result["errors"]


def test_target_reuse_is_warning_during_draft_and_error_in_final():
    case1 = _base_case(id="H1-001")
    case2 = _base_case(id="H1-002")
    payload = {"corpus_commit": "abc", "corpus_hash": "hash", "cases": [case1, case2]}
    draft = validate_corpus(payload, _corpus(), final=False)
    final = validate_corpus(payload, _corpus(), final=True)
    assert draft["valid"]
    assert any("gold_target_reused:A:2" in w for w in draft["warnings"])
    assert not final["valid"]
    assert any("gold_target_reused_in_final:A:2" in e for e in final["errors"])
