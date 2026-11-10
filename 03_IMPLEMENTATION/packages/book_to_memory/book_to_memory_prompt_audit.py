"""Prompt-bias audit for the Book-to-Memory harness (PR #209 finding B04).

A task prompt can decide the result by itself: wording that tells the model the note is right, that
the note will help, or what the answer is, measures the wording and not the note. This module

* lists the **known leading patterns** (:data:`LEADING_PATTERNS`) and :func:`lint_prompt` that finds them;
* builds **counterfactual variants** of a prompt (:func:`framing_variants`): the neutral framing and a
  reversed (sceptical) framing of the same reference material, so a run can show whether an effect
  survives a change of framing;
* holds the **audit register** (:func:`audit_register`): every task prompt or template of the harness,
  with its verdict, read live from the code so the table cannot drift from the prompts.

The H1 benchmark queries are frozen (their hash is bound into the experiment runner); they are linted
and reported but never rewritten here. Their by-design cues are listed in :data:`FROZEN_QUERY_EXEMPTIONS`,
and a test fails if a frozen query gains a flag that is not listed.
"""
from __future__ import annotations

import json
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence, Tuple

from .book_to_memory_real_ablation import (
    COND_SKEPTIC,
    COND_WITH,
    COND_WITHOUT,
    QUESTION_TEMPLATE,
    TEMPLATES,
)

#: (pattern id, regex, what is wrong with it). All case-insensitive.
LEADING_PATTERNS: Tuple[Tuple[str, "re.Pattern[str]", str], ...] = tuple(
    (pid, re.compile(rx, re.I), why) for pid, rx, why in (
        ("L01-presupposed-validity",
         r"\b(obviously|clearly|of course|naturally|undoubtedly|needless to say|it is (well[- ])?known|as everyone knows|everyone agrees)\b",
         "asserts the answer or the premise is beyond doubt"),
        ("L02-instructs-reliance",
         r"\b(use|rely on|trust|follow|apply|base your answer on)\s+(the|this|that|these)\s+(note|notes|reference|material|memory|passage)\b"
         r"|\baccording to (the|this) (verified |validated |trusted |authoritative )?(note|memory)\b"
         r"|\bbased on the (verified|validated|trusted) note\b",
         "tells the model to lean on the note instead of letting it choose"),
        ("L03-answer-in-prompt",
         r"\b(the (correct )?answer is|hint\s*[:\-]|the solution is|you should answer|the right answer)\b",
         "gives the answer away"),
        ("L04-validity-priming",
         r"\b(verified|validated|trusted|authoritative|expert|proven|high[- ]confidence|high[- ]quality|reliable)\s+(note|notes|memory|material|reference|source|passage)\b",
         "labels the reference as reliable, which is a claim the experiment is supposed to test"),
        ("L05-outcome-priming",
         r"\b(this (note|material|reference) (will )?(help|improve|ensure)|improv\w+ (your|the) (answer|accuracy|performance)|you will (do|perform) better)\b",
         "promises the reference improves the answer"),
        ("L06-tag-question",
         r"\b(isn'?t it true that|is it not true that|don'?t you (agree|think)|wouldn'?t you (say|agree))\b|\b(right|correct)\?\s*$",
         "invites agreement with a premise"),
        ("L07-condition-disclosure",
         r"\b(with(out)? (the |a )?note|baseline|control (group|condition)|treatment (group|condition)|ablation)\b",
         "tells the model which experimental condition it is in"),
        ("L08-appeal-to-authority",
         r"\b(experts (agree|say|believe)|research (shows|proves)|studies (show|prove)|scientists (agree|know))\b",
         "borrows authority for the premise"),
        ("L09-loaded-comparison",
         r"\b(obviously|clearly|much|far) (better|superior|worse)\b",
         "loads the comparison before the answer"),
        ("L11-note-as-premise",
         r"\b(requir\w+|using|use of|relying on)\s+(the |a |candidate )*note\b|\bcandidate note\b|\bnote reasoning\b",
         "presupposes that the answer uses a note, so the baseline condition is told about it too"),
        ("L12-evidence-label",
         r"\b(reference|supporting|retrieved|retrieval) evidence\b",
         "calls the material evidence, which presupposes it supports something"),
        ("L10-path-cue",
         r"\b(following the relationship|starting from|tracing from|navigating from)\b|^\s*(following\b.{3,80}?\bthrough|from .{3,80}?\b(through|via)|through the .{3,60}? from)\b",
         "names the intermediate hop in a retrieval query (a cue; by design in the multi-hop family)"),
    )
)


@dataclass(frozen=True)
class Finding:
    pattern_id: str
    matched: str
    why: str


