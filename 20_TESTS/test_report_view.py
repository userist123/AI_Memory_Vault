import json

from cognitive_core.report_view import render_markdown, render_report_file


def _sample_report():
    return {
        "generated_at": "2026-08-25T00:00:00+00:00",
        "dormant_candidates": [{"id": "old-note", "age_days": 90.0, "activation": -2.1}],
        "stale_review_candidates": [{"id": "review-note", "age_days": 20.0}],
        "conflict_pairs": [{"note_a": "a", "note_b": "b", "overlap": 0.5, "severity": "contradiction"}],
        "stats": {"total_notes": 3, "active_notes": 1, "review_notes": 1,
                  "dormant_candidates": 1, "stale_review_candidates": 1, "conflict_pairs": 1},
    }


def test_render_markdown_includes_all_sections():
    markdown = render_markdown(_sample_report())
    assert "[[old-note]]" in markdown
    assert "[[review-note]]" in markdown
    assert "[[a]]" in markdown and "[[b]]" in markdown
    assert "Total notes: 3" in markdown
    assert "advisory only" in markdown


def test_render_markdown_handles_empty_sections():
    empty_report = {"generated_at": "now", "stats": {}}
    markdown = render_markdown(empty_report)
    assert markdown.count("_None._") == 3


def test_render_report_file_writes_output(tmp_path):
    report_path = tmp_path / "report.json"
    report_path.write_text(json.dumps(_sample_report()), encoding="utf-8")
    output_path = tmp_path / "out" / "Report.md"
    result = render_report_file(report_path, output_path)
    assert result.exists()
    assert "[[old-note]]" in result.read_text(encoding="utf-8")
