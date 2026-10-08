"""B12: a blocker's severity cannot go down without an explicit owner attestation.

Two layers are covered:

* the library gate, `security.trust_gate.validate_severity_transition`, now takes a typed
  `SeverityDowngradeAttestation` (owner principal + evidence reference, checked with the same
  `require_owner_principal` the proposal queue uses);
* the blocker registry validator that CI runs (`validate_blocker_registry.py --base-ref`), which
  compares the register and the OPEN_BLOCKERS.md table with the base branch and fails a pull
  request that lowers, or deletes, a blocker without an entry in SEVERITY_ATTESTATIONS.md.
"""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

import pytest
import yaml

_REPO = Path(__file__).resolve().parents[2]
_SCRIPT_DIR = _REPO / "30_SCRIPTS" / "verification"
for _p in (str(_SCRIPT_DIR), str(Path(__file__).parent)):
    if _p not in sys.path:
        sys.path.insert(0, _p)

from cognitive_core.authorizer import Principal  # noqa: E402
from security.trust_gate import (  # noqa: E402
    SeverityAttestationError,
    SeverityDowngradeAttestation,
    validate_severity_transition,
    verify_severity_attestation,
)
from test_blocker_registry_validator import _base_record, _base_transition  # noqa: E402
from validate_blocker_registry import (  # noqa: E402
    main as validator_main,
    parse_attestation_ledger,
    parse_open_blockers_severities,
    validate_registry_and_history,
)


def _attestation(**overrides) -> SeverityDowngradeAttestation:
    fields = dict(
        principal=Principal.HUMAN,
        evidence_ref="PR #999 review, owner decision 2026-10-08",
        attested_by="owner",
        blocker_id="B-0001",
        from_severity="HARD_BLOCKER",
        to_severity="WARNING",
    )
    fields.update(overrides)
    return SeverityDowngradeAttestation(**fields)


# ---------------------------------------------------------------------------
# Library gate
# ---------------------------------------------------------------------------

def test_downgrade_without_attestation_fails():
    assert validate_severity_transition("HARD_BLOCKER", "WARNING") is False
    assert validate_severity_transition("HARD_BLOCKER", "SOFT_BLOCKER") is False
    assert validate_severity_transition("CRITICAL", "HIGH") is False
    assert validate_severity_transition("P0", "P1") is False


@pytest.mark.parametrize("principal", [Principal.HUMAN, Principal.ADMIN])
def test_downgrade_with_valid_owner_attestation_passes(principal):
    att = _attestation(principal=principal)
    assert validate_severity_transition("HARD_BLOCKER", "WARNING", attestation=att, blocker_id="B-0001") is True
    assert verify_severity_attestation(att, "B-0001", "HARD_BLOCKER", "WARNING") == principal.value


def test_upgrade_is_always_allowed():
    assert validate_severity_transition("WARNING", "HARD_BLOCKER") is True
    assert validate_severity_transition("SOFT_BLOCKER", "HARD_BLOCKER") is True
    assert validate_severity_transition("HARD_BLOCKER", "HARD_BLOCKER") is True
    assert validate_severity_transition("INFO", "CRITICAL") is True


def test_ai_agent_principal_is_refused():
    att = _attestation(principal=Principal.AI_AGENT)
    assert validate_severity_transition("HARD_BLOCKER", "WARNING", attestation=att) is False
    with pytest.raises(SeverityAttestationError, match="not allowed to attest"):
        verify_severity_attestation(att, "B-0001", "HARD_BLOCKER", "WARNING")


@pytest.mark.parametrize("principal", ["human", "admin", "owner", None])
def test_untyped_principal_is_refused(principal):
    att = _attestation(principal=principal)
    assert validate_severity_transition("HARD_BLOCKER", "WARNING", attestation=att) is False


@pytest.mark.parametrize("evidence", ["", "   ", None])
def test_attestation_without_evidence_is_refused(evidence):
    att = _attestation(evidence_ref=evidence)
    assert validate_severity_transition("HARD_BLOCKER", "WARNING", attestation=att) is False


