"""The prompt compiler must produce a brief that is complete and in English.

A request arrives as one informal line. What reaches another agent has to be a
full brief, or the receiver begins by reconstructing intent and context the
sender already had — paid for twice, once in re-explanation and once in
rediscovery.

The compiler fills the deterministic half from the live vault. These tests pin
the properties that make it worth using: the facts are measured rather than
copied, a fact it cannot measure is visible rather than omitted, and the
sections that require judgement are marked so a half-written brief cannot pass
for a finished one.
"""
import importlib.util
import re
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "30_SCRIPTS" / "prompt" / "compile_task_prompt.py"

_spec = importlib.util.spec_from_file_location("compile_task_prompt", SCRIPT)
compiler = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(compiler)


@pytest.fixture(scope="module")
def prompt():
    return compiler.compile_prompt(
        task="Wire component X into the production path and measure the effect",
        branch="rXXX/example",
        owner="EXAMPLE AGENT",
    )


def test_the_compiler_exists_and_runs(prompt):
    assert len(prompt) > 1500, "a brief this short is not a brief"


def test_context_is_measured_not_copied(prompt):
    """Numbers must come from the live vault. A figure transcribed from a
    document rots exactly like the docstring that claimed for months that
    SynapseStore was not wired into search()."""
    facts = compiler.measured_state()
    assert facts, "no state measured"
    for key, value in facts.items():
        if "UNAVAILABLE" in value:
            continue
        assert value in prompt, f"measured fact {key!r} did not reach the prompt"


def test_an_unmeasurable_fact_is_reported_not_omitted():
    """Silently dropping a line the receiver expects is worse than saying it
    could not be obtained."""
    src = SCRIPT.read_text(encoding="utf-8")
    assert "UNAVAILABLE" in src, (
        "measurement failures must surface in the prompt; an omitted line is "
        "indistinguishable from a fact that does not apply"
    )


def test_recorded_methods_are_offered_to_the_receiver(prompt):
    """The point of procedural memory is that the next agent reads it before
    diagnosing, not after."""
    methods = compiler.recorded_methods()
    assert methods, "no method notes found; procedural memory is not reaching briefs"
    for path, _title in methods:
        assert path in prompt, f"recorded method {path} missing from the brief"


def test_standing_traps_are_included(prompt):
    for fragment in ("shim", "worktree", "canonical LF"):
        assert fragment in prompt, f"trap missing from the brief: {fragment}"


def test_acceptance_states_what_finished_means(prompt):
    """A brief without a definition of done invites a task reported finished
    while unfinished — the failure the completion contract exists to prevent."""
    assert "finished when all five hold" in prompt
    assert "Remaining gaps" in prompt or "remains open" in prompt
    assert "green suite is not this on its own" in prompt.lower() or \
           "A green suite is not this on its own" in prompt


def test_judgement_sections_are_marked_unfinished(prompt):
    """Task, requirements and forbidden need the sender's judgement. Leaving
    them silently blank would let an empty brief look complete."""
    assert prompt.count("TODO") >= 2, (
        "the sections requiring judgement must be visibly incomplete until filled"
    )


def test_the_brief_is_english(prompt):
    """Replies to the user are Romanian; everything transmitted to an agent is
    English. Detail lost in translation is detail lost."""
    romanian = re.findall(
        r"\b(?:trebuie|foloseste|folosește|verifica|verifică|masoara|măsoară|"
        r"schimbari|schimbări|pentru|catre|către|acesta|aceasta)\b",
        prompt, re.I,
    )
    assert not romanian, f"Romanian leaked into an AI-facing prompt: {set(romanian)}"


def test_the_preference_recording_this_rule_exists():
    pref = REPO / "01_ARCHITECTURE" / "memory" / "Preferences" / "AI_Facing_Prompts_In_English.md"
    assert pref.exists(), "the rule must live in the vault, not only in the tooling"
    text = pref.read_text(encoding="utf-8")
    assert "lifecycle: REVIEW" in text, "a preference does not promote itself"
    assert "## Still open" in text
