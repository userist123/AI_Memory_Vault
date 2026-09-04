"""Renders a SleepConsolidationReport JSON into a human-readable Obsidian note.

Read-only and advisory. Never modifies canonical notes; only writes a fresh
Markdown summary file for human navigation in Obsidian.
"""
from __future__ import annotations

from pathlib import Path
from typing import Any, Dict
import json


def render_markdown(report: Dict[str, Any]) -> str:
    stats = report.get("stats", {})
    lines = [
        "---",
        "id: sleep-consolidation-report",
        "type: report",
        "category: memory_maintenance",
        "lifecycle: ACTIVE",
        "verification: unverified",
        "confidence: medium",
        "provenance:",
        "  source_type: execution",
        "  source_ref: cognitive_core.sleep_consolidation",
        "---",
        "",
        f"# Sleep Consolidation Report \u2014 {report.get('generated_at', '')}",
        "",
        "## Summary",
        "",
        f"- Total notes: {stats.get('total_notes', 0)}",
        f"- Active notes: {stats.get('active_notes', 0)}",
        f"- Review notes: {stats.get('review_notes', 0)}",
        f"- Dormant candidates: {stats.get('dormant_candidates', 0)}",
        f"- Stale REVIEW candidates: {stats.get('stale_review_candidates', 0)}",
        f"- Conflict pairs: {stats.get('conflict_pairs', 0)}",
        "",
        "## Dormant Candidates",
        "",
    ]
    dormant = report.get("dormant_candidates", [])
    if dormant:
        for item in dormant:
            lines.append(f"- [[{item['id']}]] \u2014 {item['age_days']} days, activation {item['activation']}")
    else:
        lines.append("_None._")

    lines += ["", "## Stale REVIEW Candidates", ""]
    stale = report.get("stale_review_candidates", [])
    if stale:
        for item in stale:
            lines.append(f"- [[{item['id']}]] \u2014 {item['age_days']} days in REVIEW")
    else:
        lines.append("_None._")

    lines += ["", "## Conflict Pairs", ""]
    conflicts = report.get("conflict_pairs", [])
    if conflicts:
        for item in conflicts:
            lines.append(
                f"- [[{item['note_a']}]] \u2194 [[{item['note_b']}]] "
                f"({item['severity']}, overlap {item['overlap']})"
            )
    else:
        lines.append("_None._")

    lines += [
        "",
        "## Suggested Actions",
        "",
        "- Review dormant candidates for `archive()` or re-`attest()`.",
        "- Promote or reject stale REVIEW notes.",
        "- Reconcile conflict pairs via `supersede()` where appropriate.",
        "",
        "_This report is advisory only. No note was modified to produce it._",
        "",
    ]
    return "\n".join(lines)


def render_report_file(report_path, output_path) -> Path:
    report = json.loads(Path(report_path).read_text(encoding="utf-8"))
    markdown = render_markdown(report)
    target = Path(output_path)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(markdown, encoding="utf-8")
    return target