def test_attestation_is_not_reusable_for_another_finding_or_step():
    att = _attestation()
    assert validate_severity_transition("HARD_BLOCKER", "WARNING", attestation=att, blocker_id="B-0002") is False
    assert validate_severity_transition("HARD_BLOCKER", "INFO", attestation=att, blocker_id="B-0001") is False
    assert validate_severity_transition("CRITICAL", "WARNING", attestation=att, blocker_id="B-0001") is False


def test_a_non_attestation_object_is_refused():
    assert validate_severity_transition("HARD_BLOCKER", "WARNING", attestation={"principal": "human"}) is False


def test_unknown_labels_are_refused():
    assert validate_severity_transition("HARD_BLOCKER", "whatever") is False
    assert validate_severity_transition("whatever", "WARNING") is False


# ---------------------------------------------------------------------------
# Registry validator (in-memory)
# ---------------------------------------------------------------------------

def _registry(severity: str, blocker_id: str = "B-0001"):
    rec = _base_record(blocker_id=blocker_id, status="OPEN", severity=severity)
    tr = _base_transition(blocker_id=blocker_id)
    return [rec], [tr]


def test_registry_downgrade_without_attestation_fails():
    base, _ = _registry("HARD_BLOCKER")
    cur, trs = _registry("WARNING")
    res = validate_registry_and_history(cur, trs, base_records=base, attestations=[])
    assert not res["valid"]
    assert any("severity_downgrade_without_attestation:HARD_BLOCKER->WARNING" in e for e in res["errors"])


def test_registry_downgrade_with_valid_owner_attestation_passes():
    base, _ = _registry("HARD_BLOCKER")
    cur, trs = _registry("WARNING")
    res = validate_registry_and_history(cur, trs, base_records=base, attestations=[_attestation()])
    assert res["valid"], res["errors"]


def test_registry_upgrade_always_passes():
    base, _ = _registry("WARNING")
    cur, trs = _registry("HARD_BLOCKER")
    res = validate_registry_and_history(cur, trs, base_records=base, attestations=[])
    assert res["valid"], res["errors"]


def test_registry_hard_to_soft_is_also_a_downgrade():
    base, _ = _registry("HARD_BLOCKER")
    cur, trs = _registry("SOFT_BLOCKER")
    res = validate_registry_and_history(cur, trs, base_records=base, attestations=[])
    assert any("severity_downgrade_without_attestation" in e for e in res["errors"])


def test_registry_attestation_for_another_blocker_or_step_does_not_count():
    base, _ = _registry("HARD_BLOCKER")
    cur, trs = _registry("WARNING")
    for att in (_attestation(blocker_id="B-0099"), _attestation(to_severity="SOFT_BLOCKER")):
        res = validate_registry_and_history(cur, trs, base_records=base, attestations=[att])
        assert any("severity_downgrade_without_attestation" in e for e in res["errors"])


def test_registry_with_ai_agent_attestation_fails():
    base, _ = _registry("HARD_BLOCKER")
    cur, trs = _registry("WARNING")
    res = validate_registry_and_history(
        cur, trs, base_records=base, attestations=[_attestation(principal=Principal.AI_AGENT)]
    )
    assert not res["valid"]
    assert any("severity_downgrade_without_attestation" in e and "not allowed to attest" in e for e in res["errors"])


def test_registry_deleting_a_blocker_is_refused():
    base, _ = _registry("HARD_BLOCKER", "B-0001")
    other, trs = _registry("HARD_BLOCKER", "B-0002")
    res = validate_registry_and_history(other, trs, base_records=base, attestations=[])
    assert any("B-0001:blocker_removed_without_attestation" in e for e in res["errors"])


def test_registry_without_base_behaves_as_before():
    cur, trs = _registry("WARNING")
    assert validate_registry_and_history(cur, trs)["valid"]


# ---------------------------------------------------------------------------
# Ledger and OPEN_BLOCKERS.md parsing
# ---------------------------------------------------------------------------

_LEDGER_ENTRY = """```yaml
attestation_id: SA-0001
blocker_id: B-0001
from_severity: HARD_BLOCKER
to_severity: WARNING
principal: {principal}
attested_by: owner
evidence_ref: PR #999
attested_at: '2026-10-08T10:00:00Z'
```
"""


