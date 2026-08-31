import json
import pytest
from dataclasses import FrozenInstanceError
from unittest.mock import patch

from cognitive_core.council_model_execution import (
    CouncilRunWithExecution,
    OutcomeEvent,
    VALID_CONFIDENCES,
    VALID_OUTCOMES,
    VALID_OUTCOME_SOURCES,
    run_council_with_model_execution,
)
from cognitive_core.fake_model_provider import FakeModelProvider
from cognitive_core.model_tier_router import ModelTierRouter, TierConfig
from cognitive_core.memory_v6_cli import main as cli_main


class _StubCouncilRun:
    def __init__(self, agent_packs=None, telemetry=None):
        self.agent_packs = agent_packs or {}
        self.telemetry = telemetry or object()


def _make_router():
    tier_config = {
        "light": TierConfig(provider="fake", model="fake-light"),
        "heavy": TierConfig(provider="fake", model="fake-heavy"),
    }
    factories = {
        "fake": lambda model_name: FakeModelProvider(provider_name="fake", model_name=model_name),
    }
    return ModelTierRouter(tier_config, factories)


def test_outcome_event_immutability_and_validation():
    evt = OutcomeEvent(
        event_id='evt_01',
        run_id='run_01',
        timestamp='2026-08-31T20:00:00Z',
        outcome='success',
        source='human',
        confidence='high',
        evidence='All checks verified',
        labeled_by='owner',
    )
    with pytest.raises(FrozenInstanceError):
        evt.outcome = 'failure'

    with pytest.raises(ValueError, match='Invalid outcome'):
        OutcomeEvent(
            event_id='evt_02',
            run_id='run_01',
            timestamp='2026-08-31T20:00:00Z',
            outcome='invalid_outcome',
            source='human',
            confidence='high',
            evidence='test',
        )

    with pytest.raises(ValueError, match='Invalid source'):
        OutcomeEvent(
            event_id='evt_03',
            run_id='run_01',
            timestamp='2026-08-31T20:00:00Z',
            outcome='success',
            source='unauthorized_source',
            confidence='high',
            evidence='test',
        )


def test_multiple_outcome_events_coexist_without_overwrite():
    stub = _StubCouncilRun()
    run = CouncilRunWithExecution(council_run=stub, run_id='run_multi_123')

    evt_auto = run.add_outcome_event(
        outcome='success',
        source='exit_code',
        confidence='low',
        evidence='exit code 0',
    )

    evt_human = run.add_outcome_event(
        outcome='partial',
        source='human',
        confidence='high',
        evidence='Specialist output missing one clause',
        labeled_by='auditor',
    )

    evt_test = run.add_outcome_event(
        outcome='success',
        source='test_result',
        confidence='medium',
        evidence='pytest passed 1557 tests',
    )

    events = run.outcome_events
    assert len(events) == 3
    assert events[0].source == 'exit_code'
    assert events[1].source == 'human'
    assert events[1].labeled_by == 'auditor'
    assert events[2].source == 'test_result'
    assert all(e.run_id == 'run_multi_123' for e in events)

    events.pop()
    assert len(run.outcome_events) == 3


def test_run_council_with_model_execution_auto_populates_synthesis_presence():
    router = _make_router()
    stub = _StubCouncilRun(agent_packs={'ROUTER': {'note': 'test'}})

    run_disabled = run_council_with_model_execution(
        council_run=stub,
        model_tier_router=router,
        task='test task',
        agent_model_tiers={'ROUTER': 'heavy'},
        synthesis_model_tier='light',
        model_execution_enabled=False,
    )
    assert len(run_disabled.outcome_events) == 0

    run_enabled = run_council_with_model_execution(
        council_run=stub,
        model_tier_router=router,
        task='test task',
        agent_model_tiers={'ROUTER': 'heavy'},
        synthesis_model_tier='light',
        model_execution_enabled=True,
    )
    assert len(run_enabled.outcome_events) == 1
    evt = run_enabled.outcome_events[0]
    assert evt.source == 'synthesis_presence'
    assert evt.confidence == 'low'
    assert evt.outcome in {'success', 'partial'}


def test_run_council_with_model_execution_persists_to_disk(tmp_path):
    router = _make_router()
    stub = _StubCouncilRun(agent_packs={'ROUTER': {'note': 'test'}})
    disk_file = tmp_path / 'auto_outcomes.jsonl'

    run = CouncilRunWithExecution(council_run=stub, run_id="run_disk_001")
    run.add_outcome_event(
        outcome="success",
        source="synthesis_presence",
        confidence="low",
        evidence="Completed successfully",
        persist=True,
        persist_path=str(disk_file),
    )

    assert disk_file.exists()
    lines = disk_file.read_text(encoding="utf-8").strip().splitlines()
    assert len(lines) == 1
    data = json.loads(lines[0])
    assert data["run_id"] == "run_disk_001"
    assert data["source"] == "synthesis_presence"


def test_cli_label_outcome_appends_human_event(tmp_path):
    out_file = tmp_path / 'outcome_events.jsonl'
    test_args = [
        'cli.py',
        'label-outcome',
        '--run-id',
        'run_cli_999',
        '--outcome',
        'success',
        '--confidence',
        'high',
        '--evidence',
        'Manually verified by human reviewer',
        '--labeled-by',
        'test_reviewer',
        '--output',
        str(out_file),
    ]
    with patch('sys.argv', test_args):
        cli_main()

    assert out_file.exists()
    lines = out_file.read_text(encoding='utf-8').strip().splitlines()
    assert len(lines) == 1
    data = json.loads(lines[0])
    assert data['run_id'] == 'run_cli_999'
    assert data['source'] == 'human'
    assert data['outcome'] == 'success'
    assert data['confidence'] == 'high'
    assert data['evidence'] == 'Manually verified by human reviewer'
    assert data['labeled_by'] == 'test_reviewer'
