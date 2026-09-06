"""Intent inference: what it can do, and the measured limit of what it cannot.

Level 1 was `--intent measure`, where the sender already knew. This proposes a
kind from the request text and, more importantly, flags when the literal
request and the actual question disagree.

The numbers below are pinned deliberately. Measured on requests held out from
the patterns' construction, plain classification scores 50%, against 100% on
the requests the patterns were drawn from — a textbook overfit, and stating it
is the point. Conflict detection scores 86% and generalises to phrasings never
seen, because its rule is semantic rather than lexical: an action on production
state whose object has no established quality is a question about quality
wearing the clothes of an instruction.

So the module refuses on weak signal instead of guessing. A confident wrong
label produces a brief silently missing the requirements that kind of work
needed, and the omission is invisible until the work is finished wrongly.
"""
import importlib.util
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "30_SCRIPTS" / "prompt" / "infer_intent.py"

_spec = importlib.util.spec_from_file_location("infer_intent", SCRIPT)
infer_mod = importlib.util.module_from_spec(_spec)
# Registered before execution: `@dataclass` resolves annotations through
# `sys.modules[cls.__module__]`, which is None for a module loaded from a path
# and never registered.
sys.modules["infer_intent"] = infer_mod
_spec.loader.exec_module(infer_mod)
infer = infer_mod.infer

#: Requests whose patterns this module was built from. 100% here proves
#: nothing except that the patterns match what they were drawn from.
TUNING = [
    ("vezi daca graful chiar ajuta", "measure"),
    ("promoveaza muchiile propuse", "measure"),
    ("verifica ce a facut antigravity", "verify"),
]

#: Requests the module declines even though its patterns were drawn from them.
#: A single marker is not enough signal, and the honest consequence is that
#: coverage is low: it classifies confidently or not at all.
REFUSED = [
    "storage-ul nu vede vault-ul, repara",
    "masoara precizia proposer-ului",
    "scoate notele duplicate din index",
]

#: The conflict cases: an action on production state, on something unproven.
#: Three of these use phrasings the patterns were never shown.
CONFLICTS = [
    "promoveaza muchiile propuse",
    "promote the proposed edges",
    "activeaza graph expansion in productie",
    "enable the experimental reranker",
    "merge the candidate edges into main",
    "deploy the draft policy",
]


@pytest.mark.parametrize("request_text,expected", TUNING)
def test_known_phrasings_are_classified(request_text, expected):
    result = infer(request_text)
    assert result.intent == expected


@pytest.mark.parametrize("request_text", CONFLICTS)
def test_acting_on_something_unproven_is_a_measurement(request_text):
    """The case this exists for. Treated as an instruction, 'promote the
    proposed edges' promotes some two thousand edges; treated as a question, it
    samples fifty and stops at 18% precision against a 70% bar, which is what
    happened."""
    result = infer(request_text)
    assert result.intent == "measure", f"{request_text!r} was not caught as a question"
    assert result.conflict, "the conflict must be shown, not just acted on"
    assert "semantic intent" in result.conflict


def test_conflict_detection_generalises_beyond_its_examples():
    """Its rule is semantic, not lexical, so phrasings never seen still trip
    it. This is the half of the module that is worth trusting."""
    unseen = ["enable the experimental reranker", "deploy the draft policy",
              "merge the candidate edges into main"]
    caught = sum(1 for r in unseen if infer(r).intent == "measure")
    assert caught == len(unseen), f"only {caught}/{len(unseen)} unseen conflicts caught"


@pytest.mark.parametrize("request_text", REFUSED)
def test_weak_signal_refuses_instead_of_guessing(request_text):
    """Four of five errors on held-out requests carried a single weak marker,
    so a single marker now declines. The cost is coverage — two of these were
    requests the patterns were built from — and the honest trade is a question
    back to the sender instead of a coin toss."""
    result = infer(request_text)
    assert result.intent is None
    assert "explicitly" in (result.conflict or "")


def test_an_unrecognised_request_is_refused_not_defaulted():
    result = infer("frobnicate the widget manifold")
    assert result.intent is None, "defaulting to implement hides a misclassification"
    assert result.confidence == "none"


def test_the_module_states_its_own_measured_accuracy():
    """A tool that reports its reliability can be trusted proportionally. One
    that does not invites being trusted absolutely."""
    source = SCRIPT.read_text(encoding="utf-8")
    assert "50%" in source, "the held-out accuracy must stay visible in the source"


def test_inference_never_silently_selects():
    """Every proposal carries the evidence that produced it, so a wrong one is
    visible rather than merely wrong."""
    for request_text, _ in TUNING:
        result = infer(request_text)
        if result.intent is not None:
            assert result.reasons, f"{request_text!r} proposed an intent with no reason"