def lint_prompt(text: str) -> List[Finding]:
    """All known leading patterns present in ``text`` (empty list = none found)."""
    out: List[Finding] = []
    for pid, rx, why in LEADING_PATTERNS:
        m = rx.search(text or "")
        if m:
            out.append(Finding(pid, m.group(0), why))
    return out


# ---------------------------------------------------------------------------------------------
# Counterfactual variants
# ---------------------------------------------------------------------------------------------
FRAMING_NEUTRAL = "neutral"
FRAMING_REVERSED = "reversed"


def framing_variants(question: str, note_text: str) -> Dict[str, str]:
    """The same question and the same reference material under two opposite framings.

    ``neutral`` introduces the material with a bare label; ``reversed`` introduces it as unchecked and possibly
    wrong. A real effect of the note should not flip sign between them; an effect that exists only under
    the favourable wording is a framing effect. (The harness runs both: ``WITH_NOTE`` and ``WITH_NOTE_SKEPTIC``.)
    """
    return {
        FRAMING_NEUTRAL: TEMPLATES[COND_WITH].format(question=question, note_text=note_text),
        FRAMING_REVERSED: TEMPLATES[COND_SKEPTIC].format(question=question, note_text=note_text),
    }


def reverse_polarity(statement_true: str, statement_false: str) -> Dict[str, str]:
    """Two yes/no probes of the same fact with opposite correct answers ("X holds" / "X does not hold").

    A model that says "yes" to both is agreeing with the wording, not using the fact. The caller supplies both
    statements so no automatic negation can change the meaning.
    """
    if not statement_true.strip() or not statement_false.strip() or statement_true.strip() == statement_false.strip():
        raise ValueError("give two different non-empty statements")
    return {
        "affirmative": f"Is the following statement correct? {statement_true.strip()} Answer yes or no, then explain briefly.",
        "reversed": f"Is the following statement correct? {statement_false.strip()} Answer yes or no, then explain briefly.",
    }


# ---------------------------------------------------------------------------------------------
# The register
# ---------------------------------------------------------------------------------------------
VERDICT_NEUTRAL = "NEUTRAL"
VERDICT_LEADING_FIXED = "LEADING - REWRITTEN"
VERDICT_BY_DESIGN = "CUE BY DESIGN - FROZEN"
VERDICT_ADJACENT = "NEUTRAL - OUTSIDE THE TRACK"


@dataclass(frozen=True)
class RegisterEntry:
    entry_id: str
    where: str
    kind: str
    text: str
    verdict: str
    action: str
    findings: Tuple[str, ...] = ()


#: Flags the frozen H1 queries are known to carry (query id -> pattern ids). All are L10 path cues of the
#: multi-hop family, which is the cue under test in H1. Any other flag on any H1 query fails the test.
FROZEN_QUERY_EXEMPTIONS: Dict[str, Tuple[str, ...]] = {
    "H1-HOP-001": ("L10-path-cue",), "H1-HOP-002": ("L10-path-cue",), "H1-HOP-003": ("L10-path-cue",),
    "H1-HOP-004": ("L10-path-cue",), "H1-HOP-005": ("L10-path-cue",), "H1-HOP-006": ("L10-path-cue",),
    "H1-HOP-007": ("L10-path-cue",), "H1-HOP-008": ("L10-path-cue",), "H1-HOP-009": ("L10-path-cue",),
    "H1-HOP-010": ("L10-path-cue",),
}


def lint_h1_queries(cases_path: Path) -> Dict[str, List[str]]:
    """Pattern ids found per H1 query id (only queries with at least one flag)."""
    cases = json.loads(Path(cases_path).read_text(encoding="utf-8"))["cases"]
    out: Dict[str, List[str]] = {}
    for c in cases:
        found = [f.pattern_id for f in lint_prompt(c["query"])]
        if found:
            out[c["id"]] = found
    return out


def h1_family_summary(cases_path: Path) -> Dict[str, Dict[str, Any]]:
    """Per family: number of queries, flagged queries, and queries that contain one of their own required facts."""
    cases = json.loads(Path(cases_path).read_text(encoding="utf-8"))["cases"]
    fam: Dict[str, Dict[str, Any]] = {}
    for c in cases:
        f = fam.setdefault(c["family"], {"queries": 0, "flagged": 0, "contains_required_fact": 0})
        f["queries"] += 1
        f["flagged"] += bool(lint_prompt(c["query"]))
        q = c["query"].lower()
        f["contains_required_fact"] += any(rf.lower() in q for rf in c.get("required_facts", []))
    return fam


