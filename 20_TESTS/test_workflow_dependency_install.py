"""A workflow that runs project code must install the project's dependencies first.

The nightly consolidation workflow ran ``python -m cognitive_core.memory_v6_cli``
on a bare runner and died on ``import jsonschema``. Its unit test did not see
it: the test suite runs in an environment that already has the dependencies
installed, which is exactly the environment the workflow does not have.

This test reads the workflow YAML instead of running it, so it does not depend
on the environment it happens to run in.
"""
from __future__ import annotations

import copy
import re
import unittest
from pathlib import Path

import yaml

REPO_ROOT = Path(__file__).resolve().parents[1]
WORKFLOWS_DIR = REPO_ROOT / ".github" / "workflows"
PACKAGES_DIR = REPO_ROOT / "03_IMPLEMENTATION" / "packages"

# `python -m <module>` where <module> is a package under 03_IMPLEMENTATION.
_PYTHON_M = re.compile(r"\bpython3?\s+-m\s+([A-Za-z_][\w.]*)")
# An install of the project's own dependency files (not a hand-written list).
_PROJECT_INSTALL = re.compile(r"\bpip\s+install\b[^\n]*(?:\s-e\s+\.(?:\s|$)|\s-r\s+requirements\S*\.txt)")


def _project_packages() -> set[str]:
    return {p.name for p in PACKAGES_DIR.iterdir() if (p / "__init__.py").exists()}


def _load(path: Path) -> dict:
    return yaml.safe_load(path.read_text(encoding="utf-8"))


def _step_texts(job: dict) -> list[str]:
    return [str(step.get("run", "")) for step in job.get("steps", [])]


def dependency_gaps(workflow: dict, packages: set[str]) -> list[str]:
    """Return "job:step" labels that run project code before any dependency install."""
    gaps: list[str] = []
    for job_name, job in (workflow.get("jobs") or {}).items():
        installed = False
        for step in job.get("steps", []):
            text = str(step.get("run", ""))
            first_install = _PROJECT_INSTALL.search(text)
            for m in _PYTHON_M.finditer(text):
                if m.group(1).split(".")[0] not in packages:
                    continue
                installed_before = installed or (first_install and first_install.start() < m.start())
                if not installed_before:
                    gaps.append(f"{job_name}:{step.get('name', '<unnamed>')}")
            if first_install:
                installed = True
    return gaps


def project_code_steps(workflow: dict, packages: set[str]) -> int:
    n = 0
    for job in (workflow.get("jobs") or {}).values():
        for text in _step_texts(job):
            n += sum(1 for m in _PYTHON_M.finditer(text) if m.group(1).split(".")[0] in packages)
    return n


class TestWorkflowDependencyInstall(unittest.TestCase):
    def setUp(self):
        self.packages = _project_packages()

    def test_consolidation_workflow_installs_dependencies_first(self):
        wf = _load(WORKFLOWS_DIR / "memory-consolidation.yml")
        # Guard against a vacuous pass: the detector must actually see the project step.
        self.assertGreaterEqual(project_code_steps(wf, self.packages), 1)
        self.assertEqual(dependency_gaps(wf, self.packages), [])

    def test_install_step_uses_repository_dependency_files(self):
        wf = _load(WORKFLOWS_DIR / "memory-consolidation.yml")
        text = "\n".join(t for job in wf["jobs"].values() for t in _step_texts(job))
        self.assertRegex(text, r"pip install -e \.")
        m = re.search(r"pip install -r (\S+)", text)
        self.assertIsNotNone(m, "no `pip install -r <requirements file>` step")
        self.assertTrue((REPO_ROOT / m.group(1)).is_file(), f"{m.group(1)} does not exist")

    def test_negative_control_without_install_step_fails(self):
        wf = _load(WORKFLOWS_DIR / "memory-consolidation.yml")
        stripped = copy.deepcopy(wf)
        for job in stripped["jobs"].values():
            job["steps"] = [s for s in job["steps"] if not _PROJECT_INSTALL.search(str(s.get("run", "")))]
        self.assertNotEqual(
            len(stripped["jobs"]["consolidate"]["steps"]), len(wf["jobs"]["consolidate"]["steps"]),
            "control did not remove anything",
        )
        self.assertNotEqual(dependency_gaps(stripped, self.packages), [])

    def test_negative_control_install_after_use_fails(self):
        wf = _load(WORKFLOWS_DIR / "memory-consolidation.yml")
        reordered = copy.deepcopy(wf)
        steps = reordered["jobs"]["consolidate"]["steps"]
        idx = next(i for i, s in enumerate(steps) if _PROJECT_INSTALL.search(str(s.get("run", ""))))
        steps.append(steps.pop(idx))
        self.assertNotEqual(dependency_gaps(reordered, self.packages), [])

    def test_no_workflow_runs_project_code_without_installing(self):
        offenders = {}
        for path in sorted(WORKFLOWS_DIR.glob("*.yml")):
            gaps = dependency_gaps(_load(path), self.packages)
            if gaps:
                offenders[path.name] = gaps
        self.assertEqual(offenders, {})


if __name__ == "__main__":
    unittest.main()
