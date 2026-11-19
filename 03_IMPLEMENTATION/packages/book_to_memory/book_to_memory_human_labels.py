"""Human-attested ground truth for the Book-to-Memory metrics (PR #209 finding B06).

The track's metrics were built on a corpus whose "truth" is machine-made: the H1 gold ids were chosen by a
script from a labelling corpus, and the 51 candidate notes were extracted by a model. This module

* builds the **labelling packet** of items that need a human decision (rows for a CSV the owner fills in);
* ingests the **completed labels** and calibrates the metrics against them: how often the H1 gold note is
  judged relevant (and what the held-out recall becomes if the refuted gold is dropped), and how often a
  candidate note's definition is faithful to its own quoted passage, by the confidence the note claims;
* never fills a label itself. With no labels the report says ``WAITING_ON_OWNER_LABELS``; with fewer than
  ``MIN_LABELS`` for a metric it says ``INSUFFICIENT_LABELS`` for that metric and reports no rate.
"""
from __future__ import annotations

import csv
import io
import json
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any, Dict, List, Mapping, Optional, Sequence

from .book_to_memory_paired_stats import wilson_interval
from .book_to_memory_raters import RatingError, cohens_kappa, krippendorff_alpha

TYPE_H1 = "h1_gold_relevance"
TYPE_NOTE = "candidate_note_faithfulness"
ALLOWED = {
    TYPE_H1: ("yes", "partial", "no"),
    TYPE_NOTE: ("faithful", "partial", "unfaithful", "not_a_term"),
}
QUESTIONS = {
    TYPE_H1: "Reading only the note text below, does this note answer the query? yes / partial / no. "
             "In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).",
    TYPE_NOTE: "Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? "
               "faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).",
}
COLUMNS = ["item_id", "item_type", "priority", "context", "evidence_title", "evidence_text", "question",
           "allowed_values", "label", "other_relevant_ids", "comment", "labeler_id"]
FILL_COLUMNS = ("label", "other_relevant_ids", "comment", "labeler_id")

#: Fewest labelled items of a type before any rate is reported for it.
MIN_LABELS = 20
STATUS_WAITING = "WAITING_ON_OWNER_LABELS"
STATUS_INSUFFICIENT = "INSUFFICIENT_LABELS"
STATUS_CALIBRATED = "CALIBRATED"


class LabelError(ValueError):
    """A labels file is malformed or contradicts the packet."""


# ---------------------------------------------------------------------------------------------
# Building the packet
# ---------------------------------------------------------------------------------------------
def build_rows(h1_cases: Mapping[str, Any], corpus: Mapping[str, Any], task_notes: Sequence[Mapping[str, Any]],
               note_confidence: Mapping[str, str]) -> List[Dict[str, str]]:
    """All rows needing a human decision. Priority 1 = the held-out H1 cases and every candidate note."""
    notes = {n["id"]: n for n in corpus["notes"]}
    rows: List[Dict[str, str]] = []
    for case in h1_cases["cases"]:
        for gold in case["gold_relevant_notes"]:
            n = notes.get(gold)
            if n is None:
                raise LabelError(f"gold note {gold} of {case['id']} is not in the labelling corpus")
            rows.append({
                "item_id": f"H1G|{case['id']}|{gold}", "item_type": TYPE_H1,
                "priority": "1" if case["split"] == "held_out" else "2",
                "context": f"[{case['family']}, {case['split']}] {case['query']}",
                "evidence_title": f"{n['title']} ({gold})", "evidence_text": " ".join(n.get("excerpt", "").split()),
                "question": QUESTIONS[TYPE_H1], "allowed_values": "|".join(ALLOWED[TYPE_H1]),
            })
    for t in task_notes:
        rows.append({
            "item_id": f"PN|{t['note_id']}", "item_type": TYPE_NOTE, "priority": "1",
            "context": f"term: {t['term']}   (claimed confidence: {note_confidence.get(t['note_id'], 'unknown')})",
            "evidence_title": f"{t['term']} ({t['note_id']})",
            "evidence_text": f"DEFINITION: {t['note_definition']}\nSOURCE PASSAGE: {t['source_passage']}",
            "question": QUESTIONS[TYPE_NOTE], "allowed_values": "|".join(ALLOWED[TYPE_NOTE]),
        })
    for r in rows:
        for c in FILL_COLUMNS:
            r[c] = ""
    return rows


def rows_to_csv(rows: Sequence[Mapping[str, str]]) -> str:
    buf = io.StringIO()
    w = csv.DictWriter(buf, fieldnames=COLUMNS, lineterminator="\n")
    w.writeheader()
    w.writerows(rows)
    return buf.getvalue()