def audit_register(repo: Path) -> List[RegisterEntry]:
    """Every task prompt or template of the harness, with verdict and action, read from the live code."""
    from . import book_to_memory_facade as facade
    from . import book_to_memory_retrieval as retrieval

    entries: List[RegisterEntry] = []

    def add(entry_id: str, where: str, kind: str, text: str, verdict: str, action: str) -> None:
        entries.append(RegisterEntry(entry_id, where, kind, text, verdict, action,
                                     tuple(f.pattern_id for f in lint_prompt(text))))

    add("P-01", "book_to_memory_facade.py DEFAULT_TASK_DESCRIPTION", "default usage-test task",
        facade.DEFAULT_TASK_DESCRIPTION, VERDICT_LEADING_FIXED,
        'Was "Real-world application test requiring candidate note reasoning and factual constraints.": it presupposed '
        "that the answer uses the candidate note and told the model so, in the WITHOUT condition as well. Rewritten "
        "neutrally; the same text is used for both conditions.")
    add("P-02", "book_to_memory_retrieval.py CONTEXT_HEADER", "retrieved-context wrapper",
        "\n".join(retrieval.CONTEXT_HEADER_LINES), VERDICT_LEADING_FIXED,
        'Line 2 called the notes "passive reference evidence": "evidence" presupposes the notes support something. '
        'Reworded to "reference material"; the safety markers and the do-not-execute rule are unchanged.')
    for cond, label in ((COND_WITHOUT, "B03 baseline prompt"), (COND_WITH, "B03 neutral with-note prompt"),
                        (COND_SKEPTIC, "B03 reversed-framing prompt")):
        n = {COND_WITHOUT: "P-03", COND_WITH: "P-04", COND_SKEPTIC: "P-05"}[cond]
        add(n, f"book_to_memory_real_ablation.py TEMPLATES[{cond}]", label, TEMPLATES[cond], VERDICT_NEUTRAL,
            "New in this PR. The neutral and the reversed framing are both run (conditions WITH_NOTE and WITH_NOTE_SKEPTIC).")
    add("P-06", "book_to_memory_real_ablation.py QUESTION_TEMPLATE", "B03 task question", QUESTION_TEMPLATE, VERDICT_NEUTRAL,
        "New in this PR. Asks for an explanation of a term; names no note, no condition and no expected content.")

    h1 = repo / "08_RESEARCH" / "BOOK_TO_MEMORY" / "h1_cases.json"
    fam = h1_family_summary(h1)
    fam_flags: Dict[str, set] = {}
    for c in json.loads(h1.read_text(encoding="utf-8"))["cases"]:
        fam_flags.setdefault(c["family"], set()).update(f.pattern_id for f in lint_prompt(c["query"]))
    notes = {
        "direct_lexical": "queries repeat distinctive words of the target note: the lexical cue is the control condition",
        "paraphrase": "paraphrases the target; no target term required",
        "indirect_cue": "describes the target without its name; this is the cue under test",
        "entity_context": "names an entity or context of the target; a cue, not the answer",
        "multi_hop_associative": "names the starting node and an intermediate node of the path; the cue under test",
        "conflict": "asks which of two notes governs; no answer given",
        "distractor": "built to share words with a distractor; leading toward the distractor by design",
    }
    for i, (family, info) in enumerate(sorted(fam.items()), 7):
        verdict = VERDICT_BY_DESIGN
        entries.append(RegisterEntry(
            f"P-{i:02d}", f"h1_cases.json family {family}", "frozen benchmark queries",
            f"{info['queries']} queries; {info['flagged']} flagged; {info['contains_required_fact']} contain one of their own required facts",
            verdict,
            f"Not rewritten: the case set is frozen and its hash is bound into run_h1_associative_experiment.py. {notes[family]}.",
            tuple(sorted(fam_flags.get(family, ()))),
        ))
    n_next = 7 + len(fam)
    entries.append(RegisterEntry(
        f"P-{n_next:02d}", "07_EVALUATION/retrieval_benchmark_v3/LABELING_PROMPT.md", "labelling prompt (adjacent)",
        "instructions to an independent labeller", VERDICT_ADJACENT,
        "Read, not modified: it asks for paraphrased questions, forbids using system results and fixes the format. Outside the track.", ()))
    entries.append(RegisterEntry(
        f"P-{n_next + 1:02d}", "07_EVALUATION/golden_memory_effectiveness_v1/golden_tasks.json", "coding task prompts (adjacent)",
        "10 imperative task statements", VERDICT_ADJACENT,
        "Read, not modified: plain specifications with a verification command; no hint, no reliance wording. Outside the track.", ()))
    return entries


def render_audit_markdown(entries: Sequence[RegisterEntry]) -> str:
    lines = ["| id | where | kind | verdict | lint flags | action |", "|---|---|---|---|---|---|"]
    for e in entries:
        flags = ", ".join(e.findings) or "none"
        lines.append(f"| {e.entry_id} | `{e.where}` | {e.kind} | {e.verdict} | {flags} | {e.action} |")
    return "\n".join(lines) + "\n"
