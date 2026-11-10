"""Blind-rating packet generator (PR #209 finding B05).

Turns answers produced under known conditions (with note / without note / ...) into a packet a rater
can score without knowing the condition: items are shuffled with a seed, each gets an opaque id,
the condition and the trial id appear nowhere in what the rater receives, and the mapping back to the
trials is written to a separate **unblinding key** that is never part of the packet.

``assert_blind`` is the guard: it fails if any item text carries a trial id, a condition label or a
condition-revealing marker of the packet itself. Answers that *mention* the reference material
("according to the reference material ...") cannot be edited out without altering the answer; they
are listed under ``unblinding_risk`` in the key and counted in the manifest, and the rater
instructions say that such a phrase is not to be used as evidence of the condition.
"""
from __future__ import annotations

import hashlib
import json
import random
import re
from pathlib import Path
from typing import Any, Dict, Iterable, List, Mapping, Optional, Sequence

PACKET_SCHEMA = "b2m-blind-packet/1"
KEY_SCHEMA = "b2m-unblinding-key/1"

#: Wording in an answer that may reveal that the model had reference material.
_RISK_PATTERNS = [
    re.compile(r"\b(reference|provided|given|supplied)\s+(material|note|text|passage|context)\b", re.I),
    re.compile(r"\bthe\s+(note|material)\s+(says|states|describes|defines|mentions)\b", re.I),
    re.compile(r"\baccording\s+to\s+the\s+(note|material|reference|passage)\b", re.I),
    re.compile(r"\b(unchecked|unverified|machine-extracted)\b", re.I),
    re.compile(r"\bwithout\s+(a|the|any)\s+(note|reference|material)\b", re.I),
]
#: Strings that must never appear in what a rater receives.
_FORBIDDEN_IN_ITEMS = re.compile(r"WITH_NOTE|WITHOUT_NOTE|WITH_DECOY_NOTE|WITH_NOTE_SKEPTIC|\bB03-T\d+")

RUBRIC_TEXT = """\
Score each answer on the five dimensions of the Policy-02 usage-test rubric, 0 to 2 each (total 0 to 10):

1. corectitudine (correctness): 0 wrong or contradicts the reference, 1 partly right, 2 right.
2. completitudine (completeness): 0 misses the main point, 1 covers part of it, 2 covers it.
3. fara_ghicit (no guessing): 0 vague or invented specifics, 1 some unsupported specifics, 2 nothing unsupported.
4. fara_surse_externe (no external sources): 0 cites outside sources or links, 2 none (1 is not used).
5. reproductibilitate (reproducibility): 0 an unrelated reader could not follow it, 1 partly, 2 clear and self-contained.

The label you record is the TOTAL (0-10). The rationale must say what drove the score.
"""


class BlindPacketError(ValueError):
    """The packet cannot be built or fails the blindness check."""


def _opaque_id(salt: str, trial_id: str) -> str:
    return "ITM-" + hashlib.sha256(f"{salt}|{trial_id}".encode("utf-8")).hexdigest()[:10]


def unblinding_risk(answer: str) -> List[str]:
    """Patterns in an answer that may reveal the condition (an empty list means none found)."""
    return [p.pattern for p in _RISK_PATTERNS if p.search(answer or "")]


def assert_blind(items: Sequence[Mapping[str, Any]]) -> None:
    """Fail if any rater-visible item field carries a trial id, a condition label or a packet marker."""
    for it in items:
        text = json.dumps(it, ensure_ascii=False)
        m = _FORBIDDEN_IN_ITEMS.search(text)
        if m:
            raise BlindPacketError(f"item {it.get('item_id')} leaks {m.group(0)!r}")
        if set(it) - {"item_id", "question", "answer", "reference"}:
            raise BlindPacketError(f"item {it.get('item_id')} carries unexpected fields {sorted(set(it))}")