def rows_to_markdown(rows: Sequence[Mapping[str, str]]) -> str:
    out = ["# B06 labelling items (read-only view; enter labels in the CSV)", ""]
    for r in rows:
        out += [f"## {r['item_id']}  (priority {r['priority']}, {r['item_type']})", "",
                f"* **Context**: {r['context']}", f"* **Evidence**: {r['evidence_title']}", "",
                "> " + r["evidence_text"].replace("\n", "\n> "), "",
                f"* **Question**: {r['question']}", f"* **Allowed labels**: `{r['allowed_values']}`", ""]
    return "\n".join(out)


# ---------------------------------------------------------------------------------------------
# Ingesting labels
# ---------------------------------------------------------------------------------------------
def read_labels(text: str, packet_ids: Mapping[str, str]) -> Dict[str, Any]:
    """Parse a completed CSV. ``packet_ids`` maps item_id -> item_type of the packet it came from.

    Blank ``label`` cells are unlabelled and are skipped. An unknown item id, an unknown item type, a label outside the
    allowed values for the item's type, a missing labeler id or a repeated item id is an error.
    """
    reader = csv.DictReader(io.StringIO(text))
    missing = [c for c in COLUMNS if c not in (reader.fieldnames or [])]
    if missing:
        raise LabelError(f"missing columns: {missing}")
    labels: Dict[str, Dict[str, str]] = {}
    seen_ids: set = set()
    unlabelled = 0
    for n, row in enumerate(reader, 2):
        item_id, label = row["item_id"].strip(), row["label"].strip().lower()
        if item_id not in packet_ids:
            raise LabelError(f"line {n}: unknown item_id {item_id!r}")
        if row["item_type"] != packet_ids[item_id]:
            raise LabelError(f"line {n}: item_type changed for {item_id}")
        if item_id in seen_ids:
            raise LabelError(f"line {n}: item {item_id} appears twice")
        seen_ids.add(item_id)
        if not label:
            unlabelled += 1
            continue
        if label not in ALLOWED[row["item_type"]]:
            raise LabelError(f"line {n}: label {label!r} is not one of {ALLOWED[row['item_type']]} for {item_id}")
        labeler = row["labeler_id"].strip()
        if not labeler:
            raise LabelError(f"line {n}: labeler_id is required for a labelled row")
        labels[item_id] = {"label": label, "labeler_id": labeler, "other_relevant_ids": row["other_relevant_ids"].strip(),
                           "comment": row["comment"].strip()}
    return {"labels": labels, "unlabelled": unlabelled, "rows": len(seen_ids)}


def _rate(k: int, n: int) -> Dict[str, Any]:
    lo, hi = wilson_interval(k, n)
    return {"k": k, "n": n, "rate": round(k / n, 4), "wilson95": [round(lo, 4), round(hi, 4)]}


