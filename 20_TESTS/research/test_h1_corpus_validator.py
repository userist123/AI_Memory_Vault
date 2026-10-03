import importlib.util
from pathlib import Path

_MODULE = Path(__file__).parents[2] / "08_RESEARCH" / "BOOK_TO_MEMORY" / "validate_h1_corpus.py"
_SPEC = importlib.util.spec_from_file_location("validate_h1_corpus", _MODULE)
_MOD = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(_MOD)
validate_corpus = _MOD.validate_corpus
canonical_hash = _MOD.canonical_hash
benchmark_hash = _MOD.benchmark_hash


def _corpus():
    return {
        "notes": [
            {"id": "A", "title": "Alpha", "excerpt": "required fact", "lifecycle": "ACTIVE"},
            {"id": "RAW", "title": "Raw", "excerpt": "required fact", "lifecycle": "RAW"},
            {"id": "B", "title": "Beta", "excerpt": "other fact", "lifecycle": "ACTIVE"},
            {"id": "C", "title": "Gamma", "excerpt": "target fact", "lifecycle": "ACTIVE"},
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
        "split": "development",
        "corpus_commit": "abc",
        "corpus_hash": canonical_hash(_corpus()),
    }
    case.update(overrides)
    return case


def _payload(*cases):
    payload = {"corpus_commit": "abc", "corpus_hash": canonical_hash(_corpus()), "cases": list(cases)}
    payload["benchmark_hash"] = benchmark_hash(payload)
    return payload


def test_missing_gold_blocks_corpus():
    result = validate_corpus(_payload(_base_case(gold_relevant_notes=["MISSING"])), _corpus())
    assert not result["valid"]
    assert any("missing_gold:MISSING" in error for error in result["errors"])


def test_required_fact_must_exist_in_gold_evidence():
    result = validate_corpus(_payload(_base_case(required_facts=["not present"])), _corpus())
    assert not result["valid"]
    assert any("missing_required_fact:A:not present" in error for error in result["errors"])


def test_raw_gold_is_rejected():
    result = validate_corpus(_payload(_base_case(gold_relevant_notes=["RAW"])), _corpus())
    assert not result["valid"]
    assert any("ineligible_gold_lifecycle:RAW:RAW" in error for error in result["errors"])


def test_multihop_requires_directionally_exact_edges():
    case = _base_case(
        family="multi_hop_associative",
        gold_relevant_notes=["C"],
        required_facts=["target fact"],
        graph_path=[
            {"source": "A", "target": "B", "relation": "depends_on"},
            {"source": "C", "target": "B", "relation": "related_to"},
        ],
    )
    result = validate_corpus(_payload(case), _corpus())
    assert not result["valid"]
    assert any("missing_or_reversed_edge:C:B:related_to" in error for error in result["errors"])


def test_duplicate_case_ids_are_rejected():
    result = validate_corpus(_payload(_base_case(), _base_case(id="H1-001")), _corpus())
    assert not result["valid"]
    assert any("duplicate_case_id:H1-001" in error for error in result["errors"])


def test_multiple_gold_requires_reason():
    result = validate_corpus(_payload(_base_case(gold_relevant_notes=["A", "B"])), _corpus())
    assert not result["valid"]
    assert "H1-001:multiple_gold_requires_reason" in result["errors"]


def test_target_reuse_is_warning_during_draft_and_error_in_final():
    case1 = _base_case(id="H1-001")
    case2 = _base_case(id="H1-002")
    draft = validate_corpus(_payload(case1, case2), _corpus(), final=False)
    final = validate_corpus(_payload(case1, case2), _corpus(), final=True)
    assert draft["valid"]
    assert any("gold_target_reused:A:2" in w for w in draft["warnings"])
    assert not final["valid"]
    assert any("gold_target_reused_in_final:A:2" in e for e in final["errors"])


def test_query_and_principal_are_required_and_valid():
    missing = validate_corpus(_payload(_base_case(query="", principal="")), _corpus())
    assert not missing["valid"]
    assert "H1-001:missing_query" in missing["errors"]
    assert "H1-001:invalid_principal" in missing["errors"]

    invalid = validate_corpus(_payload(_base_case(principal="ROOT")), _corpus())
    assert not invalid["valid"]
    assert "H1-001:invalid_principal" in invalid["errors"]


def test_answerable_case_requires_required_facts():
    result = validate_corpus(_payload(_base_case(required_facts=[])), _corpus())
    assert not result["valid"]
    assert "H1-001:answerable_case_has_no_required_facts" in result["errors"]


def test_multihop_path_must_be_connected():
    case = _base_case(
        family="multi_hop_associative",
        gold_relevant_notes=["C"],
        required_facts=["target fact"],
        graph_path=[
            {"source": "A", "target": "B", "relation": "depends_on"},
            {"source": "A", "target": "C", "relation": "related_to"},
        ],
    )
    result = validate_corpus(_payload(case), _corpus())
    assert not result["valid"]
    assert any("disconnected_graph_path:1:B->A" in error for error in result["errors"])


def test_ai_agent_cannot_use_classified_gold_under_default_search_floor():
    corpus = _corpus()
    corpus["notes"][0]["lifecycle"] = "CLASSIFIED"
    case = _base_case(principal="AI_AGENT")
    payload = {"corpus_commit": "abc", "corpus_hash": canonical_hash(corpus), "cases": [dict(case, corpus_hash=canonical_hash(corpus))]}
    payload["benchmark_hash"] = benchmark_hash(payload)
    result = validate_corpus(payload, corpus)
    assert not result["valid"]
    assert any("ineligible_gold_for_principal:A:AI_AGENT:CLASSIFIED" in error for error in result["errors"])


def test_human_can_use_normalized_gold():
    corpus = _corpus()
    corpus["notes"][0]["lifecycle"] = "NORMALIZED"
    case = _base_case()
    payload = {"corpus_commit": "abc", "corpus_hash": canonical_hash(corpus), "cases": [dict(case, corpus_hash=canonical_hash(corpus))]}
    result = validate_corpus(payload, corpus)
    assert result["valid"]


def test_abstain_case_cannot_declare_required_facts():
    result = validate_corpus(
        _payload(_base_case(abstain=True, gold_relevant_notes=[], required_facts=["should not exist"])),
        _corpus(),
    )
    assert not result["valid"]
    assert "H1-001:abstain_case_has_required_facts" in result["errors"]


def test_duplicate_gold_ids_are_rejected():
    result = validate_corpus(
        _payload(_base_case(gold_relevant_notes=["A", "A"])),
        _corpus(),
    )
    assert not result["valid"]
    assert "H1-001:duplicate_gold_id" in result["errors"]


def test_final_gold_reuse_is_allowed_only_for_documented_conflict_design():
    case1 = _base_case(
        id="H1-001",
        family="conflict",
        gold_reuse_reason="Same target is intentionally tested against two contextual conflict prompts.",
        rationale="Conflict case one.",
        query="conflict context question",
    )
    case2 = _base_case(
        id="H1-002",
        family="distractor",
        gold_reuse_reason="Same target is intentionally reused to test distractor competition.",
        rationale="Distractor case two.",
        query="distractor competition question",
    )
    final = validate_corpus(_payload(case1, case2), _corpus(), final=True)
    assert final["valid"]


def test_final_requires_held_out_split():
    result = validate_corpus(_payload(_base_case()), _corpus(), final=True)
    assert not result["valid"]
    assert "final_case_not_held_out:H1-001" in result["errors"]


def test_held_out_cannot_carry_expected_baseline():
    case = _base_case(split="held_out", expected_baseline={"rank": 1})
    result = validate_corpus(_payload(case), _corpus())
    assert not result["valid"]
    assert "H1-001:held_out_case_has_expected_baseline" in result["errors"]


def test_final_duplicate_query_is_rejected():
    case1 = _base_case(id="H1-001", split="held_out")
    case2 = _base_case(id="H1-002", split="held_out")
    result = validate_corpus(_payload(case1, case2), _corpus(), final=True)
    assert not result["valid"]
    assert any(error.startswith("duplicate_final_query:") for error in result["errors"])


def test_benchmark_hash_changes_when_case_changes():
    payload = _payload(_base_case())
    changed = dict(payload)
    changed["cases"] = [dict(payload["cases"][0], query="changed frozen query")]
    assert benchmark_hash(payload) != benchmark_hash(changed)


def test_benchmark_hash_mismatch_blocks_corpus():
    payload = _payload(_base_case())
    payload["benchmark_hash"] = "wrong"
    result = validate_corpus(payload, _corpus())
    assert not result["valid"]
    assert "benchmark_hash_mismatch" in result["errors"]
