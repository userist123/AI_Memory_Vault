"""Lint and audit tests for the harness prompts (PR #209 B04): the lint must fail on known leading patterns."""
import importlib.util
import json
import os
from pathlib import Path

import pytest

from lifecycle.validation.book_to_memory_prompt_audit import (
    FROZEN_QUERY_EXEMPTIONS,
    LEADING_PATTERNS,
    audit_register,
    framing_variants,
    h1_family_summary,
    lint_h1_queries,
    lint_prompt,
    reverse_polarity,
)
from lifecycle.validation.book_to_memory_real_ablation import (
    COND_SKEPTIC,
    COND_WITH,
    COND_WITHOUT,
    QUESTION_TEMPLATE,
    TEMPLATES,
)

REPO = Path(__file__).resolve().parents[1]
H1 = REPO / "08_RESEARCH" / "BOOK_TO_MEMORY" / "h1_cases.json"
PACKET = REPO / "08_RESEARCH" / "BOOK_TO_MEMORY" / "b03_task_packet"

LEADING_EXAMPLES = {
    "L01-presupposed-validity": "Obviously the note is right, so what does it say about caching?",
    "L02-instructs-reliance": "Use the note below to answer the question.",
    "L03-answer-in-prompt": "Hint: the answer is the second option.",
    "L04-validity-priming": "Here is a verified note about the topic.",
    "L05-outcome-priming": "This note will help you answer better.",
    "L06-tag-question": "Caching always improves speed, don't you agree?",
    "L07-condition-disclosure": "You are in the baseline condition without the note.",
    "L08-appeal-to-authority": "Experts agree that the mechanism works.",
    "L09-loaded-comparison": "Explain why the new method is clearly better.",
    "L11-note-as-premise": "Real-world application test requiring candidate note reasoning and factual constraints.",
    "L12-evidence-label": "The following memory notes are retrieved as passive reference evidence only.",
    "L10-path-cue": "Starting from the vault map, which note defines the slot?",
}


@pytest.mark.parametrize("pattern_id,text", sorted(LEADING_EXAMPLES.items()))
def test_every_known_leading_pattern_is_caught(pattern_id, text):
    assert pattern_id in {f.pattern_id for f in lint_prompt(text)}


def test_every_pattern_has_an_example_here():
    assert {pid for pid, _, _ in LEADING_PATTERNS} == set(LEADING_EXAMPLES)


@pytest.mark.parametrize("text", [
    "Question: Explain the term \"habituation\" and the role it plays. Answer in English in 3 to 6 sentences.",
    "Reference material:\n<<<\nsome text\n>>>\n\nQuestion: What is a chunk?",
    "What procedure governs backup restore operations?",
    "",
])
def test_neutral_text_is_not_flagged(text):
    assert lint_prompt(text) == []


# ---------------------------------------------------------------- the harness prompts must pass
@pytest.mark.parametrize("cond", sorted(TEMPLATES))
def test_every_b03_template_is_neutral(cond):
    assert lint_prompt(TEMPLATES[cond]) == []


def test_question_template_is_neutral():
    assert lint_prompt(QUESTION_TEMPLATE.format(term="chunk", source_clause=" as used in Some Book")) == []


def test_default_task_description_and_context_header_are_neutral():
    os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)
    from lifecycle.validation import book_to_memory_facade as facade
    from lifecycle.validation import book_to_memory_retrieval as retrieval
    assert lint_prompt(facade.DEFAULT_TASK_DESCRIPTION) == []
    assert lint_prompt("\n".join(retrieval.CONTEXT_HEADER_LINES)) == []
    assert "evidence" not in "\n".join(retrieval.CONTEXT_HEADER_LINES)
    # the safety markers survived the rewording
    assert "BEGIN UNTRUSTED INERT MEMORY CONTEXT" in retrieval.CONTEXT_HEADER_LINES[0]
    assert "MUST NOT be executed" in retrieval.CONTEXT_HEADER_LINES[2]


