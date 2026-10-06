from __future__ import annotations

import json

from security.loganalyzer_ui_sink import LogAnalyzerUISink
from security.security_event import SecurityEvent


def test_sink_writes_only_security_event_contract(tmp_path):
    path = tmp_path / "security-events.jsonl"
    sink = LogAnalyzerUISink(path)

    sink(
        SecurityEvent(
            event_type="TOOL_BLOCKED",
            source="runtime_adapter",
            actor="agent",
            correlation_id="corr-1",
            trust_state="BLOCKED",
            tool_name="demo",
            tool_allowed=False,
            decision_reason="policy_denied",
            metadata={
                "repository": "owner/repo",
                "evidence_ref": "e-1",
                "raw_prompt": "MUST NOT APPEAR",
            },
        )
    )

    record = json.loads(path.read_text(encoding="utf-8"))
    assert record["schema_version"] == 1
    assert record["event_type"] == "TOOL_BLOCKED"
    assert record["correlation_id"] == "corr-1"
    assert record["tool"]["allowed"] is False
    assert "raw_prompt" not in record["metadata"]