def test_ledger_entry_by_owner_is_accepted():
    atts, errs = parse_attestation_ledger("# Ledger\n\n" + _LEDGER_ENTRY.format(principal="human"))
    assert errs == []
    assert len(atts) == 1 and atts[0].blocker_id == "B-0001"


@pytest.mark.parametrize("principal", ["ai_agent", "system", "bot"])
def test_ledger_entry_by_non_owner_is_an_error(principal):
    atts, errs = parse_attestation_ledger(_LEDGER_ENTRY.format(principal=principal))
    assert atts == []
    assert errs and "invalid_severity_attestation" in errs[0]


def test_ledger_entry_with_missing_fields_is_an_error():
    atts, errs = parse_attestation_ledger("```yaml\nattestation_id: SA-0002\nblocker_id: B-0001\n```\n")
    assert atts == [] and "attestation_missing_fields" in errs[0]


def test_empty_ledger_has_no_entries():
    assert parse_attestation_ledger("# Ledger\n\nNo downgrade has been attested so far.\n") == ([], [])


_OPEN_BLOCKERS = """# Open blockers

| Blocker ID | Severity | Category | Status |
|---|---|---|---|
| **B-POLICY-01** | **CRITICAL** | Governance | `CLOSED` |
| **B-SOURCE-05** | **{sev}** | Security Gate | `OPEN` |

Other text.

| Finding | Gap | State |
|---|---|---|
| B01 | x | y |
"""


def test_open_blockers_table_is_parsed():
    sev = parse_open_blockers_severities(_OPEN_BLOCKERS.format(sev="MEDIUM"))
    assert sev == {"B-POLICY-01": "CRITICAL", "B-SOURCE-05": "MEDIUM"}


def test_real_open_blockers_table_is_parsed():
    sev = parse_open_blockers_severities((_REPO / "08_RESEARCH/BOOK_TO_MEMORY/OPEN_BLOCKERS.md").read_text(encoding="utf-8"))
    assert sev["B-POLICY-01"] == "CRITICAL" and "B-SOURCE-05" in sev


# ---------------------------------------------------------------------------
# End to end: the CLI that CI runs, in a throw-away git repository
# ---------------------------------------------------------------------------

def _git(repo: Path, *args: str) -> None:
    subprocess.run(
        ["git", "-c", "user.name=t", "-c", "user.email=t@example.invalid", "-C", str(repo), *args],
        check=True, capture_output=True,
    )


def _write_registry(repo: Path, severity: str, open_sev: str = "CRITICAL") -> None:
    d = repo / "reg"
    d.mkdir(exist_ok=True)
    recs, trs = _registry(severity)
    (d / "BLOCKER_REGISTER.md").write_text(
        "# Register\n\n```yaml\n" + yaml.safe_dump(recs[0], sort_keys=True) + "```\n", encoding="utf-8"
    )
    (d / "BLOCKER_HISTORY.md").write_text(
        "# History\n\n```yaml\n" + yaml.safe_dump(trs[0], sort_keys=True) + "```\n", encoding="utf-8"
    )
    (d / "OPEN_BLOCKERS.md").write_text(_OPEN_BLOCKERS.format(sev=open_sev), encoding="utf-8")


@pytest.fixture
def repo(tmp_path):
    _git(tmp_path, "init", "-q")
    _write_registry(tmp_path, "HARD_BLOCKER", open_sev="CRITICAL")
    _git(tmp_path, "add", "-A")
    _git(tmp_path, "commit", "-q", "-m", "base")
    return tmp_path


def _run(repo: Path, capsys, *extra: str) -> tuple[int, str]:
    argv = [
        "--register", str(repo / "reg/BLOCKER_REGISTER.md"),
        "--history", str(repo / "reg/BLOCKER_HISTORY.md"),
        "--open-blockers", str(repo / "reg/OPEN_BLOCKERS.md"),
        "--attestations", str(repo / "reg/SEVERITY_ATTESTATIONS.md"),
        *extra,
    ]
    code = validator_main(argv)
    return code, capsys.readouterr().out


def test_cli_unchanged_registry_passes(repo, capsys):
    code, out = _run(repo, capsys, "--base-ref", "HEAD")
    assert code == 0, out