def calibrate(packet_rows: Sequence[Mapping[str, str]], label_sets: Sequence[Mapping[str, Any]],
              baseline_cases: Optional[Sequence[Mapping[str, Any]]] = None,
              note_confidence: Optional[Mapping[str, str]] = None, min_labels: int = MIN_LABELS) -> Dict[str, Any]:
    """Calibrate the metrics against the human labels. ``label_sets`` is one :func:`read_labels` result per labeler."""
    packet = {r["item_id"]: r for r in packet_rows}
    per_labeler: Dict[str, Dict[str, str]] = defaultdict(dict)
    for ls in label_sets:
        for item_id, rec in ls["labels"].items():
            if rec["labeler_id"] in per_labeler and item_id in per_labeler[rec["labeler_id"]]:
                raise LabelError(f"labeler {rec['labeler_id']} labelled {item_id} twice")
            per_labeler[rec["labeler_id"]][item_id] = rec["label"]
    report: Dict[str, Any] = {
        "labelers": sorted(per_labeler), "items_in_packet": len(packet),
        "labelled_items": len({i for d in per_labeler.values() for i in d}), "metrics": {},
    }
    if not per_labeler:
        report["status"] = STATUS_WAITING
        return report

    # Consensus label per item: unanimous label if all labelers agree, "disputed" otherwise (never silently majority-voted).
    by_item: Dict[str, List[str]] = defaultdict(list)
    for d in per_labeler.values():
        for item_id, lab in d.items():
            by_item[item_id].append(lab)
    consensus = {i: (v[0] if len(set(v)) == 1 else "disputed") for i, v in by_item.items()}
    report["disputed_items"] = sorted(i for i, c in consensus.items() if c == "disputed")

    # Agreement between labelers.
    if len(per_labeler) >= 2:
        ids = sorted(by_item)
        units = {i: by_item[i] for i in ids if len(by_item[i]) >= 2}
        agreement: Dict[str, Any] = {"items_with_2plus_labelers": len(units)}
        if units:
            try:
                agreement["krippendorff_alpha_nominal"] = krippendorff_alpha(units, "nominal")
            except RatingError as exc:
                agreement["error"] = str(exc)
            if len(per_labeler) == 2:
                a, b = sorted(per_labeler)
                common = [i for i in ids if i in per_labeler[a] and i in per_labeler[b]]
                if common:
                    agreement["cohens_kappa"] = cohens_kappa([per_labeler[a][i] for i in common], [per_labeler[b][i] for i in common])
        report["agreement"] = agreement

    # H1 gold relevance.
    h1_items = {i: c for i, c in consensus.items() if packet[i]["item_type"] == TYPE_H1 and c != "disputed"}
    m1: Dict[str, Any] = {"labelled": len(h1_items), "required": min_labels}
    if len(h1_items) < min_labels:
        m1["status"] = STATUS_INSUFFICIENT
    else:
        counts = Counter(h1_items.values())
        m1.update({"status": STATUS_CALIBRATED, "gold_judged_relevant": _rate(counts["yes"], len(h1_items)),
                   "gold_judged_relevant_or_partial": _rate(counts["yes"] + counts["partial"], len(h1_items)),
                   "gold_judged_not_relevant": _rate(counts["no"], len(h1_items))})
        if baseline_cases:
            m1["baseline_recall_at_10"] = _adjusted_recall(baseline_cases, consensus)
    report["metrics"]["h1_gold"] = m1

    # Candidate-note faithfulness, by claimed confidence.
    note_items = {i: c for i, c in consensus.items() if packet[i]["item_type"] == TYPE_NOTE and c != "disputed"}
    m2: Dict[str, Any] = {"labelled": len(note_items), "required": min_labels}
    if len(note_items) < min_labels:
        m2["status"] = STATUS_INSUFFICIENT
    else:
        counts = Counter(note_items.values())
        m2.update({"status": STATUS_CALIBRATED, "faithful": _rate(counts["faithful"], len(note_items)),
                   "faithful_or_partial": _rate(counts["faithful"] + counts["partial"], len(note_items)),
                   "not_a_term": _rate(counts["not_a_term"], len(note_items))})
        by_conf: Dict[str, List[str]] = defaultdict(list)
        for i, lab in note_items.items():
            by_conf[(note_confidence or {}).get(i.split("|", 1)[1], "unknown")].append(lab)
        m2["by_claimed_confidence"] = {conf: _rate(sum(l == "faithful" for l in labs), len(labs)) for conf, labs in sorted(by_conf.items())}
    report["metrics"]["candidate_notes"] = m2

    statuses = {m["status"] for m in report["metrics"].values()}
    report["status"] = STATUS_CALIBRATED if statuses == {STATUS_CALIBRATED} else (
        STATUS_INSUFFICIENT if STATUS_INSUFFICIENT in statuses else STATUS_WAITING)
    return report


def _adjusted_recall(cases: Sequence[Mapping[str, Any]], consensus: Mapping[str, str]) -> Dict[str, Any]:
    """Held-out baseline recall@10 as published, and over the cases whose gold a human confirmed (every labelled gold row is 'yes')."""
    held = [c for c in cases if c.get("split") == "held_out"]
    confirmed, refuted = [], []
    for c in held:
        rows = [consensus.get(f"H1G|{c['id']}|{g}") for g in c["gold_relevant_notes"]]
        if rows and all(r == "yes" for r in rows):
            confirmed.append(c)
        elif any(r in ("no", "partial") for r in rows):
            refuted.append(c)

    def rec(cs: Sequence[Mapping[str, Any]]) -> Optional[Dict[str, Any]]:
        return _rate(sum(1 for c in cs if c.get("hits_at_10")), len(cs)) if cs else None

    return {"as_published_held_out": rec(held), "human_confirmed_gold_only": rec(confirmed),
            "cases_with_gold_not_confirmed": len(refuted),
            "held_out_cases_without_a_complete_human_label": len(held) - len(confirmed) - len(refuted)}


def render_report_markdown(report: Mapping[str, Any]) -> str:
    lines = ["# B06 calibration report (generated)", "", f"**Status**: `{report['status']}`", "",
             f"* labelers: {', '.join(report['labelers']) or 'none'}",
             f"* items in the packet: {report['items_in_packet']}; labelled: {report['labelled_items']}"]
    if report["status"] == STATUS_WAITING:
        lines += ["", "No human label has been supplied. No metric was calibrated and none is claimed."]
        return "\n".join(lines) + "\n"
    if report.get("disputed_items"):
        lines.append(f"* disputed items (labelers disagree, excluded from rates): {len(report['disputed_items'])}")
    if "agreement" in report:
        lines.append(f"* labeler agreement: `{json.dumps(report['agreement'])}`")
    for name, m in report["metrics"].items():
        lines += ["", f"## {name}", "", f"* status: `{m['status']}` ({m['labelled']} labelled, {m['required']} required)"]
        for k, v in m.items():
            if isinstance(v, dict):
                lines.append(f"* {k}: `{json.dumps(v)}`")
    return "\n".join(lines) + "\n"