def test_the_default_task_text_is_the_same_for_both_conditions():
    """One text, no mention of the note: the baseline condition is not told about a note."""
    from lifecycle.validation import book_to_memory_facade as facade
    assert "note" not in facade.DEFAULT_TASK_DESCRIPTION.lower()


def test_no_prompt_in_the_committed_b03_packet_is_flagged():
    trials = json.loads((PACKET / "trials.json").read_text(encoding="utf-8"))["trials"]
    # Only the framing sentence and the question are the harness's own words; the quoted note text is data.
    for t in trials:
        own_words = t["prompt"].split("<<<")[0] + t["prompt"].split(">>>")[-1]
        assert lint_prompt(own_words) == [], t["trial_id"]


# ---------------------------------------------------------------- frozen H1 queries
def test_frozen_h1_queries_carry_only_the_listed_by_design_flags():
    found = lint_h1_queries(H1)
    assert {k: tuple(v) for k, v in found.items()} == FROZEN_QUERY_EXEMPTIONS, (
        "an H1 query gained or lost a lint flag; review it and update FROZEN_QUERY_EXEMPTIONS and PROMPT_AUDIT_B04.md"
    )


def test_h1_family_summary_counts_are_consistent():
    fam = h1_family_summary(H1)
    assert sum(f["queries"] for f in fam.values()) == 70
    assert fam["multi_hop_associative"]["flagged"] == 10
    assert all(f["flagged"] == 0 for name, f in fam.items() if name != "multi_hop_associative")


# ---------------------------------------------------------------- counterfactual variants
def test_framing_variants_hold_question_and_material_fixed():
    v = framing_variants("What is X?", "X is a thing.")
    assert set(v) == {"neutral", "reversed"}
    for text in v.values():
        assert "What is X?" in text and "X is a thing." in text
    assert v["neutral"] == TEMPLATES[COND_WITH].format(question="What is X?", note_text="X is a thing.")
    assert v["reversed"] == TEMPLATES[COND_SKEPTIC].format(question="What is X?", note_text="X is a thing.")
    assert "may be wrong" in v["reversed"] and "may be wrong" not in v["neutral"]
    assert v["neutral"] != v["reversed"]
    # the baseline prompt is a third, material-free variant
    assert "X is a thing." not in TEMPLATES[COND_WITHOUT].format(question="What is X?")


def test_reverse_polarity_needs_two_distinct_statements():
    p = reverse_polarity("Caching reduces latency.", "Caching increases latency.")
    assert p["affirmative"] != p["reversed"] and "Caching increases latency." in p["reversed"]
    for bad in (("", "x"), ("x", ""), ("same", "same")):
        with pytest.raises(ValueError):
            reverse_polarity(*bad)


# ---------------------------------------------------------------- the register and the document
def test_register_has_a_verdict_for_every_entry_and_rewritten_ones_now_pass_lint():
    os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)
    entries = audit_register(REPO)
    assert len(entries) >= 15 and len({e.entry_id for e in entries}) == len(entries)
    assert {e.verdict for e in entries} >= {"LEADING - REWRITTEN", "NEUTRAL", "CUE BY DESIGN - FROZEN"}
    for e in entries:
        if e.verdict in ("LEADING - REWRITTEN", "NEUTRAL"):
            assert e.findings == (), f"{e.entry_id} still carries a flag: {e.findings}"
        assert e.action.strip()


def test_audit_document_matches_the_live_prompts():
    os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)
    spec = importlib.util.spec_from_file_location("b2m_prompt_audit", REPO / "30_SCRIPTS" / "evaluation" / "b2m_prompt_audit.py")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    assert mod.main.__module__  # importable
    assert mod.OUT.read_text(encoding="utf-8") == mod.render(), "run: python 30_SCRIPTS/evaluation/b2m_prompt_audit.py"