def test_cli_register_downgrade_without_attestation_fails_ci(repo, capsys):
    _write_registry(repo, "WARNING")
    code, out = _run(repo, capsys, "--base-ref", "HEAD")
    assert code == 1
    assert "register:B-0001:severity_downgrade_without_attestation:HARD_BLOCKER->WARNING" in out


def test_cli_open_blockers_downgrade_without_attestation_fails_ci(repo, capsys):
    _write_registry(repo, "HARD_BLOCKER", open_sev="LOW")  # B-SOURCE-05 CRITICAL -> LOW
    code, out = _run(repo, capsys, "--base-ref", "HEAD")
    assert code == 1
    assert "open_blockers:B-SOURCE-05:severity_downgrade_without_attestation:CRITICAL->LOW" in out


def test_cli_open_blockers_downgrade_with_attestation_passes(repo, capsys):
    _write_registry(repo, "HARD_BLOCKER", open_sev="LOW")
    entry = _LEDGER_ENTRY.format(principal="admin").replace("B-0001", "B-SOURCE-05").replace(
        "from_severity: HARD_BLOCKER", "from_severity: CRITICAL"
    ).replace("to_severity: WARNING", "to_severity: LOW")
    (repo / "reg/SEVERITY_ATTESTATIONS.md").write_text(entry, encoding="utf-8")
    code, out = _run(repo, capsys, "--base-ref", "HEAD")
    assert code == 0, out


def test_cli_downgrade_with_owner_attestation_passes(repo, capsys):
    _write_registry(repo, "WARNING")
    entry = _LEDGER_ENTRY.format(principal="human")
    (repo / "reg/SEVERITY_ATTESTATIONS.md").write_text("# Ledger\n\n" + entry, encoding="utf-8")
    code, out = _run(repo, capsys, "--base-ref", "HEAD")
    assert code == 0, out


def test_cli_downgrade_with_ai_agent_attestation_fails(repo, capsys):
    _write_registry(repo, "WARNING")
    entry = _LEDGER_ENTRY.format(principal="ai_agent")
    (repo / "reg/SEVERITY_ATTESTATIONS.md").write_text("# Ledger\n\n" + entry, encoding="utf-8")
    code, out = _run(repo, capsys, "--base-ref", "HEAD")
    assert code == 1
    assert "not allowed to attest" in out


def test_cli_ai_agent_ledger_entry_fails_even_without_a_downgrade(repo, capsys):
    (repo / "reg/SEVERITY_ATTESTATIONS.md").write_text(_LEDGER_ENTRY.format(principal="ai_agent"), encoding="utf-8")
    code, out = _run(repo, capsys, "--base-ref", "HEAD")
    assert code == 1
    assert "invalid_severity_attestation" in out


def test_cli_upgrade_passes_without_attestation(tmp_path, capsys):
    _git(tmp_path, "init", "-q")
    _write_registry(tmp_path, "WARNING", open_sev="LOW")
    _git(tmp_path, "add", "-A")
    _git(tmp_path, "commit", "-q", "-m", "base")
    _write_registry(tmp_path, "HARD_BLOCKER", open_sev="CRITICAL")
    code, out = _run(tmp_path, capsys, "--base-ref", "HEAD")
    assert code == 0, out


def test_cli_unknown_base_ref_fails_closed(repo, capsys):
    code, out = _run(repo, capsys, "--base-ref", "no-such-ref")
    assert code == 1
    assert "base ref not found" in out


def test_cli_new_files_have_no_baseline(tmp_path, capsys):
    _git(tmp_path, "init", "-q")
    (tmp_path / "x.txt").write_text("x", encoding="utf-8")
    _git(tmp_path, "add", "-A")
    _git(tmp_path, "commit", "-q", "-m", "base")
    _write_registry(tmp_path, "WARNING")
    code, out = _run(tmp_path, capsys, "--base-ref", "HEAD")
    assert code == 0, out


def test_ci_runs_the_downgrade_gate_on_pull_requests():
    wf = (_REPO / ".github/workflows/repository-hygiene.yml").read_text(encoding="utf-8")
    assert "validate_blocker_registry.py --base-ref" in wf
    assert "github.base_ref" in wf