def build_blind_packet(trials: Sequence[Mapping[str, Any]], out_dir: Path, key_path: Path, *, n_raters: int = 2,
                       seed: int = 0, include_reference: bool = True) -> Dict[str, Any]:
    """Write the rater packet to ``out_dir`` and the unblinding key to ``key_path``.

    ``trials`` are dicts with ``trial_id``, ``condition``, ``question``, ``answer`` and optionally ``task_id`` and
    ``reference`` (what the rater scores against). Trials with an empty answer are not packaged and are
    listed in the manifest as missing: a rater is never asked to score nothing.
    """
    if n_raters < 2:
        raise BlindPacketError("a blind multi-rater packet needs at least 2 raters")
    out_dir = Path(out_dir)
    key_path = Path(key_path)
    try:
        key_path.resolve().relative_to(out_dir.resolve())
    except ValueError:
        pass
    else:
        raise BlindPacketError("the unblinding key must be written outside the rater packet directory")
    ids = [t["trial_id"] for t in trials]
    if len(set(ids)) != len(ids):
        raise BlindPacketError("duplicate trial_id in the input")
    salt = hashlib.sha256(f"b2m-blind|{seed}".encode("utf-8")).hexdigest()[:16]
    usable, missing = [], []
    for t in trials:
        (usable if str(t.get("answer") or "").strip() else missing).append(t)
    order = list(usable)
    random.Random(seed).shuffle(order)

    items: List[Dict[str, Any]] = []
    key: Dict[str, Any] = {}
    risky = 0
    for t in order:
        item_id = _opaque_id(salt, t["trial_id"])
        item = {"item_id": item_id, "question": t["question"], "answer": t["answer"]}
        if include_reference and t.get("reference"):
            item["reference"] = t["reference"]
        items.append(item)
        risk = unblinding_risk(t["answer"])
        risky += bool(risk)
        key[item_id] = {"trial_id": t["trial_id"], "task_id": t.get("task_id"), "condition": t["condition"],
                        "unblinding_risk": risk}
    if len({i["item_id"] for i in items}) != len(items):
        raise BlindPacketError("opaque id collision; change the seed")
    assert_blind(items)

    out_dir.mkdir(parents=True, exist_ok=True)
    (out_dir / "items.json").write_text(
        json.dumps({"schema": PACKET_SCHEMA, "items": items}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (out_dir / "RATER_INSTRUCTIONS.md").write_text(_instructions(len(items), n_raters), encoding="utf-8")
    templates = out_dir / "rating_templates"
    templates.mkdir(exist_ok=True)
    for k in range(1, n_raters + 1):
        lines = [json.dumps({
            "schema_version": "b2m-rating-record/1", "rater_id": f"RATER-{k}", "rater_kind": "<human|model>",
            "model": "<model id, or null for a human>", "blind": True, "item_id": it["item_id"],
            "label": None, "rationale": "",
        }, ensure_ascii=False) for it in items]
        (templates / f"RATER-{k}.template.jsonl").write_text("\n".join(lines) + "\n", encoding="utf-8")
    manifest = {
        "schema": PACKET_SCHEMA, "seed": seed, "items": len(items), "raters": n_raters,
        "missing_answers": [t["trial_id"] for t in missing],
        "items_with_unblinding_risk": risky,
        "items_sha256": hashlib.sha256((out_dir / "items.json").read_bytes()).hexdigest(),
    }
    (out_dir / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    key_path.parent.mkdir(parents=True, exist_ok=True)
    key_path.write_text(json.dumps({"schema": KEY_SCHEMA, "seed": seed, "packet_items_sha256": manifest["items_sha256"],
                                    "key": key}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return manifest


def _instructions(n_items: int, n_raters: int) -> str:
    return (
        "# Rater instructions (blind)\n\n"
        f"You are one of {n_raters} independent raters of {n_items} items. Rate alone: do not look at another rater's\n"
        "file, do not discuss items, do not try to work out how an answer was produced.\n\n"
        "Each item in `items.json` has a question, an answer and (when present) a reference to score against. You are not\n"
        "told, and must not guess, which setup produced an answer; an answer that mentions reference material is not\n"
        "evidence of anything for your score.\n\n"
        + RUBRIC_TEXT
        + "\nFill your own file in `rating_templates/` (copy it to `RATER-k.jsonl`): set `rater_kind` (`human` or `model`), `model`\n"
          "(the model id, or `null` for a human), `label` (the 0-10 total) and `rationale` for every line. Leave `blind` true only\n"
          "if you did not know the setup of any item; otherwise set it false and say why in the rationale.\n"
    )


def scores_by_trial(aggregate: Mapping[str, Any], key_file: Path) -> Dict[str, float]:
    """Map an :func:`aggregate_ratings` result back to ``trial_id -> mean rubric total`` through the key."""
    if aggregate.get("status") != "OK":
        raise BlindPacketError(f"ratings are not usable: {aggregate.get('status')} {aggregate.get('reason', '')}")
    key = json.loads(Path(key_file).read_text(encoding="utf-8"))["key"]
    out: Dict[str, float] = {}
    for item_id, entry in aggregate["items"].items():
        if item_id not in key:
            raise BlindPacketError(f"rated item {item_id} is not in the key")
        out[key[item_id]["trial_id"]] = float(entry["mean"])
    return out


def items_from_b03_packet(packet_dir: Path, answers_dir: Optional[Path] = None) -> List[Dict[str, Any]]:
    """Read the B03 task packet plus its answers into the ``trials`` form :func:`build_blind_packet` takes."""
    packet_dir = Path(packet_dir)
    answers_dir = Path(answers_dir) if answers_dir else packet_dir / "answers"
    trials = json.loads((packet_dir / "trials.json").read_text(encoding="utf-8"))["trials"]
    tasks = {t["task_id"]: t for t in json.loads((packet_dir / "scoring" / "tasks.json").read_text(encoding="utf-8"))["tasks"]}
    out = []
    for t in trials:
        f = answers_dir / Path(t["answer_path"]).name
        answer = f.read_text(encoding="utf-8") if f.exists() else ""
        task = tasks[t["task_id"]]
        out.append({
            "trial_id": t["trial_id"], "task_id": t["task_id"], "condition": t["condition"],
            "question": task["question"], "answer": answer,
            "reference": f"Source passage: {task['source_passage']}\nNote definition: {task['note_definition']}",
        })
    return out
