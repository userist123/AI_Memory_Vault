from pathlib import Path


def test_claude_restores_human_memory_and_skill_gate():
    text = (Path(__file__).resolve().parents[1] / 'CLAUDE.md').read_text(encoding='utf-8')
    assert '## Human gate: memory and skill promotion' in text
    assert '`memory_propose` creates a candidate note: lifecycle `REVIEW`, verification `unverified`' in text
    assert 'only the owner attests it (`attest()`)' in text
    assert 'The router and dispatcher MUST NOT promote, attest' in text
    assert 'python 30_SCRIPTS/skills/skill_ingestion.py scan' in text
    assert 'python 30_SCRIPTS/skills/skill_ingestion.py match' in text
    assert 'promote --skill <skill-id> --verified' in text
    assert 'router and dispatcher MUST NOT execute' in text
