"""Workflow policy checks against the REAL .github/workflows files (F-01, F-03)."""
from __future__ import annotations

import importlib.util
from pathlib import Path

import pytest
import yaml

ROOT = Path(__file__).resolve().parents[1]
WORKFLOWS = ROOT / ".github" / "workflows"
FILES = sorted(WORKFLOWS.glob("*.y*ml"))

_spec = importlib.util.spec_from_file_location("wsa_real", ROOT / "30_SCRIPTS" / "verification" / "workflow_security_audit.py")
audit = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(audit)

# (workflow file name, job id) pairs allowed to keep persisted credentials. Must stay empty
# unless a job genuinely pushes with the checkout token.
PERSIST_CREDENTIALS_ALLOWLIST: set[tuple[str, str]] = set()


def _load(path: Path) -> dict:
    return yaml.safe_load(path.read_text(encoding="utf-8")) or {}


def test_real_workflows_exist():
    assert FILES


@pytest.mark.parametrize("path", FILES, ids=lambda p: p.name)
def test_real_workflow_has_no_audit_findings(path):
    assert audit.audit_file(path) == []


def test_audit_all_is_clean():
    assert audit.audit_all() == {}


def test_security_boundary_audits_workflow_changes_on_pull_request():
    on = _load(WORKFLOWS / "security-boundary.yml")
    on = on.get("on", on.get(True))
    paths = on["pull_request"]["paths"]
    assert ".github/workflows/**" in paths
    assert "30_SCRIPTS/verification/workflow_security_audit.py" in paths


@pytest.mark.parametrize("path", FILES, ids=lambda p: p.name)
def test_every_checkout_step_disables_persisted_credentials(path):
    for job_id, job in (_load(path).get("jobs") or {}).items():
        for step in (job or {}).get("steps") or []:
            if str(step.get("uses", "")).startswith("actions/checkout@"):
                if (path.name, job_id) in PERSIST_CREDENTIALS_ALLOWLIST:
                    continue
                assert (step.get("with") or {}).get("persist-credentials") is False, f"{path.name}:{job_id}"
