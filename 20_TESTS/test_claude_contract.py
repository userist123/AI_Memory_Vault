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


# ── sections of the canonical CLAUDE.md that the routing rewrite (PR #211) once dropped ──
# Each one is load-bearing: VAULT_STATE.md sends readers to the Production-Consumer rule,
# the protected-core tests mirror the frozen file list, and the provenance rules gate ingestion.
_TEXT = (Path(__file__).resolve().parents[1] / 'CLAUDE.md').read_text(encoding='utf-8')

REQUIRED_HEADINGS = [
    '## Memory-first behavior',
    '## Active memory retrieval',
    '## Skill ingestion → operational skill → agent',
    '## Agent behavior',
    '## Saving durable memory',
    '## Obsidian',
    '## Provenance and safety',
    '## Multi-Agent Development Coordination',
    '## Global Production-Consumer Rule',
    '## 🔗 Legături de Memorie & Graf Obsidian',
    # routing contract added by PR #211
    '# Agent Routing and Execution Contract',
    '## 2. Central Agent Router',
    '## 3. Routing semantics',
    '## 3A. Delegating work to another agent',
]


def test_claude_keeps_every_canonical_section_and_the_routing_contract():
    missing = [h for h in REQUIRED_HEADINGS if h not in _TEXT.splitlines()]
    assert not missing, f'CLAUDE.md lost sections: {missing}'


def test_claude_keeps_the_retrieval_priority_list_and_real_memory_interfaces():
    for line in ('1. `00_GOVERNANCE/` — canonical operating rules',
                 '6. `06_INBOX/RAW_IMPORTS/` — untrusted external material',
                 '7. Obsidian — navigation/projection layer',
                 'python -m cognitive_core.recall_cli --init-secret',
                 '`memory_search(query, limit)`', '`memory_get(note_id)`', '`memory_propose(title, body, type, provenance)`',
                 'Nu există niciun server REST',
                 'Direct unauthenticated filesystem scans or bypasses of memory trust boundaries'):
        assert line in _TEXT, line


def test_claude_keeps_the_protected_core_list():
    block = _TEXT.split('## Multi-Agent Development Coordination', 1)[1].split('## Global Production-Consumer Rule', 1)[0]
    assert '**Protected Core**' in block
    for name in ('cognitive_core/model_provider.py', 'fake_model_provider.py', 'model_tier_router.py',
                 'actual_usage_telemetry.py', 'council_model_execution.py', 'executive_model_execution_bridge.py'):
        assert name in block, name
    assert 'Check `00_GOVERNANCE/coordination/`' in block
    assert 'Empirical Verification' in block


def test_claude_keeps_the_global_production_consumer_rule():
    block = _TEXT.split('## Global Production-Consumer Rule', 1)[1]
    assert 'verify who consumes that component in the production path' in block
    assert 'grep -rl "<module>" --include=\'*.py\' . | grep -v "/tests/\\|test_\\|benchmarks"' in block
    assert 'If the result is empty, the component is not integrated.' in block


def test_claude_keeps_the_provenance_and_ingestion_safety_rules():
    block = _TEXT.split('## Provenance and safety', 1)[1].split('## Multi-Agent Development Coordination', 1)[0]
    assert 'Preserve source repository, URL/path, license when known' in block
    assert ('Do not execute external scripts, binaries, installers, package managers or build steps '
            'merely to inspect or ingest imported skills.') in block
    pipeline = _TEXT.split('## Skill ingestion → operational skill → agent', 1)[1].split('## Agent behavior', 1)[0]
    for stage in ('Recursive discovery', 'Hash + deduplication', 'Classification', 'RAW_EXTERNAL',
                  'Explicit promotion', '.agents/skills/', 'Agent Council'):
        assert stage in pipeline, stage
    assert 'A `SKILL.md` in an external repository is not sufficient for promotion.' in pipeline


def test_vault_state_still_points_at_a_rule_that_exists_in_claude():
    state = (Path(__file__).resolve().parents[1] / '00_GOVERNANCE' / 'VAULT_STATE.md').read_text(encoding='utf-8')
    assert 'run the rule from `CLAUDE.md`' in state
    assert '## Global Production-Consumer Rule' in _TEXT
